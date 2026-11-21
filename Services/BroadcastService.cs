using System.Text.Json;
using AneedWeb.Data;

namespace AneedWeb.Services;

public record Track(long? SamSongId, string Title, string Artist, string Album, string? Artwork, string? Picture, DateTimeOffset? Started);
public record Broadcast(
    bool Playing,
    Track? Current,
    IReadOnlyList<Track> Recent,
    string? Error,
    string Source = "Unavailable",
    DateTimeOffset? LastSuccessfulRefresh = null,
    bool IsStale = false);

public sealed class BroadcastService(
    IWebHostEnvironment environment,
    ILogger<BroadcastService> logger,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory)
{
    private const string HistoryRelativePath = "sam/history.json";
    private const string DefaultDevelopmentHistoryUrl = "https://aneedstatic.live/sam/history.json";
    private static readonly TimeSpan SuccessCacheDuration = TimeSpan.FromSeconds(20);

    private static readonly HashSet<string> AllowedPictureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp"
    };

    private readonly AzureSqlCatalogRepository catalog = new(configuration);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HttpClient http = httpClientFactory.CreateClient();
    private readonly string? developmentHistoryUrl = configuration["ANEED_SAM_HISTORY_URL"];

    private Broadcast snapshot = new(false, null, [], null);
    private DateTimeOffset refreshed;
    private DateTimeOffset? lastSuccessfulRefresh;

    public async Task<Broadcast> Get()
    {
        await gate.WaitAsync();
        try
        {
            if (snapshot.Error is null && DateTimeOffset.UtcNow - refreshed < SuccessCacheDuration)
                return snapshot;

            snapshot = await LoadSnapshot();
            refreshed = DateTimeOffset.UtcNow;
            return snapshot;
        }
        finally
        {
            gate.Release();
        }
    }

    public static Broadcast ParseHistorySnapshot(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var history = root.ValueKind == JsonValueKind.Array
            ? root
            : TryGetPropertyIgnoreCase(root, "history", out var nested) && nested.ValueKind == JsonValueKind.Array
                ? nested
                : default;

        if (history.ValueKind != JsonValueKind.Array)
            return new(false, null, [], "SAM history response does not contain a usable history array.");

        var tracks = history.EnumerateArray().Select(ReadTrack).Where(static track => track is not null).Cast<Track>().ToArray();
        if (tracks.Length == 0)
            return new(false, null, [], "SAM history response did not include a valid current track.");

        return new(true, tracks[0], tracks.Skip(1).Take(4).ToArray(), null);
    }

    public static string BuildArtworkFallback(string? mappedArtwork, string? picture)
    {
        if (!string.IsNullOrWhiteSpace(mappedArtwork))
            return mappedArtwork;

        var filename = ValidatePictureFilename(picture);
        if (filename is not null)
            return $"/media/covers/{Uri.EscapeDataString(filename)}";

        return "/images/station.png";
    }

    private async Task<Broadcast> LoadSnapshot()
    {
        var source = ResolveSource();
        logger.LogInformation("Refreshing SAM history from source {Source}", source.Label);

        try
        {
            var json = source.RemoteUri is not null
                ? await ReadRemoteJson(source.RemoteUri)
                : await ReadLocalJson(source.LocalFilePath!);

            var parsed = ParseHistorySnapshot(json) with { Source = source.Label };
            if (!parsed.Playing || parsed.Current is null)
                return Unavailable(source.Label, parsed.Error ?? "SAM history did not include a current track.");

            var mapped = await ApplyArtworkMappings(parsed);
            lastSuccessfulRefresh = DateTimeOffset.UtcNow;
            return mapped with { Source = source.Label, LastSuccessfulRefresh = lastSuccessfulRefresh, IsStale = false };
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            logger.LogWarning(ex, "SAM history refresh failed from source {Source}", source.Label);
            return Unavailable(source.Label, ex.Message);
        }
    }

    private Broadcast Unavailable(string source, string message)
        => new(false, null, [], message, source, lastSuccessfulRefresh, true);

    private async Task<string> ReadRemoteJson(Uri uri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType is not null && !contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            throw new JsonException($"Unexpected content type from SAM history source: {contentType}");

        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<string> ReadLocalJson(string path)
    {
        if (!File.Exists(path))
            throw new IOException($"SAM history metadata file was not found: {path}");

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private (string Label, Uri? RemoteUri, string? LocalFilePath) ResolveSource()
    {
        if (environment.IsDevelopment())
        {
            var configuredUrl = string.IsNullOrWhiteSpace(developmentHistoryUrl)
                ? DefaultDevelopmentHistoryUrl
                : developmentHistoryUrl.Trim();

            if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var remoteUri) ||
                (remoteUri.Scheme != Uri.UriSchemeHttps && remoteUri.Scheme != Uri.UriSchemeHttp))
            {
                throw new InvalidOperationException($"Invalid ANEED_SAM_HISTORY_URL value '{configuredUrl}'. Configure an absolute HTTP/HTTPS URL.");
            }

            return ($"HTTPS: {remoteUri}", remoteUri, null);
        }

        var path = Path.Combine(environment.WebRootPath, HistoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
        return ($"Local file: {path}", null, path);
    }

    private async Task<Broadcast> ApplyArtworkMappings(Broadcast broadcast)
    {
        var orderedTracks = new[] { broadcast.Current }.Concat(broadcast.Recent).Where(static track => track is not null).Cast<Track>().ToArray();
        if (orderedTracks.Length == 0)
            return broadcast;

        IReadOnlyDictionary<long, Song> songLookup = new Dictionary<long, Song>();
        if (catalog.Configured)
        {
            try
            {
                var samSongIds = orderedTracks.Where(track => track.SamSongId is > 0).Select(track => track.SamSongId!.Value);
                var songs = await catalog.BroadcastSongs(samSongIds);
                songLookup = songs.ToDictionary(song => song.Id);
            }
            catch (Microsoft.Data.SqlClient.SqlException ex)
            {
                logger.LogWarning(ex, "Failed to map SAM song IDs to SQL artwork. Using file/station fallback artwork.");
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(ex, "Failed to map SAM song IDs to SQL artwork. Using file/station fallback artwork.");
            }
        }

        static Track WithArtwork(Track track, IReadOnlyDictionary<long, Song> lookup)
        {
            string? mapped = null;
            if (track.SamSongId is long id && lookup.TryGetValue(id, out var song))
                mapped = song.Artwork;

            return track with { Artwork = BuildArtworkFallback(mapped, track.Picture) };
        }

        var current = broadcast.Current is null ? null : WithArtwork(broadcast.Current, songLookup);
        var recent = broadcast.Recent.Select(track => WithArtwork(track, songLookup)).ToArray();
        return broadcast with { Current = current, Recent = recent, Error = null };
    }

    private static Track? ReadTrack(JsonElement item)
    {
        string? ReadString(string name)
        {
            if (!TryGetPropertyIgnoreCase(item, name, out var value) || value.ValueKind != JsonValueKind.String)
                return null;

            return value.GetString()?.Trim();
        }

        long? samId = null;
        if (TryGetPropertyIgnoreCase(item, "id", out var idValue))
        {
            if (idValue.ValueKind == JsonValueKind.Number && idValue.TryGetInt64(out var numeric))
                samId = numeric;
            else if (idValue.ValueKind == JsonValueKind.String && long.TryParse(idValue.GetString(), out var parsed))
                samId = parsed;
        }

        var title = ReadString("title");
        var artist = ReadString("artist");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
            return null;

        var album = ReadString("album") ?? string.Empty;
        var picture = ReadString("picture");

        DateTimeOffset? started = null;
        if (TryGetPropertyIgnoreCase(item, "date", out var dateValue) && dateValue.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(dateValue.GetString(), out var parsedDate))
        {
            started = parsedDate;
        }
        else if (TryGetPropertyIgnoreCase(item, "started", out var startedValue) && startedValue.ValueKind == JsonValueKind.String &&
                 DateTimeOffset.TryParse(startedValue.GetString(), out var parsedStarted))
        {
            started = parsedStarted;
        }

        return new Track(samId, title, artist, album, null, picture, started);
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement item, string name, out JsonElement value)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in item.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? ValidatePictureFilename(string? picture)
    {
        if (string.IsNullOrWhiteSpace(picture))
            return null;

        var candidate = picture.Trim();
        if (!string.Equals(candidate, Path.GetFileName(candidate), StringComparison.Ordinal))
            return null;

        if (candidate.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;

        var extension = Path.GetExtension(candidate);
        return AllowedPictureExtensions.Contains(extension) ? candidate : null;
    }
}

using System.Text.Json;
using AneedWeb.Data;

namespace AneedWeb.Services;

public record Track(long? SamSongId, string Title, string Artist, string Album, string? Artwork, string? Picture, DateTimeOffset? Started);
public record Broadcast(bool Playing, Track? Current, IReadOnlyList<Track> Recent, string? Error);

public sealed class BroadcastService(IWebHostEnvironment environment, ILogger<BroadcastService> logger, IConfiguration configuration)
{
    private const string HistoryRelativePath = "sam/history.json";
    private static readonly HashSet<string> AllowedPictureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp"
    };

    private readonly AzureSqlCatalogRepository catalog = new(configuration);
    private readonly SemaphoreSlim gate = new(1, 1);
    private Broadcast snapshot = new(false, null, [], null);
    private DateTimeOffset refreshed;

    public async Task<Broadcast> Get()
    {
        await gate.WaitAsync();
        try
        {
            if (DateTimeOffset.UtcNow - refreshed < TimeSpan.FromSeconds(20))
                return snapshot;

            var updated = await LoadSnapshot();
            if (updated is not null)
                snapshot = updated;

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
            : root.TryGetProperty("history", out var nested) && nested.ValueKind == JsonValueKind.Array
                ? nested
                : default;

        if (history.ValueKind != JsonValueKind.Array)
            return new(false, null, [], null);

        var tracks = history.EnumerateArray().Select(ReadTrack).Where(static track => track is not null).Cast<Track>().ToArray();
        if (tracks.Length == 0)
            return new(false, null, [], null);

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

    private async Task<Broadcast?> LoadSnapshot()
    {
        var path = Path.Combine(environment.WebRootPath, HistoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            logger.LogWarning("SAM history metadata file was not found: {Path}", path);
            return null;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = await JsonDocument.ParseAsync(stream);
            var parsed = ParseHistorySnapshot(doc.RootElement.GetRawText());
            return await ApplyArtworkMappings(parsed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "SAM history metadata could not be read. Keeping last valid broadcast snapshot.");
            return null;
        }
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
        string? ReadString(string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

        long? samId = null;
        if (item.TryGetProperty("id", out var idValue))
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
        return new Track(samId, title, artist, album, null, picture, null);
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

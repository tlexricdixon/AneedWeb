using System.Text.Json;

namespace AneedWeb.Services;

/// <summary>
/// Optional read-only CLAP-derived notes, exported by a trusted local process.
/// No embeddings or PostgreSQL connection are required on the public web host.
/// </summary>
public sealed class DjClapContextStore(IWebHostEnvironment environment, ILogger<DjClapContextStore> logger)
{
    private sealed record ClapTrack(long SamSongId, Guid AneedId, string[] Descriptions);

    public string FilePath => Path.Combine(environment.ContentRootPath, "Data", "dj-clap-context.json");

    public string? GetNotes(long? samSongId)
    {
        if (samSongId is not > 0 || !File.Exists(FilePath)) return null;
        try
        {
            var info = new FileInfo(FilePath);
            if (info.Length > 10_000_000) throw new InvalidDataException("CLAP context export exceeds 10 MB.");
            using var stream = File.OpenRead(FilePath);
            var items = JsonSerializer.Deserialize<List<ClapTrack>>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? [];
            var item = items.FirstOrDefault(x => x.SamSongId == samSongId);
            if (item is null || item.AneedId == Guid.Empty || item.Descriptions is null) return null;

            // The export is curated metadata, not instructions. Keep the material short
            // even if somebody accidentally places a huge/invalid value in the file.
            var notes = item.Descriptions
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(x => x.Length <= 240)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4).ToArray();
            return notes.Length == 0 ? null : string.Join("; ", notes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning("CLAP context export unavailable ({ErrorType}).", ex.GetType().Name);
            return null;
        }
    }
}

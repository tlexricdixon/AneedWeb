
using System.Text;
using System.Text.Json;

namespace AneedWeb.Data;

// Public website models. Song IDs use stable SAM IDs; artist IDs are deterministic catalog identities.
public record Artist(long Id, string Name, string Biography, string? Image);
public record Song(long Id, string Title, long ArtistId, string Artist, string Album, int? Year, string Description, string? Artwork, string Genre, string Composer, string Label, string Isrc);
public record ArtistEditorialDraft(string Body, string Status, string Model, string PromptVersion, string SourceUrl, string LicenseNotes);
public record ArtistContentDraft(long ArtistId, string ArtistName, string Summary, string SourceUrl, string LicenseNotes, string ReviewStatus);
public record FeedPost(long Id, string Type, string Title, string Excerpt, string Body, string? Image, DateTime Published);

public class LibraryRepository(IWebHostEnvironment environment)
{
    private readonly AzureSqlCatalogRepository catalog = new();

    public bool Configured => catalog.Configured;
    public bool PostgresConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANEED_POSTGRES"));

    public Task<List<Song>> Songs(string query = "", long? id = null, long? artist = null, int page = 1, int pageSize = 100) =>
        catalog.Songs(query, id, artist, page, pageSize);

    public Task<List<Song>> BroadcastSongs(IEnumerable<Services.Track> tracks) => catalog.BroadcastSongs(tracks);
    public Task<List<Artist>> Artists(string query = "", long? id = null) => catalog.Artists(query, id);
    public Task<(List<Artist> Artists, long Total)> ArtistDirectory(string letter, int page = 1, int pageSize = 30) => catalog.ArtistDirectory(letter, page, pageSize);
    public Task<List<(string Name, int Tracks, int? Year)>> ArtistAlbums(long artistId) => catalog.ArtistAlbums(artistId);
    public Task<List<Song>> AlbumSongs(long artistId, string album) => catalog.AlbumSongs(artistId, album);

    //private async Task<NpgsqlConnection> OpenPostgresAsync()
    //{
    //    if (!PostgresConfigured)
    //        throw new InvalidOperationException("PostgreSQL editorial database is not configured.");

    //    var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable("ANEED_POSTGRES"));
    //    try
    //    {
    //        await connection.OpenAsync();
    //        return connection;
    //    }
    //    catch
    //    {
    //        await connection.DisposeAsync();
    //        throw;
    //    }
    //}

    //public async Task<ArtistContentDraft?> ArtistContentPreview(long artistId)
    //{
    //    await using var connection = await OpenPostgresAsync();
    //    await using var command = new NpgsqlCommand("""
    //        SELECT a.id, a.name, c.summary, c.summary_source_url,
    //               c.license_notes, c.review_status
    //        FROM music.artist_content c
    //        JOIN music.artists a ON a.id = c.artist_id
    //        WHERE c.artist_id = @id
    //        """, connection);
    //    command.Parameters.AddWithValue("id", artistId);
    //    await using var reader = await command.ExecuteReaderAsync();
    //    if (!await reader.ReadAsync()) return null;
    //    return new ArtistContentDraft(
    //        reader.GetInt64(0),
    //        reader.GetString(1),
    //        reader.IsDBNull(2) ? "" : reader.GetString(2),
    //        reader.IsDBNull(3) ? "" : reader.GetString(3),
    //        reader.IsDBNull(4) ? "" : reader.GetString(4),
    //        reader.GetString(5));
    //}

    //public async Task<ArtistEditorialDraft?> ArtistEditorialPreview(long artistId)
    //{
    //    await using var connection = await OpenPostgresAsync();
    //    await using var command = new NpgsqlCommand("""
    //        SELECT body, status, model_name, prompt_version, source_url, license_notes
    //        FROM music.artist_editorial WHERE artist_id = @id
    //        """, connection);
    //    command.Parameters.AddWithValue("id", artistId);
    //    await using var reader = await command.ExecuteReaderAsync();
    //    if (!await reader.ReadAsync()) return null;
    //    return new ArtistEditorialDraft(reader.GetString(0), reader.GetString(1),
    //        reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5));
    //}

    public async Task<List<FeedPost>> Posts(long? id = null)
    {
        var path = Path.Combine(environment.ContentRootPath, "Data", "feed.json");
        if (!File.Exists(path)) return [];
        await using var stream = File.OpenRead(path);
        var posts = await JsonSerializer.DeserializeAsync<List<FeedPost>>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        return posts.Where(p => p.Published <= DateTime.UtcNow && (id is null || p.Id == id))
            .OrderByDescending(p => p.Published).Take(6).ToList();
    }
}

using MySqlConnector;
using System.Text.Json;
using AneedWeb.Services;
namespace AneedWeb.Data;

public record Artist(long Id, string Name, string Biography, string? Image);
public record Song(long Id, string Title, long ArtistId, string Artist, string Album, int? Year, string Description, string? Artwork, string Genre, string Composer, string Label, string Isrc);
public record FeedPost(long Id, string Type, string Title, string Excerpt, string Body, string? Image, DateTime Published);

// Read-only adapter for the existing SAMDB.songlist table. No migrations or writes.
public class LibraryRepository(IConfiguration config, IWebHostEnvironment environment)
{
    public bool Configured => !string.IsNullOrWhiteSpace(config.GetConnectionString("Library"));
    private async Task<MySqlConnection> Open()
    {
        if (!Configured) throw new InvalidOperationException("Library is not configured.");
        var connection = new MySqlConnection(config.GetConnectionString("Library"));
        try { await connection.OpenAsync(); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }
    private static string Text(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i);
    // SAM pictures may be Windows filenames. Only web URLs are suitable for the browser.
    private static string? Artwork(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http") ? value : null;
    private static Song ReadSong(MySqlDataReader r) => new(
        r.GetInt64(0), Text(r,1), r.GetInt64(0), Text(r,2), Text(r,3),
        int.TryParse(Text(r,4),out var year) && year>0 ? year : null,
        Text(r,5), Artwork(Text(r,6)), Text(r,7), Text(r,8), Text(r,9), Text(r,10));
    private const string SongSelect = "SELECT s.ID,s.title,s.artist,s.album,s.albumyear,s.info,s.picture,s.genre,s.composer,s.label,s.ISRC FROM songlist s ";

    public async Task<List<Song>> Songs(string query = "", long? id = null, long? artist = null)
    {
        await using var c = await Open();
        // Artist URLs use a representative song ID because SAM has no artist primary key.
        await using var cmd = new MySqlCommand(SongSelect + "WHERE s.songtype='S' AND (@id IS NULL OR s.ID=@id) AND (@artist IS NULL OR s.artist=(SELECT artist FROM songlist WHERE ID=@artist)) AND (@q='' OR LOCATE(@q,s.title)>0 OR LOCATE(@q,s.artist)>0) ORDER BY s.artist,s.title,s.ID LIMIT 100", c);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@artist", artist);
        cmd.Parameters.AddWithValue("@q", query.Trim());
        await using var r = await cmd.ExecuteReaderAsync();
        var result = new List<Song>();
        while (await r.ReadAsync()) result.Add(ReadSong(r));
        return result;
    }

    public async Task<List<Song>> BroadcastSongs(IEnumerable<Track> tracks)
    {
        var items = tracks.Take(11).ToArray();
        if (items.Length == 0 || !Configured) return [];
        await using var c = await Open();
        var conditions = items.Select((_, i) => $"(s.title=@title{i} AND s.artist=@artist{i})");
        await using var cmd = new MySqlCommand(SongSelect + "WHERE s.songtype='S' AND (" + string.Join(" OR ", conditions) + ")", c);
        for (var i = 0; i < items.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@title{i}", items[i].Title);
            cmd.Parameters.AddWithValue($"@artist{i}", items[i].Artist);
        }
        await using var r = await cmd.ExecuteReaderAsync();
        var result = new List<Song>();
        while (await r.ReadAsync()) result.Add(ReadSong(r));
        return result;
    }

    public async Task<List<Artist>> Artists(string query = "", long? id = null)
    {
        await using var c = await Open();
        await using var cmd = new MySqlCommand("SELECT MIN(s.ID),s.artist,COUNT(*),COUNT(DISTINCT NULLIF(s.album,'')),MAX(s.picture) FROM songlist s WHERE s.songtype='S' AND s.artist<>'' AND (@id IS NULL OR s.artist=(SELECT artist FROM songlist WHERE ID=@id)) AND (@q='' OR LOCATE(@q,s.artist)>0) GROUP BY s.artist ORDER BY s.artist LIMIT 100",c);
        cmd.Parameters.AddWithValue("@id",id);
        cmd.Parameters.AddWithValue("@q",query.Trim());
        await using var r = await cmd.ExecuteReaderAsync();
        var result = new List<Artist>();
        while (await r.ReadAsync()) result.Add(new(r.GetInt64(0),Text(r,1),$"{r.GetInt64(2)} tracks across {r.GetInt64(3)} albums in the Aneed library. Explore their music below.",Artwork(Text(r,4))));
        return result;
    }

    public async Task<List<FeedPost>> Posts(long? id = null)
    {
        // SAM has no editorial feed table. Keep this unified model outside the broadcaster DB.
        var path = Path.Combine(environment.ContentRootPath, "Data", "feed.json");
        if (!File.Exists(path)) return [];
        await using var stream = File.OpenRead(path);
        var posts = await JsonSerializer.DeserializeAsync<List<FeedPost>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        return posts.Where(p => p.Published <= DateTime.UtcNow && (id is null || p.Id == id)).OrderByDescending(p=>p.Published).Take(6).ToList();
    }
}

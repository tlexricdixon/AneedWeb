using Npgsql;
using AneedWeb.Services;
using System.Text.Json;
namespace AneedWeb.Data;

// Public website models. Song IDs use stable SAM IDs; artist IDs use music.artists.id.
public record Artist(long Id, string Name, string Biography, string? Image);
public record Song(long Id, string Title, long ArtistId, string Artist, string Album, int? Year, string Description, string? Artwork, string Genre, string Composer, string Label, string Isrc);
public record FeedPost(long Id, string Type, string Title, string Excerpt, string Body, string? Image, DateTime Published);

// Read-only PostgreSQL catalog. No direct dependency on SAM MariaDB.
public class LibraryRepository(IWebHostEnvironment environment)
{
    public bool Configured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANEED_POSTGRES"));
    private async Task<NpgsqlConnection> Open()
    {
        if (!Configured) throw new InvalidOperationException("PostgreSQL catalog is not configured.");
        var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable("ANEED_POSTGRES"));
        try { await connection.OpenAsync(); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    // Fields not yet present in catalog_tracks are deliberately blank, not fabricated.
    // Preserve the public route /songs/{sam_song_id} and link to stable artist IDs.
    private const string SongSelect = """
        SELECT c.sam_song_id::bigint, c.title, coalesce(c.artist_id,0), c.artist,
               c.album, c.album_year, c.genre, coalesce(c.isrc,'')
        FROM music.catalog_tracks c
        """;
    private static Song ReadSong(NpgsqlDataReader r) => new(
        r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetString(3),
        r.GetString(4), r.IsDBNull(5) ? null : r.GetInt32(5),
        "", null, r.GetString(6), "", "", r.GetString(7));

    public async Task<List<Song>> Songs(string query = "", long? id = null, long? artist = null)
    {
        await using var c = await Open();
        await using var cmd = new NpgsqlCommand(SongSelect + """
            WHERE (@id IS NULL OR c.sam_song_id = @id)
              AND (@artist IS NULL OR c.artist_id = @artist)
              AND (@q = '' OR strpos(lower(c.title), lower(@q)) > 0
                           OR strpos(lower(c.artist), lower(@q)) > 0)
            ORDER BY c.artist,c.title,c.sam_song_id LIMIT 100
            """, c);
        cmd.Parameters.Add("id", NpgsqlTypes.NpgsqlDbType.Bigint).Value = (object?)id ?? DBNull.Value;
        cmd.Parameters.Add("artist", NpgsqlTypes.NpgsqlDbType.Bigint).Value = (object?)artist ?? DBNull.Value;
        cmd.Parameters.AddWithValue("q", query.Trim());
        await using var r = await cmd.ExecuteReaderAsync();
        var songs = new List<Song>();
        while (await r.ReadAsync()) songs.Add(ReadSong(r));
        return songs;
    }

    public async Task<List<Song>> BroadcastSongs(IEnumerable<Track> tracks)
    {
        var items = tracks.Take(11).ToArray();
        if (items.Length == 0 || !Configured) return [];
        await using var c = await Open();
        var terms = items.Select((_, i) => $"(lower(c.title)=lower(@title{i}) AND lower(c.artist)=lower(@artist{i}))");
        await using var cmd = new NpgsqlCommand(SongSelect + " WHERE " + string.Join(" OR ", terms), c);
        for (int i = 0; i < items.Length; i++)
        {
            cmd.Parameters.AddWithValue($"title{i}", items[i].Title);
            cmd.Parameters.AddWithValue($"artist{i}", items[i].Artist);
        }
        await using var r = await cmd.ExecuteReaderAsync();
        var songs = new List<Song>();
        while (await r.ReadAsync()) songs.Add(ReadSong(r));
        return songs;
    }

    public async Task<List<Artist>> Artists(string query = "", long? id = null)
    {
        await using var c = await Open();
        await using var cmd = new NpgsqlCommand("""
            SELECT a.id,a.name,count(c.id),count(DISTINCT nullif(c.album,''))
            FROM music.artists a
            INNER JOIN music.catalog_tracks c ON c.artist_id=a.id
            WHERE (@id IS NULL OR a.id=@id)
              AND (@q='' OR strpos(lower(a.name),lower(@q))>0)
            GROUP BY a.id,a.name ORDER BY a.name LIMIT 100
            """, c);
        cmd.Parameters.Add("id", NpgsqlTypes.NpgsqlDbType.Bigint).Value = (object?)id ?? DBNull.Value;
        cmd.Parameters.AddWithValue("q", query.Trim());
        await using var r = await cmd.ExecuteReaderAsync();
        var results = new List<Artist>();
        while (await r.ReadAsync())
            results.Add(new Artist(r.GetInt64(0), r.GetString(1),
                $"{r.GetInt64(2)} tracks across {r.GetInt64(3)} albums in the Aneed library. Explore their music below.", null));
        return results;
    }

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

using AneedWeb.Services;
using Npgsql;
using System.Text;
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


    public async Task<List<Song>> Songs(
        string query = "",
        long? id = null,
        long? artist = null)
    {
        await using var connection = await Open();

        var sql = new StringBuilder(SongSelect);
        sql.AppendLine(" WHERE 1 = 1");

        await using var cmd = new NpgsqlCommand();
        cmd.Connection = connection;

        if (id.HasValue)
        {
            sql.AppendLine(" AND c.sam_song_id = @id");

            cmd.Parameters.AddWithValue(
                "id",
                NpgsqlTypes.NpgsqlDbType.Bigint,
                id.Value);
        }

        if (artist.HasValue)
        {
            sql.AppendLine(" AND c.artist_id = @artist");

            cmd.Parameters.AddWithValue(
                "artist",
                NpgsqlTypes.NpgsqlDbType.Bigint,
                artist.Value);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            sql.AppendLine("""
            AND (
                c.title ILIKE @search
                OR c.artist ILIKE @search
            )
            """);

            // Escape LIKE wildcards so search input is literal.
            var escaped = query.Trim()
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");

            cmd.Parameters.AddWithValue("search", $"%{escaped}%");
        }

        sql.AppendLine("""
        ORDER BY c.artist, c.title, c.sam_song_id
        LIMIT 100
        """);

        cmd.CommandText = sql.ToString();

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();

            var songs = new List<Song>();

            while (await reader.ReadAsync())
            {
                songs.Add(ReadSong(reader));
            }

            return songs;
        }
        catch (PostgresException ex)
        {
            Console.WriteLine($"SQL: {cmd.CommandText}");
            Console.WriteLine($"SQL State: {ex.SqlState}");
            Console.WriteLine($"Message: {ex.MessageText}");
            throw;
        }
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


    public async Task<(List<Artist> Artists, long Total)> ArtistDirectory(
        string letter, int page = 1, int pageSize = 30)
    {
        letter = (letter ?? "A").Trim().ToUpperInvariant();
        if (letter.Length != 1 || letter[0] < 'A' || letter[0] > 'Z')
            letter = "A";
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        await using var connection = await Open();
        const string filter = """
            FROM music.artists a
            WHERE EXISTS (SELECT 1 FROM music.catalog_tracks t WHERE t.artist_id = a.id)
              AND left(upper(a.name), 1) = @letter
            """;
        await using var count = new NpgsqlCommand("SELECT count(*) " + filter, connection);
        count.Parameters.AddWithValue("letter", letter);
        var total = (long)(await count.ExecuteScalarAsync() ?? 0L);

        await using var cmd = new NpgsqlCommand(
            "SELECT a.id, a.name " + filter +
            " ORDER BY a.name, a.id LIMIT @limit OFFSET @offset", connection);
        cmd.Parameters.AddWithValue("letter", letter);
        cmd.Parameters.AddWithValue("limit", pageSize);
        cmd.Parameters.AddWithValue("offset", (page - 1) * pageSize);
        var artists = new List<Artist>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            artists.Add(new Artist(reader.GetInt64(0), reader.GetString(1), "", null));
        return (artists, total);
    }

    public async Task<List<(string Name, int Tracks, int? Year)>> ArtistAlbums(long artistId)
    {
        await using var connection = await Open();
        await using var cmd = new NpgsqlCommand("""
            SELECT c.album, count(*)::integer, min(c.album_year)
            FROM music.catalog_tracks c
            WHERE c.artist_id = @artist AND btrim(c.album) <> ''
            GROUP BY c.album
            ORDER BY min(c.album_year) NULLS LAST, c.album
            """, connection);
        cmd.Parameters.AddWithValue("artist", artistId);
        var albums = new List<(string Name, int Tracks, int? Year)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            albums.Add((reader.GetString(0), reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2)));
        return albums;
    }

    public async Task<List<Song>> AlbumSongs(long artistId, string album)
    {
        await using var connection = await Open();
        await using var cmd = new NpgsqlCommand(SongSelect + "\n" + """
            WHERE c.artist_id = @artist AND c.album = @album
            ORDER BY c.track_number NULLS LAST, c.title, c.sam_song_id
            """, connection);
        cmd.Parameters.AddWithValue("artist", artistId);
        cmd.Parameters.AddWithValue("album", album);
        var songs = new List<Song>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) songs.Add(ReadSong(reader));
        return songs;
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

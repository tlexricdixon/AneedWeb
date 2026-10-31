using AneedWeb.Services;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Text;

namespace AneedWeb.Data;

public sealed class AzureSqlCatalogRepository
{
    private const int DefaultPageSize = 100;
    private readonly string? connectionString;

    public AzureSqlCatalogRepository(IConfiguration configuration)
    {
        connectionString = configuration.GetConnectionString(
            "ANEED_AZURE_SQL_CONNECTION");
    }

    public bool Configured => !string.IsNullOrWhiteSpace(connectionString);

    private async Task<SqlConnection> OpenAsync()
    {
        if (!Configured)
            throw new InvalidOperationException("Azure SQL catalog is not configured.");

        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync();
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private const string SongSelect = """
        SELECT [SamSongId], [Artist], [Title], [Album], [Genre], [AlbumYear], [Isrc]
        FROM [dbo].[Songs]
        """;

    private static Song ReadSong(SqlDataReader reader)
    {
        var artist = reader.IsDBNull(1) ? "" : reader.GetString(1);
        var title = reader.IsDBNull(2) ? "" : reader.GetString(2);
        var songId = ReadSongId(reader, artist, title);
        return new Song(
            songId,
            title,
            ArtistIdentity.FromName(artist),
            artist,
            reader.IsDBNull(3) ? "" : reader.GetString(3),
            ReadNullableInt(reader, 5),
            "",
            null,
            reader.IsDBNull(4) ? "" : reader.GetString(4),
            "",
            "",
            reader.IsDBNull(6) ? "" : reader.GetString(6));
    }

    private static long ReadSongId(SqlDataReader reader, string artist, string title)
    {
        if (reader.IsDBNull(0))
            throw new InvalidDataException(
                $"Catalog row has NULL SamSongId. Artist='{artist}', Title='{title}'.");

        var rawValue = reader.GetValue(0);
        var text = Convert.ToString(rawValue, CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException(
                $"Catalog row has an empty SamSongId. Artist='{artist}', Title='{title}'.");

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var songId))
            return songId;

        throw new InvalidDataException(
            $"Catalog row has an invalid SamSongId '{text}'. Artist='{artist}', Title='{title}'.");
    }

    private static int? ReadNullableInt(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return null;

        var value = reader.GetValue(ordinal);
        if (value is string text && string.IsNullOrWhiteSpace(text))
            return null;

        return Convert.ToInt32(value);
    }

    public async Task<List<Song>> Songs(string query = "", long? id = null, long? artist = null,
        int page = 1, int pageSize = DefaultPageSize)
    {
        page = NormalizePage(page);
        pageSize = NormalizePageSize(pageSize);
        await using var connection = await OpenAsync();
        var sql = new StringBuilder(SongSelect).AppendLine("WHERE 1 = 1");
        await using var command = new SqlCommand { Connection = connection };

        if (id.HasValue)
        {
            sql.AppendLine("AND [SamSongId] = @id");
            command.Parameters.Add("id", SqlDbType.BigInt).Value = id.Value;
        }

        if (artist.HasValue)
        {
            var artistName = await ResolveArtistNameAsync(connection, artist.Value);
            if (artistName is null)
                return [];

            sql.AppendLine("AND [Artist] = @artist");
            command.Parameters.Add("artist", SqlDbType.NVarChar, 512).Value = artistName;
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            sql.AppendLine("AND (LOWER([Title]) LIKE LOWER(@search) ESCAPE '\\' OR LOWER([Artist]) LIKE LOWER(@search) ESCAPE '\\')");
            command.Parameters.Add("search", SqlDbType.NVarChar, 402).Value = $"%{EscapeSearchTerm(query)}%";
        }

        sql.AppendLine("ORDER BY [Artist], [Title], [SamSongId] OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY");
        command.Parameters.Add("offset", SqlDbType.Int).Value = (page - 1) * pageSize;
        command.Parameters.Add("pageSize", SqlDbType.Int).Value = pageSize;
        command.CommandText = sql.ToString();

        var songs = new List<Song>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            songs.Add(ReadSong(reader));
        return songs;
    }

    public async Task<List<Song>> BroadcastSongs(IEnumerable<Track> tracks)
    {
        var items = tracks.Take(11).ToArray();
        if (items.Length == 0 || !Configured)
            return [];

        await using var connection = await OpenAsync();
        var terms = items.Select((_, index) => $"(LOWER([Title]) = LOWER(@title{index}) AND LOWER([Artist]) = LOWER(@artist{index}))");
        await using var command = new SqlCommand($"{SongSelect} WHERE {string.Join(" OR ", terms)}", connection);
        for (var index = 0; index < items.Length; index++)
        {
            command.Parameters.Add($"title{index}", SqlDbType.NVarChar, 512).Value = items[index].Title;
            command.Parameters.Add($"artist{index}", SqlDbType.NVarChar, 512).Value = items[index].Artist;
        }

        var songs = new List<Song>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            songs.Add(ReadSong(reader));
        return songs;
    }

    public async Task<List<Artist>> Artists(string query = "", long? id = null)
    {
        await using var connection = await OpenAsync();
        var requestedName = id.HasValue ? await ResolveArtistNameAsync(connection, id.Value) : null;
        if (id.HasValue && requestedName is null)
            return [];

        await using var command = new SqlCommand("""
            SELECT [Artist], COUNT_BIG(*) AS [Tracks],
                   COUNT(DISTINCT NULLIF(LTRIM(RTRIM([Album])), '')) AS [Albums]
            FROM [dbo].[Songs]
            WHERE NULLIF(LTRIM(RTRIM([Artist])), '') IS NOT NULL
              AND (@artist IS NULL OR [Artist] = @artist)
              AND (@query = N'' OR LOWER([Artist]) LIKE LOWER(@search) ESCAPE '\')
            GROUP BY [Artist]
            ORDER BY [Artist]
            OFFSET 0 ROWS FETCH NEXT 100 ROWS ONLY
            """, connection);
        command.Parameters.Add("query", SqlDbType.NVarChar, 200).Value = query.Trim();
        command.Parameters.Add("search", SqlDbType.NVarChar, 402).Value = $"%{EscapeSearchTerm(query)}%";
        command.Parameters.Add("artist", SqlDbType.NVarChar, 512).Value = (object?)requestedName ?? DBNull.Value;

        var results = new List<Artist>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            results.Add(new Artist(ArtistIdentity.FromName(name), name,
                $"{reader.GetInt64(1)} tracks across {reader.GetInt32(2)} albums in the Aneed library. Explore their music below.", null));
        }

        return results;
    }

    public async Task<(List<Artist> Artists, long Total)> ArtistDirectory(string letter, int page = 1, int pageSize = 30)
    {
        letter = NormalizeLetter(letter);
        page = NormalizePage(page);
        pageSize = NormalizePageSize(pageSize);
        await using var connection = await OpenAsync();
        const string filter = "NULLIF(LTRIM(RTRIM([Artist])), '') IS NOT NULL AND LEFT(UPPER(LTRIM(RTRIM([Artist]))), 1) = @letter";

        await using var count = new SqlCommand($"SELECT COUNT(DISTINCT [Artist]) FROM [dbo].[Songs] WHERE {filter}", connection);
        count.Parameters.Add("letter", SqlDbType.NVarChar, 1).Value = letter;
        var total = Convert.ToInt64(await count.ExecuteScalarAsync() ?? 0L);

        await using var command = new SqlCommand($"""
            SELECT [Artist], COUNT_BIG(*) AS [Tracks],
                   COUNT(DISTINCT NULLIF(LTRIM(RTRIM([Album])), '')) AS [Albums]
            FROM [dbo].[Songs]
            WHERE {filter}
            GROUP BY [Artist]
            ORDER BY [Artist]
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
            """, connection);
        command.Parameters.Add("letter", SqlDbType.NVarChar, 1).Value = letter;
        command.Parameters.Add("offset", SqlDbType.Int).Value = (page - 1) * pageSize;
        command.Parameters.Add("pageSize", SqlDbType.Int).Value = pageSize;

        var artists = new List<Artist>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            artists.Add(new Artist(ArtistIdentity.FromName(name), name, "", null));
        }

        return (artists, total);
    }

    public async Task<List<(string Name, int Tracks, int? Year)>> ArtistAlbums(long artistId)
    {
        var artistName = await ResolveArtistNameAsync(artistId);
        if (artistName is null)
            return [];

        await using var connection = await OpenAsync();
        await using var command = new SqlCommand("""
            SELECT [Album], COUNT(*) AS [Tracks], MIN([AlbumYear]) AS [Year]
            FROM [dbo].[Songs]
            WHERE [Artist] = @artist AND NULLIF(LTRIM(RTRIM([Album])), '') IS NOT NULL
            GROUP BY [Album]
            ORDER BY MIN([AlbumYear]), [Album]
            """, connection);
        command.Parameters.Add("artist", SqlDbType.NVarChar, 512).Value = artistName;
        var albums = new List<(string Name, int Tracks, int? Year)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            albums.Add((reader.GetString(0), reader.GetInt32(1), ReadNullableInt(reader, 2)));
        return albums;
    }

    public async Task<List<Song>> AlbumSongs(long artistId, string album)
    {
        var artistName = await ResolveArtistNameAsync(artistId);
        if (artistName is null)
            return [];

        await using var connection = await OpenAsync();
        await using var command = new SqlCommand($"""
            {SongSelect}
            WHERE [Artist] = @artist AND [Album] = @album
            ORDER BY [Title], [SamSongId]
            """, connection);
        command.Parameters.Add("artist", SqlDbType.NVarChar, 512).Value = artistName;
        command.Parameters.Add("album", SqlDbType.NVarChar, 512).Value = album;
        var songs = new List<Song>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            songs.Add(ReadSong(reader));
        return songs;
    }

    public async Task<string?> ResolveArtistNameAsync(long artistId)
    {
        await using var connection = await OpenAsync();
        return await ResolveArtistNameAsync(connection, artistId);
    }

    private static async Task<string?> ResolveArtistNameAsync(SqlConnection connection, long artistId)
    {
        await using var command = new SqlCommand("""
            SELECT DISTINCT [Artist]
            FROM [dbo].[Songs]
            WHERE NULLIF(LTRIM(RTRIM([Artist])), '') IS NOT NULL
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        string? match = null;
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            if (ArtistIdentity.FromName(name) != artistId)
                continue;
            if (match is not null && !string.Equals(match, name, StringComparison.Ordinal))
                return null;
            match = name;
        }
        return match;
    }

    public static int NormalizePage(int page) => Math.Max(1, page);

    public static int NormalizePageSize(int pageSize) => Math.Clamp(pageSize, 1, 100);

    public static string EscapeSearchTerm(string? value) =>
        (value ?? "").Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string NormalizeLetter(string? letter)
    {
        var value = letter?.Trim().ToUpperInvariant();
        return value is { Length: 1 } && value[0] is >= 'A' and <= 'Z' ? value : "A";
    }
}

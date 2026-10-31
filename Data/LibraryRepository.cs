
using System.Text.Json;

namespace AneedWeb.Data;

// Public website models. Song IDs use stable SAM IDs; artist IDs are deterministic catalog identities.
public record Artist(long Id, string Name, string Biography, string? Image);
public record Song(long Id, string Title, long ArtistId, string Artist, string Album, int? Year, string Description, string? Artwork, string Genre, string Composer, string Label, string Isrc);
public record FeedPost(long Id, string Type, string Title, string Excerpt, string Body, string? Image, DateTime Published);

public class LibraryRepository(IWebHostEnvironment environment, IConfiguration configuration)
{
    private readonly AzureSqlCatalogRepository catalog = new(configuration);

    public bool Configured => catalog.Configured;

    public Task<List<Song>> Songs(string query = "", long? id = null, long? artist = null, int page = 1, int pageSize = 100) =>
        catalog.Songs(query, id, artist, page, pageSize);

    public Task<List<Song>> BroadcastSongs(IEnumerable<Services.Track> tracks) => catalog.BroadcastSongs(tracks);
    public Task<List<Artist>> Artists(string query = "", long? id = null) => catalog.Artists(query, id);
    public Task<(List<Artist> Artists, long Total)> ArtistDirectory(string letter, int page = 1, int pageSize = 30) => catalog.ArtistDirectory(letter, page, pageSize);
    public Task<List<(string Name, int Tracks, int? Year)>> ArtistAlbums(long artistId) => catalog.ArtistAlbums(artistId);
    public Task<List<Song>> AlbumSongs(long artistId, string album) => catalog.AlbumSongs(artistId, album);

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

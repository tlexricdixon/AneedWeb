using System.Text.Json;
namespace AneedWeb.Services;
public record Track(string Title, string Artist, string? Artwork, DateTimeOffset? Started);
public record Broadcast(bool Playing, Track? Current, IReadOnlyList<Track> Recent, string? Error);
public sealed class BroadcastService(IHttpClientFactory factory, ILogger<BroadcastService> logger)
{
    private readonly SemaphoreSlim gate = new(1,1);
    private Broadcast snapshot = new(false,null,[],null);
    private DateTimeOffset refreshed;
    public async Task<Broadcast> Get()
    {
        await gate.WaitAsync();
        try
        {
            if (DateTimeOffset.UtcNow-refreshed < TimeSpan.FromSeconds(20)) return snapshot;
            try
            {
                using var doc = await factory.CreateClient("Live365").GetFromJsonAsync<JsonDocument>("station/a30355");
                var root = doc!.RootElement;
                Track? current = root.TryGetProperty("current-track",out var t) && t.ValueKind==JsonValueKind.Object ? Read(t) : null;
                var recent = root.TryGetProperty("last-played",out var list) && list.ValueKind==JsonValueKind.Array ? list.EnumerateArray().Take(10).Select(Read).ToArray() : [];
                snapshot = new(root.TryGetProperty("is_playing",out var playing) && playing.ValueKind==JsonValueKind.True,current,recent,null);
            }
            catch(Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            { logger.LogWarning(ex,"Live365 metadata unavailable"); snapshot = snapshot with { Error="Track updates are temporarily unavailable. You can still try the live player." }; }
            refreshed=DateTimeOffset.UtcNow; return snapshot;
        }
        finally { gate.Release(); }
    }
    private static Track Read(JsonElement t)
    {
        string? Str(string key) => t.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.String ? v.GetString() : null;
        return new(Str("title")??"Unknown track",Str("artist")??"Unknown artist",Str("art"),DateTimeOffset.TryParse(Str("start"),out var date)?date:null);
    }
}

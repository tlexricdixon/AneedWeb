using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

if (args.Contains("--content", StringComparer.OrdinalIgnoreCase))
    return await ContentDrafts.Run(args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase));

var connectionString = Environment.GetEnvironmentVariable("ANEED_POSTGRES");
var contact = Environment.GetEnvironmentVariable("ANEED_CONTACT_EMAIL");
if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(contact))
{
    Console.Error.WriteLine("Set ANEED_POSTGRES and ANEED_CONTACT_EMAIL first.");
    return 1;
}
var dryRun = args.Contains("--dry-run");
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.UserAgent.ParseAdd($"AneedContentEnrichment/0.1 (mailto:{contact})");
await using var db = new NpgsqlConnection(connectionString);
await db.OpenAsync();
var jobs = new List<(long Id, string Name)>();
await using (var cmd = new NpgsqlCommand("""
    SELECT j.artist_id, a.name FROM music.enrichment_jobs j
    JOIN music.artists a ON a.id=j.artist_id
    WHERE (j.status IN ('pending','error')
       OR (j.status='processing' AND j.updated_at < now()-interval '20 minutes'))
      AND (j.next_attempt_at IS NULL OR j.next_attempt_at <= now())
    ORDER BY a.name LIMIT 20
    """, db))
await using (var reader = await cmd.ExecuteReaderAsync())
    while (await reader.ReadAsync()) jobs.Add((reader.GetInt64(0), reader.GetString(1)));

foreach (var (id, name) in jobs)
{
    try
    {
        if (!dryRun) await UpdateJob(db, id, "processing", null);
        var searchUrl = "https://musicbrainz.org/ws/2/artist/?query=" +
            Uri.EscapeDataString($"artist:\"{SearchName(name)}\"") + "&fmt=json&limit=10";
        using var search = await Fetch(http, searchUrl);
        var candidates = search.RootElement.GetProperty("artists").EnumerateArray()
            .Select(a => new
            {
                Id = a.GetProperty("id").GetString()!,
                Name = a.GetProperty("name").GetString()!,
                SortName = a.GetProperty("sort-name").GetString()!,
                Score = a.GetProperty("score").GetInt32(),
                Type = a.TryGetProperty("type", out var artistType) ? artistType.GetString() ?? "" : "",
                Country = a.TryGetProperty("country", out var country) ? country.GetString() ?? "" : "",
                Disambiguation = a.TryGetProperty("disambiguation", out var disambiguation) ? disambiguation.GetString() ?? "" : ""
            })
            .ToList();
        var matches = candidates.Where(a => Normalize(a.Name) == Normalize(name) ||
                                             Normalize(a.SortName) == Normalize(name))
            .OrderByDescending(a => a.Score).ToList();
        // Accept a standout match, but never use an arbitrary top result.
        // If names collide at similar scores, require human review.
        var clearWinner = matches.Count > 0 && matches[0].Score >= 95 &&
            (matches.Count == 1 || matches[0].Score - matches[1].Score >= 15);
        if (!clearWinner)
        {
            Console.WriteLine($"REVIEW: {name} ({matches.Count} exact candidates)");
            foreach (var candidate in candidates)
                Console.WriteLine($"  {candidate.Score,3} | {candidate.Id} | {candidate.Name} | {candidate.Type} | {candidate.Country} | {candidate.Disambiguation}");
            if (!dryRun)
            {
                await using var review = new NpgsqlCommand("""
                    INSERT INTO music.artist_sources(artist_id,match_status)
                    VALUES (@id,'review')
                    ON CONFLICT(artist_id) DO UPDATE SET match_status='review',updated_at=now()
                    """, db);
                review.Parameters.AddWithValue("id", id);
                await review.ExecuteNonQueryAsync();
                await UpdateJob(db, id, "review", null);
            }
            await Task.Delay(1200);
            continue;
        }

        var matched = matches[0];
        await Task.Delay(1200);
        using var detail = await Fetch(http,
            $"https://musicbrainz.org/ws/2/artist/{matched.Id}?inc=url-rels&fmt=json");
        string? wikidata = null;
        if (detail.RootElement.TryGetProperty("relations", out var relations))
        {
            var ids = relations.EnumerateArray()
                .Where(x => x.GetProperty("type").GetString() == "wikidata")
                .Select(x => x.GetProperty("url").GetProperty("resource").GetString() ?? "")
                .Select(x => Regex.Match(x, @"Q[1-9][0-9]*$").Value)
                .Where(x => x.Length > 0).Distinct().ToList();
            if (ids.Count == 1) wikidata = ids[0];
        }
        string? wikipedia = null;
        if (wikidata is not null)
        {
            using var entityDoc = await Fetch(http,
                $"https://www.wikidata.org/wiki/Special:EntityData/{wikidata}.json");
            var entity = entityDoc.RootElement.GetProperty("entities").GetProperty(wikidata);
            if (entity.GetProperty("sitelinks").TryGetProperty("enwiki", out var enwiki))
                wikipedia = "https://en.wikipedia.org/wiki/" +
                    Uri.EscapeDataString(enwiki.GetProperty("title").GetString()!).Replace("%20", "_");
        }
        Console.WriteLine($"MATCH: {name} => {matched.Name} ({matched.Id}), Wikidata: {wikidata ?? "none"}");
        if (!dryRun)
        {
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO music.artist_sources
                    (artist_id,musicbrainz_artist_mbid,wikidata_id,wikipedia_url,match_status,matched_at)
                VALUES (@id,@mbid,@wiki,@url,'matched',now())
                ON CONFLICT(artist_id) DO UPDATE SET
                    musicbrainz_artist_mbid=EXCLUDED.musicbrainz_artist_mbid,
                    wikidata_id=EXCLUDED.wikidata_id,
                    wikipedia_url=EXCLUDED.wikipedia_url,
                    match_status='matched',matched_at=now(),updated_at=now()
                """, db);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("mbid", Guid.Parse(matched.Id));
            cmd.Parameters.AddWithValue("wiki", (object?)wikidata ?? DBNull.Value);
            cmd.Parameters.AddWithValue("url", (object?)wikipedia ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
            await UpdateJob(db, id, "completed", null);
        }
        await Task.Delay(1200);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ERROR {name}: {ex.Message}");
        if (!dryRun) await UpdateJob(db, id, "error", ex.Message);
    }
}
return 0;

static string SearchName(string value)
{
    value = value.Trim();
    if (value.EndsWith(", the", StringComparison.OrdinalIgnoreCase))
        value = "The " + value[..^5];
    // The B-52's is indexed as The B-52s in MusicBrainz.
    // Removing this band's apostrophe makes the initial quoted query find it.
    if (value.Equals("The B-52's", StringComparison.OrdinalIgnoreCase))
        return "The B-52s";
    return value;
}

static string Normalize(string value)
{
    value = value.Trim().ToLowerInvariant();
    if (value.EndsWith(", the")) value = "the " + value[..^5];
    return Regex.Replace(value, @"[^a-z0-9]+", "");
}
static async Task<JsonDocument> Fetch(HttpClient client, string url)
{
    const int maxAttempts = 4;
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            using var response = await client.GetAsync(url);
            if ((response.StatusCode == HttpStatusCode.TooManyRequests ||
                 response.StatusCode == HttpStatusCode.RequestTimeout ||
                 (int)response.StatusCode >= 500) && attempt < maxAttempts)
            {
                var retryAfter = response.Headers.RetryAfter;
                var delay = retryAfter?.Delta ??
                    (retryAfter?.Date is { } retryAt ? retryAt - DateTimeOffset.UtcNow : TimeSpan.Zero);
                if (delay <= TimeSpan.Zero)
                    delay = TimeSpan.FromSeconds(2 * (1 << (attempt - 1)));
                delay = TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 60));
                Console.WriteLine($"HTTP {(int)response.StatusCode}; retry {attempt}/{maxAttempts - 1} in {delay.TotalSeconds:0}s");
                await Task.Delay(delay);
                continue;
            }
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        }
        catch (Exception ex) when (
            ex is HttpRequestException || ex is TaskCanceledException)
        {
            if (attempt == maxAttempts) throw;
            var delay = TimeSpan.FromSeconds(2 * (1 << (attempt - 1)));
            Console.WriteLine($"Transient request failure ({ex.GetType().Name}); retry {attempt}/{maxAttempts - 1} in {delay.TotalSeconds:0}s");
            await Task.Delay(delay);
        }
    }
    throw new InvalidOperationException("HTTP retry loop ended unexpectedly.");
}
static async Task UpdateJob(NpgsqlConnection db, long id, string status, string? error)
{
    await using var cmd = new NpgsqlCommand("""
        UPDATE music.enrichment_jobs SET status=@status, updated_at=now(),
        attempts=attempts+CASE WHEN @status='processing' THEN 1 ELSE 0 END,
        last_error=@error,
        next_attempt_at=CASE WHEN @status='error' THEN now()+interval '30 minutes' ELSE NULL END,
        completed_at=CASE WHEN @status='completed' THEN now() ELSE completed_at END
        WHERE artist_id=@id
        """, db);
    cmd.Parameters.AddWithValue("id", id);
    cmd.Parameters.AddWithValue("status", status);
    cmd.Parameters.AddWithValue("error", (object?)error ?? DBNull.Value);
    await cmd.ExecuteNonQueryAsync();
}

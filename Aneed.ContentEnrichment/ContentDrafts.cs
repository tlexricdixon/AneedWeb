using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Npgsql;

internal static class ContentDrafts
{
    public static async Task<int> Run(bool dryRun)
    {
        var connectionString = Environment.GetEnvironmentVariable("ANEED_POSTGRES");
        var email = Environment.GetEnvironmentVariable("ANEED_CONTACT_EMAIL");
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(email))
        {
            Console.Error.WriteLine("Set ANEED_POSTGRES and ANEED_CONTACT_EMAIL first.");
            return 1;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"AneedContentEnrichment/0.1 (mailto:{email})");

        await using var db = new NpgsqlConnection(connectionString);
        await db.OpenAsync();
        var targets = new List<(long Id, string Name, string Url)>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT a.id, a.name, s.wikipedia_url
            FROM music.artists a
            JOIN music.artist_sources s ON s.artist_id=a.id
            LEFT JOIN music.artist_content c ON c.artist_id=a.id
            WHERE s.match_status='matched' AND s.wikipedia_url IS NOT NULL
              AND (c.artist_id IS NULL OR c.review_status='draft')
            ORDER BY a.name LIMIT 20
            """, db))
        await using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                targets.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));

        Console.WriteLine($"Biography candidates: {targets.Count}. Dry run: {dryRun}");
        foreach (var (id, name, sourceUrl) in targets)
        {
            try
            {
                var uri = new Uri(sourceUrl);
                if (uri.Scheme != Uri.UriSchemeHttps || uri.Host != "en.wikipedia.org" ||
                    !uri.AbsolutePath.StartsWith("/wiki/", StringComparison.Ordinal))
                {
                    Console.WriteLine($"SKIP {name}: unexpected source URL");
                    continue;
                }

                var title = Uri.UnescapeDataString(uri.AbsolutePath["/wiki/".Length..]).Replace('_', ' ');
                // MediaWiki action API returns a plain-text extract from the article lead.
                var url = "https://en.wikipedia.org/w/api.php?action=query&prop=extracts" +
                    "&exintro=1&explaintext=1&redirects=1&format=json&formatversion=2&titles=" +
                    Uri.EscapeDataString(title);
                using var json = await GetWithRetry(http, url);
                var pages = json.RootElement.GetProperty("query").GetProperty("pages");
                var page = pages.EnumerateArray().FirstOrDefault();
                if (page.ValueKind != JsonValueKind.Object ||
                    page.TryGetProperty("missing", out _) ||
                    !page.TryGetProperty("extract", out var extractElement))
                {
                    Console.WriteLine($"NO CONTENT {name}");
                    continue;
                }

                var excerpt = extractElement.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(excerpt))
                {
                    Console.WriteLine($"EMPTY {name}");
                    continue;
                }

                Console.WriteLine($"DRAFT {name}: {excerpt.Length} chars from {sourceUrl}");
                if (dryRun) continue;

                // This is Wikipedia-derived draft text, NOT an original AI biography.
                // Publishing it requires attribution and compliance with CC BY-SA.
                await using var save = new NpgsqlCommand("""
                    INSERT INTO music.artist_content
                        (artist_id,summary,summary_source_url,license_notes,review_status,generated_by,updated_at)
                    VALUES (@id,@summary,@source,@license,'needs_review','Wikipedia lead extract',now())
                    ON CONFLICT (artist_id) DO UPDATE SET
                        summary=EXCLUDED.summary,
                        summary_source_url=EXCLUDED.summary_source_url,
                        license_notes=EXCLUDED.license_notes,
                        review_status='needs_review',
                        generated_by=EXCLUDED.generated_by,
                        updated_at=now()
                    WHERE music.artist_content.review_status='draft'
                    """, db);
                save.Parameters.AddWithValue("id", id);
                save.Parameters.AddWithValue("summary", excerpt);
                save.Parameters.AddWithValue("source", sourceUrl);
                save.Parameters.AddWithValue("license",
                    "Wikipedia contributors; CC BY-SA 4.0. Attribute the article and license; indicate modifications. Verify the individual article before publication.");
                await save.ExecuteNonQueryAsync();
                await Task.Delay(350);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR {name}: {ex.Message}");
            }
        }
        return 0;
    }

    private static async Task<JsonDocument> GetWithRetry(HttpClient client, string url)
    {
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(url);
                if ((response.StatusCode == HttpStatusCode.TooManyRequests ||
                     response.StatusCode == HttpStatusCode.RequestTimeout ||
                     (int)response.StatusCode >= 500) && attempt < 4)
                {
                    var retry = response.Headers.RetryAfter;
                    var delay = retry?.Delta ??
                        (retry?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.Zero);
                    if (delay <= TimeSpan.Zero)
                        delay = TimeSpan.FromSeconds(2 * (1 << (attempt - 1)));
                    delay = TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 60));
                    Console.WriteLine($"HTTP {(int)response.StatusCode}; retry in {delay.TotalSeconds:0}s");
                    await Task.Delay(delay);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                if (attempt == 4) throw;
                await Task.Delay(TimeSpan.FromSeconds(2 * (1 << (attempt - 1))));
            }
        }
        throw new InvalidOperationException("Unexpected end of retry loop.");
    }
}

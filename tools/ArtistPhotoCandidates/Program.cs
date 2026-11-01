using Microsoft.Data.SqlClient;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net;

var connectionString=Environment.GetEnvironmentVariable("ANEED_AZURE_SQL_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Set ANEED_AZURE_SQL_CONNECTION in your Nitro environment.");
    return 1;
}
var limit=20;
if (args.Length > 0 && (!int.TryParse(args[0],out limit) || limit<1 || limit>100))
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/ArtistPhotoCandidates -- [count 1..100]");
    return 1;
}
using var http=new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("AneedWebArtistReview/1.1 (contact: https://github.com/tlexricdixon/AneedWeb)");
http.Timeout=TimeSpan.FromSeconds(30);
var lastRequest=DateTimeOffset.MinValue;
var rateLimited=false;
async Task<string> GetWithBackoff(string url)
{
    for (var attempt=0; attempt<4; attempt++)
    {
        var elapsed=DateTimeOffset.UtcNow-lastRequest;
        if (elapsed<TimeSpan.FromSeconds(3)) await Task.Delay(TimeSpan.FromSeconds(3)-elapsed);
        lastRequest=DateTimeOffset.UtcNow;
        using var response=await http.GetAsync(url);
        if ((int)response.StatusCode==429)
        {
            var delay=response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date-DateTimeOffset.UtcNow)
                ?? TimeSpan.FromSeconds(15*Math.Pow(2,attempt));
            if (delay<TimeSpan.FromSeconds(5)) delay=TimeSpan.FromSeconds(5);
            if (delay>TimeSpan.FromMinutes(2)) delay=TimeSpan.FromMinutes(2);
            Console.Error.WriteLine($"HTTP 429: pausing {delay.TotalSeconds:F0}s (attempt {attempt+1}/4).");
            await Task.Delay(delay);
            continue;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
    rateLimited=true;
    throw new HttpRequestException("Rate limit still active after retries; ending batch.");
}
await using var db=new SqlConnection(connectionString);
await db.OpenAsync();
await using var query=new SqlCommand("""
    SELECT TOP (@limit) Artist
    FROM dbo.Songs
    WHERE Artist IS NOT NULL AND LTRIM(RTRIM(Artist)) <> ''
      AND NOT EXISTS (
         SELECT 1 FROM dbo.ArtistImages a WHERE a.Artist = dbo.Songs.Artist
      )
    GROUP BY Artist ORDER BY COUNT_BIG(*) DESC, Artist
    """,db);
query.Parameters.AddWithValue("@limit",limit);
var artists=new List<string>();
await using (var reader=await query.ExecuteReaderAsync())
    while (await reader.ReadAsync()) artists.Add(reader.GetString(0));
Console.WriteLine("Artist\tWikidataId\tWikidataDescription\tCandidateImageUrl\tCreator\tSourceUrl\tLicense\tReviewStatus");
foreach (var artist in artists)
{
    try
    {
        var searchUrl="https://www.wikidata.org/w/api.php?action=wbsearchentities&search="+Uri.EscapeDataString(artist)+"&language=en&limit=5&format=json";
        using var search=JsonDocument.Parse(await GetWithBackoff(searchUrl));
        foreach (var item in search.RootElement.GetProperty("search").EnumerateArray())
        {
            var label=item.TryGetProperty("label",out var l)?l.GetString():"";
            if (!string.Equals(label,artist,StringComparison.OrdinalIgnoreCase)) continue;
            if (!item.TryGetProperty("id",out var idValue)) continue;
            var id=idValue.GetString();
            if (string.IsNullOrEmpty(id)) continue;
            var description=item.TryGetProperty("description",out var d)?d.GetString()??"":"";
            // Name-only matches can be entirely different entities (Poison, KISS).
            // If the description does not identify a musical act, do not emit a candidate.
            if (!Regex.IsMatch(description, @"\\b(sing(?:er|ing)?|songwriter|musician|music(?:al)?|rock|pop|band|rapper|guitarist|vocalist|composer|recording artist|duo|trio|orchestra|performer|hip.hop|metal|punk|disco|soul|r\\&b|blues|jazz)\\b", RegexOptions.IgnoreCase)) continue;
            var entityUrl="https://www.wikidata.org/w/api.php?action=wbgetentities&ids="+Uri.EscapeDataString(id)+"&props=claims&format=json";
            using var entity=JsonDocument.Parse(await GetWithBackoff(entityUrl));
            if (!entity.RootElement.TryGetProperty("entities",out var entities) ||
                !entities.TryGetProperty(id,out var details) ||
                !details.TryGetProperty("claims",out var claims)) continue;
            if (!claims.TryGetProperty("P18",out var images) || images.GetArrayLength()==0) continue;
            if (!images[0].TryGetProperty("mainsnak",out var snak) ||
                !snak.TryGetProperty("datavalue",out var dataValue) ||
                !dataValue.TryGetProperty("value",out var imageName)) continue;
            var filename=imageName.GetString();
            if (string.IsNullOrWhiteSpace(filename)) continue;
            var commonsUrl="https://commons.wikimedia.org/w/api.php?action=query&titles="+Uri.EscapeDataString("File:"+filename)+"&prop=imageinfo&iiprop=url%7Cextmetadata&iiurlwidth=640&format=json";
            using var commons=JsonDocument.Parse(await GetWithBackoff(commonsUrl));
            if (!commons.RootElement.TryGetProperty("query",out var responseQuery) ||
                !responseQuery.TryGetProperty("pages",out var pages)) continue;
            foreach (var page in pages.EnumerateObject())
            {
                if (!page.Value.TryGetProperty("imageinfo",out var infos) || infos.GetArrayLength()==0) continue;
                var info=infos[0];
                var imageUrl=info.TryGetProperty("thumburl",out var thumb)?thumb.GetString() : (info.TryGetProperty("url",out var url)?url.GetString():null);
                var sourceUrl=info.TryGetProperty("descriptionurl",out var source)?source.GetString():null;
                var metadata=info.TryGetProperty("extmetadata",out var md)?md:default;
                string ReadMeta(string key) => metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty(key,out var v) && v.TryGetProperty("value",out var val)
                    ? StripHtml(val.GetString()??"") : "";
                var creator=ReadMeta("Artist");
                var license=ReadMeta("LicenseShortName");
                Console.WriteLine(string.Join("\t",new[]{artist,id,description,imageUrl??"",creator,sourceUrl??"",license,"REVIEW IDENTITY AND RIGHTS"}.Select(Clean)));
            }
        }
    }
    catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
    {
        Console.Error.WriteLine($"Skipped {artist}: {ex.Message}");
    }
    if (rateLimited) break;
}
return 0;

static string StripHtml(string text) => WebUtility.HtmlDecode(Regex.Replace(text,"<[^>]*>"," ")).Trim();
static string Clean(string text) => Regex.Replace(text.Replace('\t',' ').Replace('\r',' ').Replace('\n',' '),@"\s+"," ").Trim();

using AneedWeb.Data;
using AneedWeb.Services;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var originalConnection = Environment.GetEnvironmentVariable("ANEED_AZURE_SQL_CONNECTION");
try
{
    Environment.SetEnvironmentVariable("ANEED_AZURE_SQL_CONNECTION", null);
    var configuration = new ConfigurationBuilder().Build();
    var unconfigured = new AzureSqlCatalogRepository(configuration);
    Check(!unconfigured.Configured, "Catalog must report missing Azure SQL configuration.");

    var artistId = ArtistIdentity.FromName("  The Aneed Signal  ");
    Check(artistId > 0, "Artist identity must be a positive stable route ID.");
    Check(artistId == ArtistIdentity.FromName("the aneed signal"), "Artist identity must be case and whitespace stable.");
    Check(artistId != ArtistIdentity.FromName("Another Artist"), "Distinct artist names should have distinct identities in this fixture.");

    Check(AzureSqlCatalogRepository.EscapeSearchTerm("  50%_\\  ") == "50\\%\\_\\\\", "Search terms must escape SQL LIKE wildcards.");
    Check(AzureSqlCatalogRepository.NormalizePage(0) == 1, "Pagination must clamp page numbers to one.");
    Check(AzureSqlCatalogRepository.NormalizePageSize(0) == 1, "Pagination must clamp page size to one.");
    Check(AzureSqlCatalogRepository.NormalizePageSize(500) == 100, "Pagination must cap page size at 100.");

    const string orderedHistory = """
    {
      "history": [
        { "id": 1001, "artist": "A", "title": "Now", "album": "Alpha", "picture": "now.jpg" },
        { "id": 1002, "artist": "B", "title": "Recent1", "album": "Beta", "picture": "r1.jpg" },
        { "id": 1003, "artist": "C", "title": "Recent2", "album": "Gamma", "picture": "r2.jpg" },
        { "id": 1004, "artist": "D", "title": "Recent3", "album": "Delta", "picture": "r3.jpg" },
        { "id": 1005, "artist": "E", "title": "Recent4", "album": "Epsilon", "picture": "r4.jpg" },
        { "id": 1006, "artist": "F", "title": "Ignored", "album": "Zeta", "picture": "r5.jpg" }
      ]
    }
    """;

    var ordered = BroadcastService.ParseHistorySnapshot(orderedHistory);
    Check(ordered.Playing, "Broadcast should be marked playing when SAM history has entries.");
    Check(ordered.Current?.SamSongId == 1001, "history[0] must be used as current song.");
    Check(ordered.Recent.Count == 4, "Only history[1..4] should be shown as recently played.");
    Check(ordered.Recent[0].SamSongId == 1002 && ordered.Recent[3].SamSongId == 1005, "Recent ordering must follow SAM history order.");

    var empty = BroadcastService.ParseHistorySnapshot("{\"history\":[]}");
    Check(!empty.Playing && empty.Current is null && empty.Recent.Count == 0, "Empty SAM history should yield an empty, not-playing snapshot.");

    var malformedThrown = false;
    try
    {
        _ = BroadcastService.ParseHistorySnapshot("{\"history\":");
    }
    catch (JsonException)
    {
        malformedThrown = true;
    }

    Check(malformedThrown, "Malformed SAM history JSON should throw JsonException for caller-level handling.");

    Check(BroadcastService.BuildArtworkFallback("https://cdn.example/cover.jpg", "sam.jpg") == "https://cdn.example/cover.jpg", "Mapped SQL artwork must win over SAM picture fallback.");
    Check(BroadcastService.BuildArtworkFallback(null, "sam cover.jpg") == "/media/covers/sam%20cover.jpg", "Valid SAM picture should map to /media/covers fallback.");
    Check(BroadcastService.BuildArtworkFallback(null, "../sam.jpg") == "/images/station.png", "Invalid SAM picture filenames must fall back to station artwork.");

    Console.WriteLine("PASS: Azure SQL catalog identity/search/pagination plus SAM history ordering and artwork fallback checks.");
}
finally
{
    Environment.SetEnvironmentVariable("ANEED_AZURE_SQL_CONNECTION", originalConnection);
}

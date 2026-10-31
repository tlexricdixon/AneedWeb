using AneedWeb.Data;

void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var originalConnection = Environment.GetEnvironmentVariable("ANEED_AZURE_SQL_CONNECTION");
try
{
    Environment.SetEnvironmentVariable("ANEED_AZURE_SQL_CONNECTION", null);
    var unconfigured = new AzureSqlCatalogRepository();
    Check(!unconfigured.Configured, "Catalog must report missing Azure SQL configuration.");

    var artistId = ArtistIdentity.FromName("  The Aneed Signal  ");
    Check(artistId > 0, "Artist identity must be a positive stable route ID.");
    Check(artistId == ArtistIdentity.FromName("the aneed signal"), "Artist identity must be case and whitespace stable.");
    Check(artistId != ArtistIdentity.FromName("Another Artist"), "Distinct artist names should have distinct identities in this fixture.");

    Check(AzureSqlCatalogRepository.EscapeSearchTerm("  50%_\\  ") == "50\\%\\_\\\\", "Search terms must escape SQL LIKE wildcards.");
    Check(AzureSqlCatalogRepository.NormalizePage(0) == 1, "Pagination must clamp page numbers to one.");
    Check(AzureSqlCatalogRepository.NormalizePageSize(0) == 1, "Pagination must clamp page size to one.");
    Check(AzureSqlCatalogRepository.NormalizePageSize(500) == 100, "Pagination must cap page size at 100.");

    Console.WriteLine("PASS: Azure SQL catalog identity, search escaping, pagination, and missing configuration.");
}
finally
{
    Environment.SetEnvironmentVariable("ANEED_AZURE_SQL_CONNECTION", originalConnection);
}

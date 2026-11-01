using Microsoft.Data.SqlClient;
using System.Data;

var write = args.Contains("--write", StringComparer.OrdinalIgnoreCase);
var file = args.FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal)) ?? "artist_images.tsv";
var connectionString = Environment.GetEnvironmentVariable("ANEED_AZURE_SQL_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Missing ANEED_AZURE_SQL_CONNECTION (same convention as AneedWeb).");
    return 1;
}
if (!File.Exists(file))
{
    Console.Error.WriteLine($"Missing input TSV: {file}");
    return 1;
}
var entries = new List<(string Artist,string ImageUrl,string Credit,string SourceUrl,string License)>();
var lineNumber=0;
foreach (var line in File.ReadLines(file))
{
    lineNumber++;
    if (lineNumber == 1 && line.StartsWith("Artist\t", StringComparison.OrdinalIgnoreCase)) continue;
    if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
    var fields=line.Split('\t');
    if (fields.Length != 5) throw new InvalidDataException($"Line {lineNumber}: expected 5 tab-separated fields.");
    var artist=fields[0].Trim();
    var imageUrl=fields[1].Trim();
    var credit=fields[2].Trim();
    var sourceUrl=fields[3].Trim();
    var license=fields[4].Trim();
    if (artist.Length == 0 || artist.Length > 512 ||
        credit.Length == 0 || credit.Length > 500 ||
        sourceUrl.Length == 0 || sourceUrl.Length > 1000 ||
        license.Length == 0 || license.Length > 100 ||
        imageUrl.Length > 1000 ||
        !Uri.TryCreate(imageUrl, UriKind.Absolute, out var image) ||
        image.Scheme != Uri.UriSchemeHttps ||
        !Uri.TryCreate(sourceUrl, UriKind.Absolute, out var source) ||
        source.Scheme != Uri.UriSchemeHttps)
        throw new InvalidDataException($"Line {lineNumber}: invalid artist, URL, credit, or license.");
    entries.Add((artist,imageUrl,credit,sourceUrl,license));
}
if (entries.GroupBy(x=>x.Artist,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))
    throw new InvalidDataException("Duplicate artist names in the input file.");
Console.WriteLine($"Validated {entries.Count} artist images. Mode: {(write?"WRITE":"PREVIEW")}");
foreach (var e in entries.Take(10)) Console.WriteLine($"{e.Artist}: {e.ImageUrl}");
if (!write) return 0;
await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();
await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
try
{
    foreach (var e in entries)
    {
        await using var command = new SqlCommand("""
            UPDATE dbo.ArtistImages
            SET ImageUrl = @image, Credit = @credit, SourceUrl = @source,
                License = @license, UpdatedAt = SYSUTCDATETIME()
            WHERE Artist = @artist;
            IF @@ROWCOUNT = 0
                INSERT INTO dbo.ArtistImages (Artist, ImageUrl, Credit, SourceUrl, License)
                VALUES (@artist, @image, @credit, @source, @license);
            """,connection,transaction);
        command.Parameters.Add("artist",SqlDbType.NVarChar,512).Value=e.Artist;
        command.Parameters.Add("image",SqlDbType.NVarChar,1000).Value=e.ImageUrl;
        command.Parameters.Add("credit",SqlDbType.NVarChar,500).Value=e.Credit;
        command.Parameters.Add("source",SqlDbType.NVarChar,1000).Value=e.SourceUrl;
        command.Parameters.Add("license",SqlDbType.NVarChar,100).Value=e.License;
        await command.ExecuteNonQueryAsync();
    }
    await transaction.CommitAsync();
    Console.WriteLine($"Upserted {entries.Count} artist images into AneedCatalog.dbo.ArtistImages.");
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
return 0;

# Artist image import (Nitro)

Uses the same **ANEED_AZURE_SQL_CONNECTION** environment variable as AneedWeb and `Microsoft.Data.SqlClient`. No ODBC connection string and no SAM dependency.

Populate `artist_images.tsv` with five **TAB-separated** fields in this order: `Artist`, `ImageUrl`, `Credit`, `SourceUrl`, `License`. Use the exact artist spelling from `dbo.Songs.Artist`. Check identities and permissions before adding rows; do not use album artwork as a substitute for artist photos.

```powershell
dotnet run --project tools/ArtistImageImport -- tools/ArtistImageImport/artist_images.tsv
dotnet run --project tools/ArtistImageImport -- tools/ArtistImageImport/artist_images.tsv --write
```

Run `scripts/azure_artwork_catalog.sql` in Azure SQL AneedCatalog before importing (it creates `dbo.ArtistImages` if absent). The website uses the image URL directly and falls back to station artwork when no artist image has been imported. For Wikimedia Commons images, preserve creator credit, original file description URL, and the exact license; verify the license permits display.

No image URLs are invented or downloaded by this utility.

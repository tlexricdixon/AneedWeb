# Find candidate artist photographs

Run from Nitro. Reads the most common artists without a photo from Azure SQL using `ANEED_AZURE_SQL_CONNECTION` (the existing AneedWeb connection). Looks up **exact-label** Wikidata matches, Wikidata P18 images, and Wikimedia Commons thumbnail and file metadata. Outputs TSV to stdout with an explicit review flag. **No database writes** and no automatic acceptance of ambiguous artist identities.

```powershell
dotnet run --project tools/ArtistPhotoCandidates -- 20 > artist_candidates.tsv
```

Inspect each candidate's Wikidata entity, Commons file description, license, attribution, and whether the depicted musician/band really matches the catalog artist. Copy approved rows into `tools/ArtistImageImport/artist_images.tsv`, retaining *exactly* five columns (Artist, ImageUrl, Credit, SourceUrl, License). Then preview/write using the separate importer.

Uses public API endpoints with a modest request delay. Some artists legitimately have no Wikidata P18 image; they won't appear. Candidate count does not guarantee photo count.

# Artist enrichment worker

This initial .NET 10 worker resolves queued artists to MusicBrainz artist IDs, Wikidata IDs and English Wikipedia URLs. It does not copy text, download images, or publish content.

Prerequisites: database migrations 002 and 003 applied to Nitro PostgreSQL; six artists queued in music.enrichment_jobs; ANEED_POSTGRES set.

In PowerShell, set a real contact email for polite identification to MusicBrainz and Wikimedia:

```powershell
$env:ANEED_CONTACT_EMAIL = "your-address@example.com"
dotnet run --project Aneed.ContentEnrichment -- --dry-run
dotnet run --project Aneed.ContentEnrichment
```

Dry run calls public APIs without writing to the database. The worker deliberately requires a unique high-scoring artist-name match and sends ambiguity to review. It handles SAM's inverted "B-52's, the" form without modifying the catalog name. API calls are spaced out; run only one worker instance at a time.

Check results:

```sql
SELECT a.id, a.name, j.status, j.last_error, s.match_status,
       s.musicbrainz_artist_mbid, s.wikidata_id, s.wikipedia_url
FROM music.enrichment_jobs j
JOIN music.artists a ON a.id=j.artist_id
LEFT JOIN music.artist_sources s ON s.artist_id=a.id
ORDER BY a.name;
```

The first pass intentionally leaves artist biographies and images for a subsequent, license-aware stage. Do not treat unreviewed matches as published content.

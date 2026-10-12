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

## Wikipedia biography drafts

After the identity pass has saved matched artist sources, run:

```powershell
dotnet run --project Aneed.ContentEnrichment -- --content --dry-run
dotnet run --project Aneed.ContentEnrichment -- --content
```

The content pass reads only matched artists that have an English Wikipedia URL, up to 20 at a time. It fetches each article's lead in plain text and stores it in `music.artist_content` as **needs_review**, never approved or published. It does not call Ollama or claim the extracted text is original. Existing `needs_review`, `approved`, or `rejected` rows are not overwritten. HTTP requests retry temporary errors.

Wikipedia writing is reused under Creative Commons Attribution-ShareAlike. Before publication, check factual accuracy, display appropriate source/author/license attribution and comply with ShareAlike; an alternative is writing new original editorial content based on verified facts. No images are downloaded by this pass.

Review in DBeaver:

```sql
SELECT a.name, c.review_status, left(c.summary, 350) AS preview,
       c.summary_source_url, c.license_notes
FROM music.artist_content c
JOIN music.artists a ON a.id=c.artist_id
ORDER BY a.name;
```

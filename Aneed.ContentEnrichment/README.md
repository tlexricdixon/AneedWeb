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

## Aneed editorial voice — first two artists

Run `database/004_artist_editorial.sql` against `aneed_analysis` first. Ensure Ollama is running on Nitro with model `ministral-3:8b`. The rewrite command uses `http://localhost:11434/` by default; override with `ANEED_OLLAMA_URL` if needed.

```powershell
dotnet run --project Aneed.ContentEnrichment -- --rewrite --dry-run
dotnet run --project Aneed.ContentEnrichment -- --rewrite
```

First pass targets **ABBA (1552)** and **Quarterflash (312)** only. Dry-run generates and prints draft text without storing it; regular mode inserts into `music.artist_editorial` with `needs_review`. It never overwrites an existing editorial draft, even if rejected, and does not publish to public artist pages.

Visit `http://localhost:5200/internal/artist-content/1552` and `/internal/artist-content/312` in local Development mode to compare drafts with original source extracts. This preview remains read-only.

Source Wikipedia text is preserved separately. AI rewriting does not automatically remove attribution or CC BY-SA obligations: treat the rewritten text as a potentially licensed adaptation, check facts, and review license details before publishing. Prompt version: `aneed-artist-v1`. The worker does not rewrite any approved material automatically.

## Voice prompt v2

The writer now uses verified song titles from catalog_tracks only as optional context (not lyrical interpretations), a more factual 90–120-word prompt, and one corrective pass for drafts outside the 80–130-word acceptance range. No track_analysis JSON is consumed yet. The two-artist test remains ABBA and Quarterflash. Dry run before inserting drafts; existing editorial rows are never overwritten.

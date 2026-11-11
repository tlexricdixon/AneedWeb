# Optional, read-only song editorial data

This branch extends the SAM recovery song popup. It does not alter `ANEED_AZURE_SQL_CONNECTION`, which points to Monster SQL in production.

## Deployment order
1. Back up Monster SQL. Have a DBA review `scripts/monster_song_analysis_published.sql` and create `dbo.SongAnalysisPublished` (separate from `dbo.Songs`).
2. Give the site's existing login SELECT on the published table.
3. Run the independent publisher tool from `AneedStaticSystem/tools/SongAnalysisPublisher` in dry-run mode, then publish a single explicitly approved song using restricted SQL credentials.
4. Confirm popup and /songs/{id} show Summary, Themes, Emotions and EmotionalArc.
5. Test missing table, database timeout, unknown song ID, and no-analysis rows. In all cases the normal song page must remain usable.

The `Song.Description` catalog model remains unchanged; no schema mutation to `dbo.Songs`. Catalog browse/search and SAM-history polling do not use the new table. The optional lookup uses `SamSongId` with a UNIQUE index and a 3-second cancellation limit; failures are not user-facing. No home call, Ollama call, or background web sync.

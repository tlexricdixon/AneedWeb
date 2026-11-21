# CLAP-grounded DJ rehearsal — local development

**Authoritative CLAP data:** SAM PostgreSQL, connected using the existing
`ANEED_POSTGRES` environment variable. No new connection variable is needed.

The AneedWeb rehearsal runs in Development only. On /studio, select
**Check SAM PostgreSQL CLAP schema** to inspect the actual columns of
`music.inventory`, `music.audio_embeddings`, `music.audio_features`,
`music.audio_segments`, and `music.lyric_analysis`, without changing data.
The connection string must be visible to the AneedWeb process; restart VS
if you set the variable after VS started.

Do not assume older Nitro `Aneed.ContentEnrichment` table definitions
match the newer completed SAM PostgreSQL analysis. Verify the exact
ANEED ID / SAM song ID crosswalk and JSONB top-description shape
before writing the real CLAP query.

## Rehearsal status

- The original DJ group conversation remains unchanged.
- A **Rehearse with current song + CLAP** action and a guarded, optional
  JSON CLAP context adapter were added in this feature branch as an
  initial bridge; the adapter is **not the target local architecture**.
- We will replace that adapter with a parameterized PostgreSQL query
  using `ANEED_POSTGRES` once the schema inspection confirms the real
  columns. Until then, the CLAP button needs an export file to produce
  song-grounded dialogue; it is **not yet connected directly to SAM**.
- No CLAP analysis is rerun, no SAM database writes occur, and Utah
  never connects back into SAM. Production DJ generation stays disabled.

For future Utah deployment, use a scheduled/batch sync of approved
song-facing analysis into Utah's own database. Do not expose SAM Postgres
or its connection string through Utah.

The existing JSON adapter accepts optional entries in
`Data/dj-clap-context.json`:
```json
[
  {
    "samSongId": 123,
    "aneedId": "00000000-0000-0000-0000-000000000001",
    "descriptions": ["Example only — not real analyzed audio"]
  }
]
```
That example is not evidence of a real match. It should not be
used as production data.

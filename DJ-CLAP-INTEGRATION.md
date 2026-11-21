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


## First local milestone: SAM song -> DJ dialogue

AneedWeb can now optionally read the **same public HTTPS history feed** already
uploaded by SAM to Utah. This is enabled **only when AneedWeb runs in Development**.
No connection into the SAM broadcast machine is necessary.

Set the following in Nitro's environment before launching Visual Studio:

- `OPENAI_API_KEY`: existing server-side OpenAI key.
- `ANEED_POSTGRES`: existing read-only-capable connection to SAM PostgreSQL
  (used for CLAP schema inspection; live CLAP lookup is not wired yet).
- `ANEED_SAM_HISTORY_URL`: the **actual HTTPS URL** of the existing
  Utah-hosted `/sam/history.json` feed, e.g.
  `https://your-real-station-host.example/sam/history.json`. The example
  hostname is a placeholder, not the real ANEED address.
- Optional `ANEED_DJ_MODEL`: OpenAI model override.

For a single PowerShell session, start Visual Studio *from that same session*
after setting the variables, so the web process inherits them. Alternatively
set persistent user environment variables and restart Visual Studio.

Open `/studio` and click **Refresh SAM song**. Confirm the song title,
artist, and SAM ID match the live broadcast. Refreshes use the existing
20-second broadcast cache. Then use **Let the DJs discuss the SAM song
(3 turns)**. This first rehearsal uses only the track metadata—no invented
audio-analysis notes—and does not require the CLAP export.

**Check SAM PostgreSQL CLAP schema** reports the real existing columns from
the `ANEED_POSTGRES` connection. The **Rehearse with current song + CLAP**
button still needs verified CLAP notes; direct SQL retrieval is the next
change once the schema and ID crosswalk are checked.

If `ANEED_SAM_HISTORY_URL` is omitted, the development site retains the
existing `wwwroot/sam/history.json` file behavior. Production never
honors `ANEED_SAM_HISTORY_URL`, retaining the original file path.

No new autonomous schedule or broadcasting changes were introduced.

# ANEED Now Playing AI lab — local prototype

This branch adds a **Development-only** manual Now Playing AI rehearsal screen at `/studio/now-playing-lab`, linked from `/studio`. It **does not** change SAM, the public player, the broadcast, or release behavior. It does not install a new background loop.

## Architecture

1. Existing `BroadcastService` reads fresh SAM `history.json`, whose current track includes the SAM numeric song ID.
2. Existing `ClapMusicAnalysisService` resolves `samdb.aneed_song_map` in MariaDB and verifies the mapped `AneedId` against PostgreSQL.
3. New `NowPlayingDjLab` reads the latest imported `ai.conversations.song_context` for that AneedID, plus up to eight past rehearsal dialogue lines, eight full DB character profiles, shared world, and **only** `ai.memories.is_canon=true` entries.
4. Preview does not call the model or write to the database.
5. Explicit Generate makes **one** OpenAI Responses API call to the configured model (default `gpt-6-luna`), validates 6–8 dialogue lines and known speakers, verifies SAM has not advanced to a different track, then saves the conversation, generation usage, and draft messages in a transaction. `prompt_version='dj-nowplaying-local-v1'`. No memory approvals, publishing, or audio broadcast.

**The rehearsal history is not canon.** No extracted-but-unapproved memory is sent. The sisters should improvise a new scene rather than replay previous lines.

## Local setup

Set environment variables in the AneedWeb process: `ANEED_SAM_MARIADB` (read-only access to `samdb.aneed_song_map`), `ANEED_POSTGRES` (read access to music/AI analysis plus write access to the three AI conversation tables), and `OPENAI_API_KEY` (for Generate only). Set `ASPNETCORE_ENVIRONMENT=Development`. Optional: `ANEED_SAM_HISTORY_URL` and `ANEED_DJ_MODEL`. Never commit credentials or model outputs.

Pull the branch, build, launch locally, then visit `/studio/now-playing-lab`. Click **Preview current song context** first. Verify artist, title, SAM ID, AneedID, profile count, rehearsal line count and approved-memory count. Click **Generate one local draft** only when the preview resolves correctly. Confirm the displayed conversation is new, coherent, grounded in the song and draft-only.

Example verification:

```sql
SELECT c.aneed_id, c.conversation_id, c.status, g.prompt_version,
       count(m.message_id) AS lines
FROM ai.conversations c
JOIN ai.generation_runs g ON g.conversation_id = c.conversation_id
JOIN ai.messages m ON m.generation_id = g.generation_id
WHERE g.prompt_version = 'dj-nowplaying-local-v1'
GROUP BY c.aneed_id, c.conversation_id, c.status, g.prompt_version
ORDER BY c.conversation_id DESC;
```

## Known work still needed

- **Build/test locally before merge.** This change was authored through GitHub and has not been compiled or tested against the private databases, SAM, or the OpenAI API.
- The current base `DjGroupRoom.cs` appears to include duplicate `_model` declarations and an undefined `logger` reference. Confirm the compilation state of master separately; these are not addressed by this branch.
- Add a stronger playback-instance check when SAM timestamps are reliable, avoid multiple manual submissions for the same track, apply an API spending cap, and test cancel/error handling.
- Once verified, integrate with the existing `DjPlaybackCoordinator` as an opt-in preview mode and display synchronized text. **Do not enable public automatic generation** until reliability and cost controls are complete.

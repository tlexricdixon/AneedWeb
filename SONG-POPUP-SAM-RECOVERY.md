# SAM-recovery song popup

This branch is based on `recovery/sam-history` and uses the existing Monster SQL catalog configuration. No PostgreSQL or database changes.

## Manual checks
1. `dotnet build` from the feature branch / worktree.
2. Verify Library, artist, album, Now Playing, Recently Played, and persistent player open the popup on song title selection.
3. Verify song IDs correspond to SAM ID; unknown ID shows a not-found message and no fabricated metadata.
4. Confirm album artwork, title, artist, album, year, genre and ISRC where present.
5. Test Escape, Close, focus, rapid alternate clicks and mobile layout.
6. Verify `/songs/{id}` still renders directly for bookmarking.
7. Confirm connection variable `ANEED_AZURE_SQL_CONNECTION` unchanged and SAM `wwwroot/sam/history.json` still supplies the broadcast.

Song requests, callback to SAM and Ollama integration are intentionally deferred to a separate feature with defined request acceptance semantics and security.

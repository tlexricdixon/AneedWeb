# Song details popup

This branch targets `dev`, the PostgreSQL-backed AneedWeb implementation.
It does not change catalog SQL, SAM playback, the artist editorial workflow, or the
separate DJ studio draft branch.

## Behavior
- Clicking a known song from the library, an artist or album, Now Playing, or
  Recently Played opens a native HTML `<dialog>` with its SAM song ID.
- The scoped `SongPopupState` is per Blazor circuit; song state is never shared
  among website visitors.
- Details come from the existing read-only `LibraryRepository.Songs(id: id)`.
- The popup uses existing fields only and doesn't invent missing metadata.
- Unmatched Live365 songs still navigate to a library search.
- The permanent `/songs/{id}` route remains available for sharing and direct links.
- Native modal behavior supports focus handling and Escape. Close button meets
  44px target. No NuGet packages added.

## Manual checks (not yet run)
1. Build and run against the CURRENT PostgreSQL catalog (`ANEED_POSTGRES`).
2. Library -> click a song: modal shows correct ID, title and artist.
3. Artist, album, Now Playing and Recently Played: correct song in modal.
4. Unknown Live365 item: use search fallback rather than a blank modal.
5. Close with the button and Escape; keyboard Tab should stay inside the native modal,
   with focus restored on close.
6. Click artist and permanent song links inside the modal and confirm it closes.
7. Try slow or disconnected DB, missing artwork and rapid clicks on two songs.
8. Verify 375px mobile layout; page is still usable behind/after the modal.
9. Confirm `/songs/{id}` works as a standalone shareable route.

## Next feature
A song request page with a durable, validated queue and SAM integration rules,
including eligibility, deduplication, rate limiting, and acknowledgement.
The uploaded SAM `common.js` shows only how SAM opens `request.html?songID`;
it does **not** define its request persistence or scheduling behavior. Do not
claim a listener request has been accepted until the queue actually stores it.

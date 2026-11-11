# Aneed DJ Studio — integration proof of concept

This branch ports the **development-only** shared DJ rehearsal from
`tlexricdixon/AlexiTheMVCBot` into the existing .NET 10 Blazor Server AneedWeb site.

## Run locally
1. Check out this branch, start Ollama, and ensure `qwen3.5:2b` is available.
2. Configure `Ollama:BaseUrl` and `Ollama:Model` in local app settings/user secrets
   (defaults: `http://localhost:11434`, `qwen3.5:2b`). Never commit secrets.
3. Run AneedWeb with `ASPNETCORE_ENVIRONMENT=Development`.
4. Browse to `/studio`. Click **Let the DJs talk (3 turns)** and **Start over**.
5. Open a second browser window and verify live shared updates, distinct personalities,
   coherent transcript, error behavior, and that generation stops at three turns.
6. Run in Production mode and confirm the controls are not available.

## Portraits
The port uses eight existing profile paths, e.g. `/img/djs/kathleen.jpg`, expected
beneath `AneedWeb/wwwroot/img/djs/`. Binary images have not been copied in this
branch and must be supplied separately. Until then, initials are shown.

## Safety and next phases
- This prototype is **not yet a public listener chat**. The route is reachable in
  production, but generation is disabled outside Development. Do not flip that
  switch without server-side moderation, limits and an inference budget.
- It uses a single in-process room, bounded message history and three sequential
  model replies per click. No autonomous background task or database writes.
- Hosted AneedWeb cannot reach the home Ollama service with `localhost`; plan
  a private authenticated connection to the inference host before production.
  Do not expose Ollama's raw API directly to the public Internet.
- Integrate durable chat history into the deliberately chosen database after
  verifying the actual production data-store and deployment topology. The
  checked-in AneedWeb catalog currently uses `Microsoft.Data.SqlClient`.
- Later: authenticated private DJ chats with email sign-in, isolated history,
  deletion and retention controls. Not part of this POC.
- Before merging, build and test locally; no compilation or browser test has
  been performed by this GitHub-only change.

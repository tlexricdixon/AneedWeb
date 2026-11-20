# Aneed DJ Studio — integration proof of concept

This branch ports the **development-only** shared DJ rehearsal from
`tlexricdixon/AlexiTheMVCBot` into the existing .NET 10 Blazor Server AneedWeb site.

## Run locally
1. Set `OPENAI_API_KEY` in the environment inherited by the .NET process. Do not put
   the key in appsettings, a secrets file, or source control. Restart Visual Studio if
   it was launched before the variable was created.
2. Run AneedWeb with `ASPNETCORE_ENVIRONMENT=Development`.
3. Browse to `/studio`. The page reports whether the process can read the key. Click
   **Let the DJs talk (3 turns)** and **Start over**.
4. Open a second browser window and verify live shared updates, distinct personalities,
   coherent transcript, error behavior, and that generation stops at three turns.
5. Run in Production mode and confirm generation remains disabled.

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
- DJ rehearsal generation calls the OpenAI Responses API using the server-side
  `OPENAI_API_KEY` and the `gpt-6-luna` model. Generation remains disabled outside
  Development; do not make this rehearsal public without separately reviewing
  moderation, limits, and inference costs.
- Integrate durable chat history into the deliberately chosen database after
  verifying the actual production data-store and deployment topology. The
  checked-in AneedWeb catalog currently uses `Microsoft.Data.SqlClient`.
- Later: authenticated private DJ chats with email sign-in, isolated history,
  deletion and retention controls. Not part of this POC.
- Before merging, build and test locally; no compilation or browser test has
  been performed by this GitHub-only change.

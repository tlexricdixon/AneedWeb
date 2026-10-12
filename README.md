# Aneed Static System

.NET 10 Blazor Interactive Server website for Live365 station a30355, using the station’s existing MariaDB SAMDB music library.

## Run

```powershell
dotnet restore
dotnet run --urls http://localhost:5200
```

Open http://localhost:5200. Live365 playback and track metadata work without MariaDB credentials. Playback begins when the listener presses Play. Audio stays mounted when navigating within the Blazor site. Volume, pause, and the official full-player link are available on desktop and mobile.

## Connect the existing SAMDB database

No migrations, new tables, or runtime database writes are required. The repository reads `songlist` from the supplied schema. Configure a SELECT-only account for that table, using user secrets locally or an environment variable in production:

```powershell
dotnet user-secrets set "ConnectionStrings:Library" "Server=YOUR_HOST;Database=SAMDB;User=YOUR_READONLY_USER;Password=YOUR_PASSWORD;SslMode=Required"
```

For deployment use `ConnectionStrings__Library`. Keep passwords out of source control and chat. Adjust TLS settings for your MariaDB server. No actual database credentials have been supplied or configured yet.

### SAM data mapping

- Songs use the existing `songlist.ID`, `title`, `artist`, `album`, `albumyear`, `info`, `picture`, `genre`, `composer`, `label`, and `ISRC` fields.
- `songtype='S'` limits the public library to music; ads and station IDs are excluded.
- Artists are grouped from `songlist.artist`. Artist URLs use a representative song ID to look up the artist because SAM has no artist primary key. Deleting that representative song will invalidate that particular URL. An explicit artist table can be added separately in a future phase if persistent IDs and biographies are needed.
- Artist pages show library counts, genres, albums, and song links. SAM has no artist biography field, so no biography is invented.
- `info` is displayed as plain text. Music file paths and lyrics are not exposed.
- Public HTTP(S) `picture` URLs are used as artwork. Windows file paths fall back to station artwork. Serving local SAM album covers requires a separate public asset mapping.
- Parameterized substring search checks both song title and artist. Search covers the entire table and displays up to 100 songs and artists per query; refine the query for more specific matches. Matching follows the database’s existing collation.
- Live365 titles and artist names are matched to SAM songs for detail links; unknown tracks lead to a library search. Broadcast matching does not depend on the first 100 library entries.
- `historylist`, requests, queues, and other broadcasting tables are untouched. Now-playing and recent history come from Live365 rather than the local automation queue.

`database/schema.sql` documents the read contract only and contains no executable schema changes.

## Editorial feed

SAM has no editorial feed table. `Data/feed.json` holds one typed content list outside the broadcaster database. It is initially empty, so the page shows an honest coming-soon state. Each record uses:

```json
{
  "id": 1,
  "type": "StationUpdate",
  "title": "Your station announcement",
  "excerpt": "A short introduction.",
  "body": "Full plain-text article.",
  "image": "/images/station-cover.png",
  "published": "2026-10-11T12:00:00Z"
}
```

Supported editorial types: StationUpdate, Article, FeaturedSet, AlbumFeature, ArtistSpotlight, ShowPost. Future publication dates are hidden. This file is copied on publish. No editorial admin panel or external RSS ingestion is included yet.

## Live365

Metadata is fetched server-side from the observed public station endpoint `https://api.live365.com/station/a30355`, cached for 20 seconds, and refreshed on the page every 30 seconds. The endpoint currently returns five recent songs; the website displays up to ten if supplied. There is no fabricated play history. Metadata failures preserve the last snapshot with an unavailable notice.

The public endpoint is an observed integration rather than a documented API contract; validate it for production. Audio uses the stream returned by Live365. The full-player link opens the official station profile. Live365 also offers official player and history embeds through the broadcaster dashboard Listen section; confirm station account requirements before launch.

Logo and cover artwork are from the supplied station profile. Google Fonts have local fallbacks. Programming blocks are labeled concepts with no invented schedule. Contact links to the station profile until an official contact address is available.

## Validation

```powershell
dotnet build
```

Validation completed with an isolated MariaDB 11.4.5 instance and a SELECT-only fixture account. It covered SAM column mapping, songs and grouped artists, literal percent/apostrophe search, SQL-like input, filtering ads, unknown IDs, and broadcast lookup outside the first 100 songs. Playwright checks covered desktop/mobile rendering, details navigation, artwork fallback, volume, and persistent audio identity. A real Live365 playback check verified play, uninterrupted audio through navigation, and pause.

To repeat, load `tests/sam-fixture.sql` into a disposable MariaDB instance (never production). Run the application on port 5201 with a fixture connection and run:

```powershell
dotnet run --project tests/RepositoryChecks -- --ConnectionStrings:Library="Server=127.0.0.1;Port=53306;Database=aneed_fixture;User=aneed_fixture_reader;SslMode=None"
npm install --prefix tests
npm test --prefix tests
```

The fixture account is passwordless and SELECT-only for this temporary local instance; it is not suitable for deployment. Browser tests use Microsoft Edge by default; set `BROWSER_CHANNEL` to use another installed Playwright browser. Set `SITE_URL` to override http://localhost:5201. Browser test data is synthetic; production library contents have not been accessed.

Real station database connectivity still needs the station’s connection settings.

# AneedWeb

.NET 10 Blazor Interactive Server website for A Need Static System, with Live365 playback and a public, read-only Azure SQL song catalog.

## Database

Set `ANEED_AZURE_SQL_CONNECTION` as a **server-side** environment variable (Azure App Service application setting or local development environment). The connection points at the Azure SQL `AneedCatalog` database and the `dbo.Songs` table; never commit its value. Use a SELECT-only database principal for the deployed website.

The catalog is populated separately from SAM Broadcaster's MariaDB. The public site must not connect to SAM or modify rotation/playback data. Song URLs are keyed by the stable `SamSongId`; artist routes use deterministic IDs derived from artist names by `Data/ArtistIdentity.cs`.

## Local run

```powershell
if (-not $env:ANEED_AZURE_SQL_CONNECTION) { throw "ANEED_AZURE_SQL_CONNECTION is not set" }
dotnet restore
dotnet build
dotnet run --urls http://localhost:5200
```

Visit `http://localhost:5200/library`, browse artists at `/artists`, and test the Live365 player. The catalog uses `Microsoft.Data.SqlClient`; it has no PostgreSQL requirement.

## Tests

```powershell
dotnet run --project tests/RepositoryChecks/RepositoryChecks.csproj
```

These checks cover deterministic artist IDs, SQL search escaping, pagination bounds, and missing configuration; they do not replace a live Azure SQL integration test.

## Other responsibilities

Live365 metadata and streaming remain managed by `Services/BroadcastService.cs` and the player components. `Data/feed.json` supplies website editorial/news posts. AI analysis, artist biography drafting, Ollama, CLAP import, and PostgreSQL migrations belong in the separate `AneedStaticSystem` repository and are not required to deploy AneedWeb.

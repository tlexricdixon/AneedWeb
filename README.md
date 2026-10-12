# Aneed Static System — local PostgreSQL catalog

.NET 10 Blazor Interactive Server website for Live365 station `a30355`.

## Nitro setup (no SAM connection required)

1. Run `database/002_artists_postgres.sql` once against the existing **local PostgreSQL** `aneed_analysis` database, using DBeaver or psql. The script creates `music.artists`, backfills `music.catalog_tracks.artist_id`, and installs a trigger for new/updated catalog songs. Existing MusicBrainz and analysis tables remain intact.
2. From the website repository directory, configure a read-only connection string with user secrets (not in Git):

```powershell
dotnet user-secrets set "ConnectionStrings:Library" "Host=localhost;Port=5432;Database=aneed_analysis;Username=YOUR_READONLY_USER;Password=YOUR_PASSWORD"
dotnet restore
dotnet build
dotnet run --urls http://localhost:5200
```

If Nitro PostgreSQL runs on a different port, adjust accordingly. Open http://localhost:5200.

**Important:** the website connection user needs only SELECT privileges on `music.artists` and `music.catalog_tracks`, plus USAGE on schema `music`. Run the migration as a database owner, not as the website reader.

Create a dedicated reader in PostgreSQL (replace the password):

```sql
CREATE ROLE aneed_web_reader LOGIN PASSWORD 'REPLACE_WITH_STRONG_PASSWORD';
GRANT CONNECT ON DATABASE aneed_analysis TO aneed_web_reader;
GRANT USAGE ON SCHEMA music TO aneed_web_reader;
GRANT SELECT ON music.artists, music.catalog_tracks TO aneed_web_reader;
```

Do not commit database passwords or expose Nitro to incoming public connections. For Azure later, use an **outbound-only publish/sync** to an Azure PostgreSQL public-safe catalog.

## Catalog contract

- Song routes use SAM `sam_song_id`: `/songs/{sam_song_id}`.
- Artist routes use stable `music.artists.id`: `/artists/{artist_id}`.
- Tracks come from `music.catalog_tracks`, populated by our existing MariaDB → PostgreSQL sync.
- This branch intentionally leaves missing fields (artwork, description, composer, label) empty instead of inventing them. Existing UI falls back to the station image.
- Artist names are exact as imported; aliases/normalization are a future enhancement.
- Live365 continues to supply the stream, current track, and recent-played metadata.
- `Data/feed.json` supplies editorial posts; no admin system yet.
- The older MariaDB fixture in `tests/sam-fixture.sql` does **not** test this PostgreSQL implementation. Test fixtures must be ported before CI database tests can validate the new repository.

## Quick verification after running migration

```sql
SELECT count(*) AS artist_count FROM music.artists;
SELECT count(*) AS track_count FROM music.catalog_tracks;
SELECT count(*) AS missing_artist_fk
FROM music.catalog_tracks
WHERE btrim(artist) <> '' AND artist_id IS NULL;

SELECT c.sam_song_id, c.title, a.name, a.id AS artist_id
FROM music.catalog_tracks c
JOIN music.artists a ON a.id = c.artist_id
ORDER BY c.sam_song_id LIMIT 10;
```

Expected `missing_artist_fk` is zero. Database triggers handle later inserts and artist-name edits. The catalog synchronization job remains separate and unchanged.

## Playback

Playback starts only when the listener presses Play. The player remains mounted across Blazor internal navigation, with volume/pause and a fallback link to the official Live365 player. The public Live365 metadata endpoint is an observed integration, not a guaranteed documented API contract.

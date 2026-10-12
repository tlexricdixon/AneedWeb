-- Run once against the existing Nitro aneed_analysis PostgreSQL database.
-- Does not modify SAM or erase any existing catalog, analysis, or matching data.
BEGIN;
CREATE SCHEMA IF NOT EXISTS music;
CREATE TABLE IF NOT EXISTS music.artists (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name TEXT NOT NULL,
    CONSTRAINT artists_name_not_blank CHECK (btrim(name) <> '')
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_music_artists_name ON music.artists (name);
ALTER TABLE music.catalog_tracks ADD COLUMN IF NOT EXISTS artist_id BIGINT;
INSERT INTO music.artists (name)
SELECT DISTINCT btrim(artist)
FROM music.catalog_tracks
WHERE btrim(artist) <> ''
ON CONFLICT (name) DO NOTHING;
UPDATE music.catalog_tracks c SET artist_id = a.id
FROM music.artists a
WHERE c.artist_id IS DISTINCT FROM a.id AND btrim(c.artist) = a.name;
CREATE INDEX IF NOT EXISTS ix_music_catalog_tracks_artist_id ON music.catalog_tracks (artist_id);
DO $$
BEGIN
 IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conname = 'fk_catalog_tracks_artist'
      AND conrelid = 'music.catalog_tracks'::regclass
 ) THEN
   ALTER TABLE music.catalog_tracks
      ADD CONSTRAINT fk_catalog_tracks_artist
      FOREIGN KEY (artist_id) REFERENCES music.artists(id);
 END IF;
END $$;
CREATE OR REPLACE FUNCTION music.resolve_track_artist()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE resolved BIGINT; normalized TEXT;
BEGIN
  normalized := btrim(coalesce(NEW.artist,''));
  IF normalized = '' THEN
    NEW.artist_id := NULL;
    RETURN NEW;
  END IF;
  INSERT INTO music.artists(name) VALUES(normalized)
  ON CONFLICT (name) DO UPDATE SET name = EXCLUDED.name
  RETURNING id INTO resolved;
  NEW.artist_id := resolved;
  RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS trg_resolve_track_artist ON music.catalog_tracks;
CREATE TRIGGER trg_resolve_track_artist
BEFORE INSERT OR UPDATE OF artist ON music.catalog_tracks
FOR EACH ROW EXECUTE FUNCTION music.resolve_track_artist();
COMMIT;
-- Verify:
-- SELECT count(*) FROM music.artists;
-- SELECT count(*) FROM music.catalog_tracks WHERE nullif(btrim(artist),'') IS NOT NULL AND artist_id IS NULL;

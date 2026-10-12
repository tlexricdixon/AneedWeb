-- Separate editorial rewrites from licensed Wikipedia source extracts.
BEGIN;
CREATE TABLE IF NOT EXISTS music.artist_editorial (
    artist_id BIGINT PRIMARY KEY REFERENCES music.artists(id) ON DELETE CASCADE,
    body TEXT NOT NULL CHECK (btrim(body) <> ''),
    status TEXT NOT NULL DEFAULT 'needs_review'
      CHECK (status IN ('needs_review','approved','rejected')),
    model_name TEXT NOT NULL,
    prompt_version TEXT NOT NULL,
    source_url TEXT NOT NULL,
    license_notes TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    approved_at TIMESTAMPTZ
);
COMMIT;

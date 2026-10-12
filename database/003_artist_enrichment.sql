-- Aneed artist enrichment foundation.
-- Run after database/002_artists_postgres.sql against Nitro's aneed_analysis.
-- This creates no content and changes no existing artist or track data.
BEGIN;

CREATE TABLE IF NOT EXISTS music.artist_sources (
    artist_id BIGINT PRIMARY KEY REFERENCES music.artists(id) ON DELETE CASCADE,
    musicbrainz_artist_mbid UUID,
    wikidata_id TEXT,
    wikipedia_url TEXT,
    match_status TEXT NOT NULL DEFAULT 'pending'
        CHECK (match_status IN ('pending','candidate','matched','review','unavailable')),
    matched_at TIMESTAMPTZ,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CHECK (wikidata_id IS NULL OR wikidata_id ~ '^Q[1-9][0-9]*$'),
    CHECK (wikipedia_url IS NULL OR wikipedia_url ~ '^https://')
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_artist_sources_mb_artist
    ON music.artist_sources (musicbrainz_artist_mbid)
    WHERE musicbrainz_artist_mbid IS NOT NULL;

CREATE TABLE IF NOT EXISTS music.artist_content (
    artist_id BIGINT PRIMARY KEY REFERENCES music.artists(id) ON DELETE CASCADE,
    summary TEXT,
    summary_source_url TEXT,
    license_notes TEXT,
    review_status TEXT NOT NULL DEFAULT 'draft'
        CHECK (review_status IN ('draft','needs_review','approved','rejected')),
    generated_by TEXT,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    approved_at TIMESTAMPTZ,
    CHECK (summary_source_url IS NULL OR summary_source_url ~ '^https://')
);

CREATE TABLE IF NOT EXISTS music.media_assets (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    artist_id BIGINT NOT NULL REFERENCES music.artists(id) ON DELETE CASCADE,
    source_url TEXT NOT NULL,
    attribution TEXT,
    creator TEXT,
    license_name TEXT,
    license_url TEXT,
    source_page_url TEXT,
    approved BOOLEAN NOT NULL DEFAULT false,
    retrieved_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (artist_id, source_url),
    CHECK (source_url ~ '^https://')
);
CREATE INDEX IF NOT EXISTS ix_media_assets_artist ON music.media_assets(artist_id);

CREATE TABLE IF NOT EXISTS music.enrichment_jobs (
    artist_id BIGINT PRIMARY KEY REFERENCES music.artists(id) ON DELETE CASCADE,
    status TEXT NOT NULL DEFAULT 'pending'
        CHECK (status IN ('pending','processing','completed','review','error')),
    attempts INTEGER NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    last_error TEXT,
    next_attempt_at TIMESTAMPTZ,
    started_at TIMESTAMPTZ,
    completed_at TIMESTAMPTZ,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_enrichment_jobs_queue
    ON music.enrichment_jobs(status, next_attempt_at);

COMMIT;

-- Populate an initial, deliberately small batch after reviewing artist names:
-- INSERT INTO music.enrichment_jobs (artist_id)
-- SELECT id FROM music.artists WHERE name IN ('ABBA', 'Bad Company', 'Quarterflash')
-- ON CONFLICT (artist_id) DO NOTHING;

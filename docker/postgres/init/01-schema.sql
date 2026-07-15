-- Dummy schema for demonstrating replication (spec §3.5).
-- Only runs on the primary's first boot (initdb bootstrap) — replicas receive these
-- tables automatically via pg_basebackup + WAL streaming.

CREATE TABLE IF NOT EXISTS datasets (
    id         SERIAL PRIMARY KEY,
    title      TEXT NOT NULL,
    owner      TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS publications (
    id         SERIAL PRIMARY KEY,
    title      TEXT NOT NULL,
    dataset_id INTEGER REFERENCES datasets (id) ON DELETE CASCADE,
    doi        TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS resources (
    id         SERIAL PRIMARY KEY,
    name       TEXT NOT NULL,
    type       TEXT NOT NULL,
    status     TEXT NOT NULL DEFAULT 'active',
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

INSERT INTO datasets (title, owner) VALUES ('Sample Dataset', 'mrs-demo') ON CONFLICT DO NOTHING;

-- ─────────────────────────────────────────────
--  01_init_schema.sql
--  MRS demo schema — runs automatically on primary first boot
-- ─────────────────────────────────────────────

-- Registered datasets (measurement results, experiment outputs)
CREATE TABLE IF NOT EXISTS datasets (
    id          SERIAL PRIMARY KEY,
    title       VARCHAR(255) NOT NULL,
    doi         VARCHAR(100) UNIQUE,
    description TEXT,
    created_at  TIMESTAMP DEFAULT NOW()
);

-- Academic publications linked to SLICES experiments
CREATE TABLE IF NOT EXISTS publications (
    id          SERIAL PRIMARY KEY,
    title       VARCHAR(255) NOT NULL,
    authors     TEXT,
    journal     VARCHAR(255),
    year        INT,
    doi         VARCHAR(100) UNIQUE
);

-- Software tools and scripts registered in MRS
CREATE TABLE IF NOT EXISTS software_tools (
    id             SERIAL PRIMARY KEY,
    name           VARCHAR(255) NOT NULL,
    version        VARCHAR(50),
    language       VARCHAR(50),
    repository_url TEXT,
    registered_at  TIMESTAMP DEFAULT NOW()
);
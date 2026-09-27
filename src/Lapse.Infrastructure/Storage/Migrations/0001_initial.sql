CREATE TABLE items (
    id              INTEGER PRIMARY KEY,
    kind            TEXT NOT NULL,
    key             TEXT NOT NULL,
    name            TEXT NOT NULL,
    owner           TEXT NOT NULL,
    expires_at      TEXT,
    status          TEXT NOT NULL,
    last_scanned_at TEXT NOT NULL,
    last_error      TEXT,
    UNIQUE (kind, key)
);

CREATE TABLE observations (
    item_id     INTEGER NOT NULL REFERENCES items (id) ON DELETE CASCADE,
    observed_at TEXT NOT NULL,
    expires_at  TEXT NOT NULL,
    fingerprint TEXT
);

CREATE INDEX ix_observations_item ON observations (item_id, observed_at);

CREATE TABLE alerts_sent (
    item_id        INTEGER NOT NULL REFERENCES items (id) ON DELETE CASCADE,
    kind           TEXT NOT NULL,
    threshold_days INTEGER NOT NULL,
    expires_at     TEXT NOT NULL,
    channel        TEXT NOT NULL,
    sent_at        TEXT NOT NULL,
    PRIMARY KEY (item_id, kind, threshold_days, expires_at, channel)
);

ALTER TABLE items ADD COLUMN target_kind TEXT;
ALTER TABLE items ADD COLUMN target_key TEXT;

UPDATE items SET target_kind = kind, target_key = key;

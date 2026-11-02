-- run once against Team04_DB before deploying this branch

-- bumped to sign a user out of every session
ALTER TABLE users ADD COLUMN session_version INT NOT NULL DEFAULT 0;

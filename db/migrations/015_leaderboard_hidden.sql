-- run once against Team04_DB before deploying this branch

-- drivers who hide their name show as anonymous on the leaderboard
ALTER TABLE users ADD COLUMN leaderboard_hidden TINYINT(1) NOT NULL DEFAULT 0;

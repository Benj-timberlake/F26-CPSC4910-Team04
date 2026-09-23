-- run against a database that was created before users.points existed (local volume or RDS)
-- new databases get the column from schema.sql
ALTER TABLE users ADD COLUMN points INT NOT NULL DEFAULT 0;

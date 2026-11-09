-- run once against Team04_DB before deploying this branch

-- one row per minute from the backend's database check
CREATE TABLE IF NOT EXISTS uptime_checks (
    id INT NOT NULL AUTO_INCREMENT,
    checked_at DATETIME NOT NULL,
    ok TINYINT(1) NOT NULL,
    latency_ms INT NOT NULL,
    error VARCHAR(255) NULL,
    PRIMARY KEY (id),
    KEY ix_uptime_checks_checked (checked_at)
);

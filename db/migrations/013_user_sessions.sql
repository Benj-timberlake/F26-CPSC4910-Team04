-- run once against Team04_DB before deploying this branch

-- one row per signed-in browser, for the devices page. only a hash of the cookie's key is stored
CREATE TABLE IF NOT EXISTS user_sessions (
    id INT NOT NULL AUTO_INCREMENT,
    user_id INT NOT NULL,
    key_hash CHAR(64) NOT NULL,
    created_at DATETIME NOT NULL,
    last_seen_at DATETIME NOT NULL,
    ip_address VARCHAR(45) NULL,
    user_agent VARCHAR(255) NULL,
    revoked_at DATETIME NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_user_sessions_key (key_hash),
    KEY ix_user_sessions_user (user_id, last_seen_at),
    CONSTRAINT fk_user_sessions_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

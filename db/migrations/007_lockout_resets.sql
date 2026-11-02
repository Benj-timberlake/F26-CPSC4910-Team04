-- run once against Team04_DB before deploying this branch

-- admins lifting a lockout early
CREATE TABLE IF NOT EXISTS lockout_resets (
    id INT NOT NULL AUTO_INCREMENT,
    username VARCHAR(255) NOT NULL,
    user_id INT NULL,
    admin_id INT NOT NULL,
    reset_at DATETIME NOT NULL,
    PRIMARY KEY (id),
    KEY ix_lockout_resets_username (username),
    CONSTRAINT fk_lockout_resets_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_lockout_resets_admin FOREIGN KEY (admin_id) REFERENCES users (id) ON DELETE CASCADE
);

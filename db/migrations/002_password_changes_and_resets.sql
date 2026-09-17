-- password change audit log and forgot-password tokens, see schema.sql for the column notes
CREATE TABLE IF NOT EXISTS password_changes (
    id INT NOT NULL AUTO_INCREMENT,
    user_id INT NOT NULL,
    change_type ENUM('changed','reset_requested','reset_completed') NOT NULL,
    ip_address VARCHAR(45) NULL,
    changed_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    KEY ix_password_changes_user (user_id),
    CONSTRAINT fk_password_changes_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS password_resets (
    id INT NOT NULL AUTO_INCREMENT,
    user_id INT NOT NULL,
    token_hash CHAR(64) NOT NULL,
    expires_at DATETIME NOT NULL,
    used_at DATETIME NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_password_resets_token (token_hash),
    CONSTRAINT fk_password_resets_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

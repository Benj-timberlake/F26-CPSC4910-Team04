-- Team04_DB was created from an older schema.sql; this brings it up to the current one

ALTER TABLE users
    MODIFY user_type ENUM('admin','sponser','sponsor','driver') NOT NULL;
UPDATE users SET user_type = 'sponsor' WHERE user_type = 'sponser';
ALTER TABLE users
    MODIFY user_type ENUM('admin','sponsor','driver') NOT NULL,
    MODIFY password VARCHAR(255) NULL,
    ADD UNIQUE KEY uq_users_username (username),
    ADD UNIQUE KEY uq_users_email (email);

ALTER TABLE sponsors
    DROP PRIMARY KEY,
    ADD COLUMN id INT NOT NULL AUTO_INCREMENT FIRST,
    ADD PRIMARY KEY (id),
    ADD UNIQUE KEY uq_sponsors_user (user_id),
    ADD CONSTRAINT fk_sponsors_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE;

ALTER TABLE login_attempts
    MODIFY user_id INT NULL,
    MODIFY attempted_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ADD KEY ix_login_attempts_user (user_id),
    ADD CONSTRAINT fk_login_attempts_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE SET NULL;

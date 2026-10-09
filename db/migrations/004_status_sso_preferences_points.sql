-- run once against Team04_DB before deploying this branch

-- null for google/microsoft accounts in both tables
ALTER TABLE users MODIFY password VARCHAR(255) NULL;
ALTER TABLE accounts_history MODIFY password VARCHAR(255) NULL;

-- admins can deactivate an account without deleting it
ALTER TABLE users
    ADD COLUMN status ENUM('active','inactive') NOT NULL DEFAULT 'active';

-- one row per user and category the user changed from the default
CREATE TABLE IF NOT EXISTS notification_preferences (
    user_id INT NOT NULL,
    category VARCHAR(30) NOT NULL,
    enabled TINYINT(1) NOT NULL DEFAULT 1,
    emailed TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (user_id, category),
    CONSTRAINT fk_notification_preferences_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

-- the app updates users.points itself
DROP TRIGGER IF EXISTS points_history_after_insert;

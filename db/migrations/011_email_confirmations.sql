-- run once against Team04_DB before deploying this branch

-- null until the user clicks the emailed link. accounts from before this count as confirmed
ALTER TABLE users ADD COLUMN email_confirmed_at DATETIME NULL;
UPDATE users SET email_confirmed_at = UTC_TIMESTAMP();

-- confirmation links. only a hash of the token is stored
CREATE TABLE IF NOT EXISTS email_confirmations (
    id INT NOT NULL AUTO_INCREMENT,
    user_id INT NOT NULL,
    email VARCHAR(255) NOT NULL,
    token_hash CHAR(64) NOT NULL,
    expires_at DATETIME NOT NULL,
    used_at DATETIME NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_email_confirmations_token (token_hash),
    CONSTRAINT fk_email_confirmations_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

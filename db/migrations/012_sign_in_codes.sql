-- run once against Team04_DB before deploying this branch

-- sign in also needs a code emailed to the confirmed address
ALTER TABLE users ADD COLUMN email_codes TINYINT(1) NOT NULL DEFAULT 0;

-- the emailed codes. only hashes of the code and its challenge are stored
CREATE TABLE IF NOT EXISTS sign_in_codes (
    id INT NOT NULL AUTO_INCREMENT,
    user_id INT NOT NULL,
    challenge_hash CHAR(64) NOT NULL,
    code_hash CHAR(64) NOT NULL,
    created_at DATETIME NOT NULL,
    expires_at DATETIME NOT NULL,
    attempts INT NOT NULL DEFAULT 0,
    used_at DATETIME NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_sign_in_codes_challenge (challenge_hash),
    KEY ix_sign_in_codes_user (user_id, created_at),
    CONSTRAINT fk_sign_in_codes_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

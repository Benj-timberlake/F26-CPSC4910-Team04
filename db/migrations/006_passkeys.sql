-- run once against Team04_DB before deploying this branch

-- passkeys (face id, fingerprint, security keys) for signing in
CREATE TABLE IF NOT EXISTS passkey_credentials (
    id INT NOT NULL AUTO_INCREMENT,
    user_id INT NOT NULL,
    credential_id VARBINARY(1023) NOT NULL,
    public_key BLOB NOT NULL,
    sign_count BIGINT NOT NULL DEFAULT 0,
    name VARCHAR(100) NOT NULL,
    created_at DATETIME NOT NULL,
    last_used_at DATETIME NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_passkey_credentials_credential (credential_id),
    CONSTRAINT fk_passkey_credentials_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

-- users table, matches BackEnd/Models/AppDbContext.cs
-- password holds the salted hash, never the plain password
CREATE TABLE IF NOT EXISTS users (
    id INT NOT NULL AUTO_INCREMENT,
    user_type ENUM('admin','sponsor','driver') NOT NULL,
    username VARCHAR(255) NOT NULL,
    password VARCHAR(255) NULL,
    email VARCHAR(255) NOT NULL,
    phone_number VARCHAR(20) NOT NULL,
    address VARCHAR(255) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_users_username (username),
    UNIQUE KEY uq_users_email (email)
);

-- extra info for sponsor accounts
CREATE TABLE IF NOT EXISTS sponsors (
    id INT NOT NULL AUTO_INCREMENT,
    user_id INT NOT NULL,
    company_name VARCHAR(255) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_sponsors_user (user_id),
    CONSTRAINT fk_sponsors_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

-- every login attempt, good or bad, so admins can review failures
CREATE TABLE IF NOT EXISTS login_attempts (
    id INT NOT NULL AUTO_INCREMENT,
    username VARCHAR(255) NOT NULL,
    user_id INT NULL,
    succeeded TINYINT(1) NOT NULL,
    ip_address VARCHAR(45) NULL,
    attempted_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    KEY ix_login_attempts_user (user_id),
    CONSTRAINT fk_login_attempts_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE SET NULL
);

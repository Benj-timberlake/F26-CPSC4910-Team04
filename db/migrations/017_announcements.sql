-- run once against Team04_DB before deploying this branch

-- sponsor announcements. they stay when the author's account is deleted
CREATE TABLE IF NOT EXISTS announcements (
    id INT NOT NULL AUTO_INCREMENT,
    company_id INT NOT NULL,
    author_id INT NULL,
    title VARCHAR(100) NOT NULL,
    body VARCHAR(2000) NOT NULL,
    created_at DATETIME NOT NULL,
    PRIMARY KEY (id),
    KEY ix_announcements_company (company_id, created_at),
    CONSTRAINT fk_announcements_company FOREIGN KEY (company_id) REFERENCES companies (id) ON DELETE CASCADE,
    CONSTRAINT fk_announcements_author FOREIGN KEY (author_id) REFERENCES users (id) ON DELETE SET NULL
);

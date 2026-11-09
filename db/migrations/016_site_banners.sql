-- run once against Team04_DB before deploying this branch

-- the site-wide banner. the active one is the row with no cleared_at
CREATE TABLE IF NOT EXISTS site_banners (
    id INT NOT NULL AUTO_INCREMENT,
    message VARCHAR(500) NOT NULL,
    level VARCHAR(10) NOT NULL,
    created_at DATETIME NOT NULL,
    set_by INT NULL,
    cleared_at DATETIME NULL,
    PRIMARY KEY (id),
    KEY ix_site_banners_cleared (cleared_at, created_at),
    CONSTRAINT fk_site_banners_user FOREIGN KEY (set_by) REFERENCES users (id) ON DELETE SET NULL
);

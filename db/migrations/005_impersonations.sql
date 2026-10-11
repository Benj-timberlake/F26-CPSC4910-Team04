-- run once against Team04_DB before deploying this branch

-- each time an admin or sponsor views the site as another user
CREATE TABLE IF NOT EXISTS impersonations (
    id INT NOT NULL AUTO_INCREMENT,
    viewer_id INT NOT NULL,
    user_id INT NOT NULL,
    started_at DATETIME NOT NULL,
    ip_address VARCHAR(45) NULL,
    PRIMARY KEY (id),
    KEY ix_impersonations_user (user_id),
    CONSTRAINT fk_impersonations_viewer FOREIGN KEY (viewer_id) REFERENCES users (id) ON DELETE CASCADE,
    CONSTRAINT fk_impersonations_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

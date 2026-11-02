-- run once against Team04_DB before deploying this branch

-- which admin sent a reset link
ALTER TABLE password_changes
    ADD COLUMN requested_by INT NULL,
    ADD CONSTRAINT fk_password_changes_requested_by FOREIGN KEY (requested_by) REFERENCES users (id) ON DELETE SET NULL;

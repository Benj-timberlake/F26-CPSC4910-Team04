-- run once against Team04_DB before deploying this branch

-- points reasons a sponsor company reuses
CREATE TABLE IF NOT EXISTS saved_reasons (
    id INT NOT NULL AUTO_INCREMENT,
    company_id INT NOT NULL,
    reason VARCHAR(500) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_saved_reasons_company_reason (company_id, reason),
    CONSTRAINT fk_saved_reasons_company FOREIGN KEY (company_id) REFERENCES companies (id) ON DELETE CASCADE
);

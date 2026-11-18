-- accounts_companies.account_id must reference users.id, not accounts_history.id
ALTER TABLE accounts_companies
    DROP FOREIGN KEY accounts_companies_ibfk_1,
    ADD CONSTRAINT accounts_companies_ibfk_1
        FOREIGN KEY (account_id) REFERENCES users (id);

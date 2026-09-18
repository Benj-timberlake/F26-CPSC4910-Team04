# Database

The app uses the class RDS MySQL instance everywhere, including local development. It is shared
between teams, one database each: ours is `Team04_DB`. Never create databases on it. The
connection string is never checked in:

- locally, put it in the BackEnd project's user secrets (stored under `~/.microsoft/usersecrets`,
  outside the repo):

  ```
  cd TruckerReward/BackEnd
  dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
    "server=HOST;port=3306;database=Team04_DB;user=USER;password=PASSWORD"
  ```

- on Elastic Beanstalk it is the `ConnectionStrings__DefaultConnection` environment property.

Your IP has to be allowed by the RDS security group (port 3306) before the backend can connect.
The backend's `/health` endpoint runs a real query, so it shows straight away whether the
database is reachable.

## Schema changes

`schema.sql` is the full schema for a fresh database. When a table changes, also add a numbered
file under `migrations/` with the `ALTER`/`CREATE` for databases that already exist, and run it
against RDS:

```
mysql -h HOST -u USER -p Team04_DB < db/migrations/002_password_changes_and_resets.sql
```

## RDS settings worth checking

- a normal provisioned instance class (db.t3.micro / db.t4g.micro is fine), not Aurora
  Serverless with auto-pause or a 0 ACU minimum, otherwise the first request after idle hangs
- gp3 storage
- automated backups on

## Local MySQL without RDS

If you can't reach RDS (no network, security group not updated yet), `docker compose up -d`
from the repo root starts a MySQL 8.4 with `schema.sql` applied, and

```
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "server=localhost;port=3306;database=truckerreward;user=root;password=root"
```

points the backend at it. Swap the secret back when you're done. `docker compose down -v`
wipes it.

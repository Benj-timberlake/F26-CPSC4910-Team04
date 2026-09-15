# Database

Local dev uses MySQL in Docker. From the repo root:

```
docker compose up -d
```

`schema.sql` runs automatically the first time the container starts. The backend's
`appsettings.Development.json` already points at it (root/root on localhost:3306).

To wipe and start over:

```
docker compose down -v
```

## RDS

The same `schema.sql` needs to be run against the RDS instance. For prod the connection
string goes in the `ConnectionStrings__DefaultConnection` env var on Elastic Beanstalk,
not in appsettings.

Make sure the RDS instance is not something that pauses or goes cold:
- use a normal provisioned instance class (db.t3.micro/db.t4g.micro is fine), not
  Aurora Serverless with auto-pause / min ACU of 0
- storage type gp3, not magnetic
- check the backend's `/health` endpoint after deploy, it does a real query so it will
  show right away if the db is asleep or unreachable

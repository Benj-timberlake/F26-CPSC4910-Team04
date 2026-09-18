# TruckerReward

CPSC 4910 Team 04. Sponsors award points to truck drivers for good driving; drivers spend them in
the sponsor's catalog.

- `TruckerReward/BackEnd` — ASP.NET Core 10 minimal API, EF Core, MySQL
- `TruckerReward/FrontEnd` — Blazor Server
- `TruckerReward/Tests` — xUnit, bUnit
- `db/` — `schema.sql` for a fresh database, `migrations/` for one that already exists
- `infra/` — CloudFront setup script

## Local setup

The database is the class RDS instance, database `Team04_DB`. Put the connection string in the
BackEnd user secrets once (your IP has to be allowed on port 3306):

```
cd TruckerReward/BackEnd
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "server=HOST;port=3306;database=Team04_DB;user=USER;password=PASSWORD"
```

Then `make` builds, starts both apps (BackEnd 8080, FrontEnd 8081) and opens the login page.
`make test`, `make stop`, `make logs`. VS Code's Run All does the same as `make`.

Admins are made by setting `user_type = 'admin'` on the user row. Emails (reset links, security
notices) print to the backend log unless `EMAIL_FROM` is set.

Optional user secrets, never appsettings:

| Key | Project | Effect |
|---|---|---|
| `Authentication:Google:ClientId` / `ClientSecret` | FrontEnd | Google sign-in button; redirect URI `/signin-google` |
| `Authentication:Microsoft:ClientId` / `ClientSecret` | FrontEnd | Microsoft sign-in button; redirect URI `/signin-microsoft` |
| `BACKEND_API_KEY` | both | backend rejects requests without a matching `X-Api-Key` |

## Schema changes

Edit `db/schema.sql` and add a numbered file under `db/migrations/` with the `ALTER`/`CREATE`
for the existing database, then run it against RDS: `mysql -h HOST -u USER -p Team04_DB < db/migrations/NNN_name.sql`.

## Deployment

Every push to `main` builds both apps into one bundle and deploys it to Elastic Beanstalk
(`.github/workflows/deploy.yml`). The FrontEnd listens on 5000, the BackEnd on 5100 loopback only.

Environment properties on the Beanstalk environment:

| Name | Value |
|---|---|
| `ConnectionStrings__DefaultConnection` | same as local, database `Team04_DB` |
| `BACKEND_API_KEY` | one random string; the BackEnd refuses requests without it |
| `FRONTEND_URL` | the public https address; reset links are built from it |
| `EMAIL_FROM` | an SES-verified sender; the instance role needs `ses:SendEmail` |
| `Authentication__Google__*`, `Authentication__Microsoft__*` | as above, with `__` for `:` |

### HTTPS

Google and Microsoft refuse `http://` redirect URIs outside localhost and ACM won't issue a
certificate for an `elasticbeanstalk.com` name, so TLS comes from CloudFront:

```
infra/cloudfront.sh BEANSTALK-04-env.eba-xxxx.us-east-1.elasticbeanstalk.com
```

It forwards everything uncached (including the Blazor WebSocket) and redirects http to https.
The FrontEnd reads `CloudFront-Forwarded-Proto`, so the auth cookie is `Secure`, the sso redirect
URIs are https and the login log records the browser's IP. Set `FRONTEND_URL` to the CloudFront
address and register `https://<it>/signin-google` and `/signin-microsoft` with the providers.

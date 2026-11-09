# TruckerReward

CPSC 4910 Team 04. Sponsors award points to truck drivers for good driving; drivers spend them in
the sponsor's catalog.

- `TruckerReward/BackEnd`: ASP.NET Core 10 minimal API, EF Core, MySQL
- `TruckerReward/FrontEnd`: Blazor Server
- `TruckerReward/Tests`: xUnit, bUnit
- `db/`: `schema.sql` for a fresh database, `migrations/` for one that already exists
- `infra/`: CloudFront setup script
- `docs/api.md`: every backend endpoint

## How it fits together

The browser only talks to the FrontEnd. The FrontEnd signs users in with a cookie and calls the
BackEnd over HTTP with the `X-Api-Key` header, putting the signed-in user's id in the path
(`/users/{id}/...`). The BackEnd checks that user's role (`driver`, `sponsor`, `admin`) and
company on every call. Endpoints live in `BackEnd/src/endpoints`, one file per area, and are
registered in `BackEnd/src/Api.cs`, which the tests use too.

Pages that call the BackEnd inherit `Shared/BackendPage.cs` for the signed-in id, busy and error
state, and show messages with `<Alerts />`. File downloads the browser needs (audit log, points
history, your data) go through small FrontEnd endpoints in `FrontEnd/Auth` so the API key never
reaches the browser. PDFs are built in the browser by `wwwroot/js/reports.js` (jsPDF, vendored in
`wwwroot/lib/jspdf`); call it through `Shared/PdfReport.cs`.

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
notices) print to the backend log unless `SMTP_HOST` or `EMAIL_FROM` is set.

Emails to a user go through `Notifications` (`BackEnd/src/Notifications.cs`), which also saves a copy
in `notifications_history`. A new notification is one more method there; inject `Notifications`
into the endpoint and call it.

Optional user secrets, never appsettings:

| Key | Project | Effect |
|---|---|---|
| `Authentication:Google:ClientId` / `ClientSecret` | FrontEnd | Google sign-in button; redirect URI `/signin-google` |
| `Authentication:Microsoft:ClientId` / `ClientSecret` | FrontEnd | Microsoft sign-in button; redirect URI `/signin-microsoft` |
| `BACKEND_API_KEY` | both | backend rejects requests without a matching `X-Api-Key` |
| `Ebay:ClientId` / `ClientSecret` / `Environment` | BackEnd | eBay product search for the catalog |

## Schema changes

Edit `db/schema.sql` and add a numbered file under `db/migrations/` with the `ALTER`/`CREATE`
for the existing database, then run it against RDS before the code that needs it is deployed:
`mysql -h HOST -u USER -p Team04_DB < db/migrations/NNN_name.sql`. Add the entity to
`AppDbContext` too; `NewTableMigrationTests` checks a new table's migration matches the model.

Any table that points at `users` needs `ON DELETE CASCADE` (rows that belong to the user) or
`ON DELETE SET NULL` (rows that outlive them), or deleting an account will fail. The database also
has triggers that copy every insert and update on `users` into `accounts_history`.

## Tests

`make test`, or `dotnet test TruckerReward/Tests`. The tests run the BackEnd on in-memory SQLite
built from the EF model, so they need no database, and pages are tested with bUnit against a stub
BackEnd (`JsonBackend`). SQLite doesn't have the MySQL triggers, so check anything that touches
`users` history against a real database.

## Deployment

Every push to `main` builds both apps into one bundle and deploys it to Elastic Beanstalk
(`.github/workflows/deploy.yml`). The FrontEnd listens on 5000, the BackEnd on 5100 loopback only.
Pull requests only build (`build.yml`).

After deploying, the workflow checks `https://team04.cpsc4911.com/health` (the FrontEnd's, which
asks the BackEnd, which asks the database). If any step fails, the run's summary names the failed
step, a hint for it, the latest Beanstalk events and the environment health.

The BackEnd checks the database every minute and keeps 90 days of results in `uptime_checks`.
Admins see uptime for the last day, week and month on the security page; the target is 95%.

Environment properties on the Beanstalk environment:

| Name | Value |
|---|---|
| `ConnectionStrings__DefaultConnection` | same as local, database `Team04_DB` |
| `BACKEND_API_KEY` | one random string; the BackEnd refuses requests without it |
| `FRONTEND_URL` | the public https address; reset links are built from it |
| `SMTP_HOST`, `SMTP_PORT` | `smtp.gmail.com`, `587`; mail goes out through Gmail when set |
| `SMTP_USER`, `SMTP_PASSWORD` | `TruckerRewards@gmail.com` and its Google app password |
| `EMAIL_FROM` | the sender address; without `SMTP_HOST` it means SES instead (verified sender, instance role needs `ses:SendEmail`) |
| `Authentication__Google__*`, `Authentication__Microsoft__*` | as above, with `__` for `:` |
| `Ebay__ClientId`, `Ebay__ClientSecret`, `Ebay__Environment` | eBay catalog search |
| `CDL_S3_BUCKET`, `CDL_S3_REGION` | private bucket for uploaded CDL photos (`team04-truckerreward`) |

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

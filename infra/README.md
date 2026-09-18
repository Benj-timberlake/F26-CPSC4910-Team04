# Infrastructure

Everything runs in AWS us-east-1:

| Piece | What | Where it's set up |
|---|---|---|
| Elastic Beanstalk `BEANSTALK-04-env` | FrontEnd (port 5000, public) and BackEnd (port 5100, loopback only) from one bundle | `.github/workflows/deploy.yml` on every push to `main` |
| RDS MySQL (class instance, database `Team04_DB`) | the only database, local dev included | console; schema from `db/schema.sql` + `db/migrations/` |
| CloudFront | HTTPS in front of the Beanstalk URL | `infra/cloudfront.sh`, once |
| SES | reset links and security emails | console: verify the `EMAIL_FROM` address, leave the sandbox |

## Environment properties on Elastic Beanstalk

Set these in the environment's configuration, never in the repo:

| Name | Used by | Value |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | BackEnd | `server=...;port=3306;database=Team04_DB;user=...;password=...` |
| `BACKEND_API_KEY` | both | one long random string, the same on both |
| `FRONTEND_URL` | BackEnd | the CloudFront address, e.g. `https://d1234.cloudfront.net` — reset links are built from it |
| `EMAIL_FROM` | BackEnd | the SES-verified sender; unset means emails only go to the log |
| `Authentication__Google__ClientId` / `ClientSecret` | FrontEnd | from Google Cloud console |
| `Authentication__Microsoft__ClientId` / `ClientSecret` | FrontEnd | from the Entra app registration |

The instance role needs `ses:SendEmail` for SES to work.

## HTTPS

Google and Microsoft refuse `http://` redirect URIs (localhost excepted) and ACM won't issue a
certificate for an `elasticbeanstalk.com` name, so the site gets TLS from CloudFront:

```
infra/cloudfront.sh BEANSTALK-04-env.eba-xxxx.us-east-1.elasticbeanstalk.com
```

The distribution forwards everything (no caching, all headers, cookies and query strings,
WebSockets for the Blazor circuit) and redirects plain http viewers to https. The FrontEnd reads
`CloudFront-Forwarded-Proto` so it knows the browser used https: that makes the auth cookie
`Secure`, builds https redirect URIs for the sso providers, and puts the real client IP in the
login attempt log. Then:

1. set `FRONTEND_URL` to the CloudFront address
2. register `https://<cloudfront address>/signin-google` and `/signin-microsoft` with the providers
3. use the CloudFront address, not the Beanstalk one, as the public URL

Password login works on the plain Beanstalk URL too; only sso needs https.

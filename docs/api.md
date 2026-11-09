# Backend API

The BackEnd only answers the FrontEnd. Every request except `/` and `/health` needs the
`X-Api-Key` header when `BACKEND_API_KEY` is set, and on Beanstalk it listens on loopback only.
The FrontEnd knows who is signed in from its auth cookie and puts that user's id in the path, so
`/users/{id}/...` means "as user `id`". The BackEnd checks that user's role and company itself.

Times are UTC. Errors come back as `{ "message": "..." }` with 400, 403, 404 or 409.

## Health and site

| Method | Path | What it does |
|---|---|---|
| GET | `/` | API name |
| GET | `/health` | 200 when the database answers, 503 otherwise |
| GET | `/site-banner` | the active site banner, 204 when there is none |
| GET | `/api/about` | about page content |

The FrontEnd has its own `/health` that calls this one, which is what the deploy checks.

## Sign in and accounts

| Method | Path | What it does |
|---|---|---|
| GET | `/auth/password-policy` | the password rules shown on the forms |
| POST | `/auth/register` | create an account and email a confirmation link |
| POST | `/auth/login` | check a username and password, counts toward the lockout |
| POST | `/auth/login/code` | finish a sign in with the emailed one-time code |
| POST | `/auth/external` | sign in or sign up with Google or Microsoft |
| POST | `/auth/passkey/begin`, `/auth/passkey/complete` | passkey sign in |
| POST | `/auth/forgot` | email a reset link (same answer for unknown emails) |
| POST | `/auth/reset` | set a new password with a reset link |
| POST | `/auth/confirm-email` | confirm an email address with its link |
| GET | `/users/{id}` | profile |
| GET | `/users/{id}/status` | active or inactive, used when the cookie is revalidated |
| GET | `/users/{id}/security` | recent sign ins, failures since the last one and when the password last changed |
| POST | `/users/{id}/username` | change username (needs the current password) |
| POST | `/users/{id}/email` | change email and send a new confirmation link |
| POST | `/users/{id}/email/confirmation` | resend the confirmation link |
| PUT | `/users/{id}/profile` | change name, phone and address |
| POST | `/users/{id}/password` | change or set a password, signs out other browsers |
| PUT | `/users/{id}/sign-in-codes` | turn emailed sign in codes on or off |
| GET | `/users/{id}/passkeys` | list passkeys |
| POST | `/users/{id}/passkeys/begin`, `/complete` | add a passkey |
| DELETE | `/users/{id}/passkeys/{passkeyId}` | remove a passkey |
| POST | `/users/{id}/sessions` | record a new signed-in browser |
| POST | `/sessions/check`, `/sessions/end` | check or end a browser's session |
| GET | `/users/{id}/sessions` | signed-in devices |
| DELETE | `/users/{id}/sessions/{sessionId}` | sign one device out |
| POST | `/users/{id}/sessions/revoke` | sign out everywhere |
| POST | `/users/{viewerId}/view-as/{userId}` | admin or sponsor views the site as that user |
| GET | `/users/{id}/export` | everything stored about the user, as JSON (no hashes or keys) |
| DELETE | `/users/{id}/account` | delete the account, or anonymize it when sponsors need its history |

## Notifications

| Method | Path | What it does |
|---|---|---|
| GET | `/users/{id}/notifications` | notification history |
| GET | `/users/{id}/notifications/unread` | unread count for the bell |
| POST | `/users/{id}/notifications/read` | mark all read |
| GET | `/users/{id}/notification-settings` | which categories are on and emailed |
| PUT | `/users/{id}/notification-settings` | save them |
| GET | `/users/{id}/announcements` | announcements from the driver's sponsors, or the sponsor's own |
| POST | `/users/{id}/announcements` | sponsor posts one and notifies its drivers |

## Points

| Method | Path | What it does |
|---|---|---|
| GET | `/users/{id}/points` | balance and history for a driver |
| GET | `/users/{id}/points.csv` | the same history as a CSV file |
| GET, POST | `/users/{id}/points/monthly` | the sponsor's monthly budget |
| GET | `/users/{id}/points/drivers` | the sponsor's drivers with totals (search, sort, date range) |
| POST | `/users/{id}/points/drivers/{driverId}` | add or take away points with a reason |
| POST | `/users/{id}/points/drivers/{driverId}/remove` | remove a driver from the sponsor |
| POST | `/users/{id}/points/bulk` | award several drivers at once |
| GET, POST | `/users/{id}/points/reasons` | saved reasons |
| DELETE | `/users/{id}/points/reasons/{reasonId}` | forget a saved reason |
| GET | `/users/{id}/leaderboard` | ranking by points, spending or orders for a period |
| PUT | `/users/{id}/leaderboard/hidden` | driver hides their name from other drivers |

## Achievements

| Method | Path | What it does |
|---|---|---|
| GET | `/users/{id}/achievements` | earned badges (awards any newly reached ones first) |
| GET | `/users/{id}/achievements/progress` | unearned goals, closest first |
| GET, POST | `/users/{id}/achievements/goals` | the sponsor's own goals |
| PUT | `/users/{id}/achievements/goals/{goalId}` | edit or turn off a goal |

## Catalog and orders

| Method | Path | What it does |
|---|---|---|
| GET | `/api/products`, `/api/products/detail` | eBay product search and detail |
| GET, POST, DELETE | `/users/{id}/catalog` | the sponsor's catalog items, or what a driver can browse |
| GET, PUT | `/users/{id}/wishlist` | driver wishlist |
| GET, POST | `/users/{id}/cart` | cart |
| PUT | `/users/{id}/cart/{itemId}/quantity` | change quantity |
| DELETE | `/users/{id}/cart/{itemId}` | remove from cart |
| POST | `/users/{id}/cart/send-order` | place the order |
| GET | `/users/{id}/orders` | past orders |
| POST | `/users/{id}/orders/{historyId}/refund`, `/edit` | refund an order, or edit one that's still pending |
| GET | `/users/{id}/driver-orders` | orders waiting on the sponsor |
| POST | `/users/{id}/driver-orders/{orderId}/buy`, `/reject` | approve or reject one |

## Sponsors and applications

| Method | Path | What it does |
|---|---|---|
| GET | `/companies` | sponsor companies |
| POST | `/users/{id}/companies/{companyId}/switch` | driver switches the active sponsor |
| POST | `/users/{id}/applications`, `/applications/cancel` | apply to a sponsor, or cancel with a reason |
| GET | `/users/{reviewerId}/applications/{applicantType}` | applications to review |
| POST | `/users/{reviewerId}/applications/{applicationId}/review` | accept or reject |
| GET | `/users/{id}/drivers/{driverId}/view` | a sponsor's view of one driver |
| GET, POST | `/users/{id}/sponsor-team` | the sponsor's team members |
| POST | `/users/{id}/mass-accounts` | bulk account upload from a pipe-delimited file |

## Admin

Admin endpoints take the acting admin's id (`adminId`) and refuse anyone else.

| Method | Path | What it does |
|---|---|---|
| GET | `/admin/accounts`, `/admin/accounts/counts` | accounts with filters, and counts by role |
| POST | `/admin/accounts` | create an account |
| PUT | `/admin/accounts/{id}` | edit one |
| POST | `/admin/accounts/{id}/status` | deactivate or reactivate |
| DELETE | `/admin/accounts/{id}` | delete an account with no history |
| POST | `/admin/accounts/{id}/reset-link` | email the user a reset link |
| GET | `/admin/locked-accounts` | accounts locked by failed sign ins |
| POST | `/admin/locked-accounts/{username}/unlock` | unlock early |
| GET | `/admin/login-attempts` | sign in log |
| GET | `/admin/password-changes` | password change log |
| GET | `/admin/uptime` | uptime for 24h, 7d and 30d and recent failed checks |
| PUT | `/admin/site-banner` | set the site banner |
| POST | `/admin/site-banner/clear` | clear it |
| GET | `/users/{id}/audit`, `/users/{id}/audit.csv` | audit log for sponsors and admins, as JSON or CSV |

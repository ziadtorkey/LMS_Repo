# Runbook: Zoom live sessions

How the platform uses Zoom (M4b, D49, D53, D54, D55) and what an operator has to set up. Secrets live only in server configuration or the secret store, never in the database, the repository, logs or responses.

## 1. The Zoom app

Create **one** Server-to-Server OAuth app in the platform's Zoom account (not one per client).

- Scopes: create, update and delete meetings (`meeting:write:admin`, `meeting:update:admin`, `meeting:delete:admin`), read users (`user:read:admin`), read meeting reports and participants (`report:read:admin`), read cloud recordings (`recording:read:admin`).
- Event subscriptions (**Webhook only events**): `meeting.started`, `meeting.ended`, `recording.completed`. Endpoint URL: `https://<api host>/api/integrations/zoom/webhook`. Zoom validates it with an `endpoint.url_validation` challenge; the platform answers it once the secret token below is configured.
- Note the account id, client id, client secret and the webhook **secret token**.

## 2. Configuration

| Key | Meaning |
|---|---|
| `Zoom:AccountId`, `Zoom:ClientId`, `Zoom:ClientSecret` | the app credentials |
| `Zoom:WebhookSecretToken` | verifies every webhook signature; without it nothing verifies (401) |
| `Zoom:HostPool` | the licensed Zoom users that host the meetings, for example `Zoom:HostPool:0=host1@example.com`. The licence count is simply the size of the pool |
| `Zoom:HostBufferMinutes` | gap kept before the start and after the end of a session when a host is allocated (default 10) |
| `Zoom:Timeout` | longest a Zoom call may take (default 15 s) |
| `Live:MaxHostSharePercent` | the share of the pool one client may hold in any overlapping slot (default 25, minimum one host) |

Per-client settings (unit settings, admin screen): `Live.CloudRecordingEnabled` (default off, D55), `Live.JoinWindowMinutes`, `Live.ReminderMinutes`, `Live.MaxCreatesPerDay` (counts Zoom meetings created per client per day).

Pool users must exist in the Zoom account with a **licensed** seat and be active. Turning `Features.VirtualClasses` on is refused with `409 provider_not_configured` while Zoom is unconfigured, does not accept the credentials, or any pool user is missing, unlicensed or inactive.

## 3. Required Zoom account settings

Account-level settings can override what the platform sets per meeting. Set them as below and **lock** them where Zoom allows:

- Waiting room: **on** (the platform also requests it per meeting).
- Join before host: **off**.
- Automatic recording: **off** at the account level. The platform requests cloud recording per meeting only for units that enabled `Live.CloudRecordingEnabled`; an account-level "record automatically" would record every meeting regardless.
- Cloud recording retention and sharing follow the school's data protection policy (recordings can hold minors).
- Participants' authentication: allow guests (students may join with a name only); only a participant signed in to a Zoom account is ever matched to a student, by the verified account email.

## 4. How it runs

- **Create / update / delete**: the API calls Zoom after its own transaction; a failed call leaves the session `SyncFailed` and the `zoom-sync-sweep` job (every 5 minutes, per client, under a named lock) retries it under the same pool host.
- **Events**: the webhook updates the status (`Live`, `Ended`), imports the participants of an ended session and attaches a cloud recording link when the unit enabled it. Every event is recorded in `ZoomWebhookReceipts` (key = SHA-256 of the body, no client data) in the same transaction as its effect; a re-delivery is a no-op, and a failure answers 500 so Zoom retries.
- **Polling fallback**: the `zoom-polling` job (every 15 minutes) ends sessions whose end passed 10 minutes ago without an event (looking back 48 hours) and fetches recordings for ended sessions of units with cloud recording on. It is idempotent with the webhook: whichever comes first wins and the attendance seam is called once.
- **Retention**: `zoom-receipt-retention` (daily 03:30) deletes receipts older than 30 days.
- The Hangfire dashboard (Development only) can run any of these jobs on demand; in production they are scheduled when the API starts with `Jobs:ServerEnabled=true`.

## 5. Webhook protection

Order of checks: an address with too many failed verifications in a minute gets 429 without touching the database; body over 256 KB is 413; signature over the raw bytes in constant time, then a five-minute window (401 with no hint, and the failure counts against the address); then a high global ceiling for verified traffic (valid bursts and Zoom retries are never limited per address). Run the API behind a proxy that forwards the real client address (`ForwardedHeaders`), or the per-address limit sees only the proxy.

If Zoom ever re-sends an event with its original timestamp (not a fresh one), the five-minute window refuses the retry: capture one such retry, and if confirmed widen the window to Zoom's retry schedule (receipts already deduplicate).

## 6. Troubleshooting

| Symptom | Check |
|---|---|
| Create answers `409 provider_not_configured` | the `Zoom:*` keys; `GET /api/live-sessions/provider-status` shows `{configured, healthy}` only |
| `409 provider_unavailable` with `Retry-After` | the pool is full for that slot, the client holds its share, the daily cap is reached, or Zoom is rate limiting |
| Sessions stay `SyncFailed` | provider status; the sweep log lines (never a response body); Zoom app credentials |
| No `Ended` events | the subscription and secret token; the polling job will end them within about 25 minutes |
| Webhook 401 | wrong `Zoom:WebhookSecretToken`, or clock skew above five minutes on the API host |

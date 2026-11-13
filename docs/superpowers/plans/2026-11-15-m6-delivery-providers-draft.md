# M6 delivery providers: draft for the M6 backend proposal

> **Status: DRAFT section** by BE2 for BE1's M6 (Communication) backend proposal. It covers FR-NOT-03 and X-07 only: how a notification reaches email, SMS and push reliably. Messaging, targeted announcements and moderation (FR-MSG-*) are BE1's. Written against `main` at the time of writing; it reuses designs from M4a (scheduled jobs, #434/#603), M4b (Zoom provider and webhook, #499/#511). Decisions needing the owner are in section 10.

## 1. Starting point

- `INotificationPublisher.PublishAsync` writes in-app `Notification` rows through its own DbContext and pushes them over SignalR (`INotificationPusher`). Channel preferences (`NotificationPreferenceDto(EventKey, Channel, Enabled, Locked)`) are already stored for Email, Sms and Push; only in-app is delivered.
- `IEmailSender` (SMTP, MailKit) sends one email synchronously; nothing queues or retries it.
- `IBackgroundJobs` (Hangfire on MySQL) gives enqueue, idempotent scheduled keys, recurring jobs with system context. `HangfireSet.Score` is a DOUBLE since #603, so scheduled times are exact.

Goal: every notification whose recipient enabled a channel is **recorded in the same transaction as the notification**, delivered **at least once** by a worker, retried on transient failure, never retried on a permanent one, rate limited, auditable, and its outcome (delivered, bounced, complained) comes back through a webhook.

## 2. Provider abstractions (Domain contracts, Infrastructure implementations)

One small interface per channel, so a provider can be swapped without touching the pipeline. Messages carry only what the provider needs; addresses are resolved at send time and are never copied into the outbox.

```csharp
public interface IEmailProvider { string Name { get; } Task<DeliveryResult> SendAsync(EmailDelivery m, CancellationToken ct); }
public interface ISmsProvider   { string Name { get; } Task<DeliveryResult> SendAsync(SmsDelivery m, CancellationToken ct); }
public interface IPushProvider  { string Name { get; } Task<DeliveryResult> SendAsync(PushDelivery m, CancellationToken ct); }

record EmailDelivery(string IdempotencyKey, string To, string Subject, string Html, string Text, IReadOnlyDictionary<string,string> Headers);
record SmsDelivery(string IdempotencyKey, string ToE164, string Text);
record PushDelivery(string IdempotencyKey, string Endpoint, string P256dh, string Auth, byte[] Payload, TimeSpan Ttl, string? Topic);

abstract record DeliveryResult
{
    record Accepted(string ProviderMessageId);
    record Transient(string Code, TimeSpan? RetryAfter);   // 429, 5xx, timeout: retry with backoff
    record Permanent(PermanentReason Reason);               // InvalidRecipient, Rejected, ContentTooLong, SubscriptionGone
    record NotConfigured();                                  // like live sessions: the row waits, an admin sees it
}
```

- Providers **never throw** for a provider answer; a timeout is `Transient` (the Zoom client lesson: a hung call must become a typed failure, `Zoom:Timeout`-style option per provider).
- Errors carry a **code only**, never the response body, address, token or credential (the Zoom exception rule).
- **Fakes** (`FakeEmailProvider`, `FakeSmsProvider`, `FakePushProvider`) in Infrastructure, registered in Development and in the test host: record every delivery, programmable outcomes (`FailNext(n, result)`), optional per-call delay for concurrency tests. A `NotConfigured*Provider` is the default when nothing is configured, so nothing leaves the system by accident. Selection follows `AddZoom`: configured provider, else fake in Development, else not configured.
- Email v1: the existing SMTP sender becomes `SmtpEmailProvider` (MailKit). SMS v1: one provider behind `ISmsProvider` (owner choice, O2). Push: `WebPushProvider` (VAPID, RFC 8291 payload encryption); a `404`/`410` from the push service is `Permanent(SubscriptionGone)` and deletes that subscription.
- Secrets (SMTP/API keys, VAPID private key) only in server configuration or the secret store, never the database, DTOs, logs or audit.

## 3. The outbox on our job runner

**Table `DeliveryOutbox`** (tenant-owned, user-scoped; isolation test required): `Id`, `OrganizationId`, `NotificationId`, `UserId`, `Channel`, `EventKey`, `Locale`, `Status` (`Pending`, `Sending`, `Sent`, `Failed`, `Expired`, `Suppressed`, `NotConfigured`), `Attempts`, `NextAttemptAtUtc`, `LeaseUntilUtc`, `ProviderMessageId`, `LastCode`, `CreatedAtUtc`, `SentAtUtc`, `ExpiresAtUtc`. No address, no body, no parameters: the content is rendered at send time from the notification row, so the outbox holds no personal data.

1. **Enqueue = same transaction.** `NotificationPublisher` inserts the in-app rows and one outbox row per enabled non-in-app channel in one `SaveChanges` (a locked preference counts as enabled). A crash cannot lose a delivery or send one for a notification that was rolled back. After commit it nudges the dispatcher (below).
2. **Dispatcher** (`DeliveryDispatchJob`, system context, per client like the Zoom jobs): claims a batch with a **conditional update** (`Pending` and due -> `Sending`, `LeaseUntilUtc = now + 5 min`, same pattern as the quiz auto-submit and `OpenedNotifiedAtUtc` claims), sends each, then writes the outcome. A worker that dies leaves a lease that expires; a recovery pass returns `Sending` rows past their lease to `Pending` (attempt counted). Claiming is a single `UPDATE` per row, so two workers or two nodes never send the same row.
3. **Triggering.** A recurring job every minute is the safety net (Hangfire's minimum cron resolution); on publish the publisher calls `IBackgroundJobs.ScheduleAsSystem("outbox:{org}:{minute}", now, ...)`: idempotent per key, so a burst of notifications enqueues one run, and the latency is seconds, not a minute. A named lock per client (`IJobKeyLock`, zero wait, as in `ZoomSyncSweepJob`) keeps overlapping runs from working the same client twice.
4. **Delivery is at least once.** A crash between the provider accepting and the outbox write can send twice; `IdempotencyKey = outbox Id` is passed to providers that support it (SES/SendGrid/Twilio accept one), and the user-visible duplicate window is documented, not hidden.
5. **Send-time checks** (in order): row still wanted (notification not deleted, user active, **preference still enabled** unless locked), recipient address exists and is not suppressed (section 6), message not expired, rate limit token available (section 5). A failed check ends the row as `Suppressed`/`Expired` with a code, never silently.
6. **Rendering.** Per-event templates, `ar` and `en` resource entries (rule 4), locale from the user's culture, RTL-correct HTML email, a text part, no secret links, no data beyond what the in-app card shows (a push shows a generic title and the deep link, never grades or names). Parameters are HTML-encoded; subject length capped.

## 4. Retry and backoff

| Rule | Value |
|---|---|
| Transient failure | retry at `now + base * 2^(attempt-1)`, base 30 s, jitter +-20 %, cap 1 h; a provider `Retry-After` wins when longer |
| Max attempts / window | email 8 attempts or 24 h; SMS 4 attempts or 2 h (a late code or reminder is worse than none); push 5 attempts or the event TTL |
| Permanent failure | `Failed`, no retry. `InvalidRecipient` adds the address to the suppression list (section 6); `SubscriptionGone` deletes the push subscription |
| Expiry | each channel and event has a TTL (`ExpiresAtUtc`); an expired row is `Expired`, not sent |
| `NotConfigured` | row stays `NotConfigured` (not counted as an attempt) and is released when the provider is configured; the admin sees the count |
| Dead letters | a `Failed` or exhausted row is kept 30 days for diagnosis, then purged (like Zoom receipts); counts are visible to platform admins |

All timings are options (`Delivery:*`) so tests run with a fake clock (`IClock`) and no real waits.

## 5. Rate limits

- **Per provider and channel (outbound):** a token bucket plus a 429 penalty window and a bounded queue, the same shape as `ZoomOutboundLimiter` (it generalises: a bucket per named class, `Retry-After` honoured, overflow fails fast as `Transient`). Defaults per provider documentation, configurable.
- **Per client:** hourly and daily caps per channel as unit settings (`Comm.Email.MaxPerDay`, `Comm.Sms.MaxPerDay`, ...) so one announcement to a whole school cannot exhaust the platform's budget; over the cap rows wait (`Pending`, `NextAttemptAtUtc` next window) rather than fail, and the admin sees "delayed by your daily limit".
- **Per user:** a flood guard (for example 5 SMS and 20 emails per hour); the in-app notification is unaffected.
- **Fairness:** the claim orders by `NextAttemptAtUtc` and takes a bounded batch **per client** round-robin, so one client's blast never starves others (the same per-client batch as the sweep jobs).
- SMS is cost-bearing: a platform-wide daily ceiling with an alert, and quiet hours (O4).

## 6. Suppression, consent and bounces

- **`DeliverySuppression`** (platform table keyed by a hash of the normalised address plus channel, holding no clear address; plus per-client opt-outs): hard bounces, spam complaints, invalid numbers and unsubscribe clicks. The dispatcher consults it before every send. A complaint or hard bounce suppresses across clients (the reputation is the platform's).
- Non-critical email carries `List-Unsubscribe` (one-click) and a footer link that sets the user's preference for that event to off (locked events have no unsubscribe).
- Soft bounces count against the row's retries, not the suppression list, until three in a row.
- Security-sensitive messages (password reset, lockout, MFA) are always sent regardless of preference and bypass the per-user flood guard, but not suppression.

## 7. Bounce and complaint webhooks (reusing the Zoom webhook design)

`POST /api/integrations/delivery/{provider}/webhook`, anonymous, one route per provider kind. The pipeline is the one from M4b Task 7 (#511), generalised into a shared intake:

1. address over its failed-verification limit -> 429, no database access;
2. body cap (256 KB) -> 413, read with a limit;
3. **provider-specific verifier** behind `IWebhookVerifier` (`Verify(rawBody, headers) -> Valid | BadSignature | Stale`): constant-time HMAC over the raw bytes (Mailgun, SendGrid signed webhooks, Twilio), or the signature-certificate check for SNS-delivered SES events; always the raw bytes, a five-minute window, no hint in the 401, each failure counted per address (`ZoomWebhookRateLimiter` becomes `WebhookRateLimiter`);
4. subscription-confirmation handshakes answered only after verification (the Zoom URL-validation rule);
5. high global ceiling for verified traffic, so a bounce storm or provider retry is never 429;
6. **receipt and effect in one transaction**: dedupe key = the provider event id when it exists, else SHA-256 of the body; a duplicate is a 200 no-op reachable only after verification; a failure rolls both back and answers 5xx so the provider retries.
7. **Effect**: find the outbox row by `ProviderMessageId` (the single cross-client lookup, id and client only, allow-listed `ISOLATION-BYPASS`, like `ZoomEventProcessor.ResolveAsync`), set `Sent`/delivered, bounce (hard/soft), complaint; add suppressions; unknown message id = 200 no-op; a spike of bounces for one client raises an in-app notification to its admins.

`ZoomWebhookReceipt` generalises to `WebhookReceipt(Source, EventKey, ReceivedAtUtc)` (a platform table with no client data, 30-day retention). Recommendation: extract the shared intake after #511 merges; do not couple M6 to Zoom code before then.

SMTP caveat: plain SMTP gives no asynchronous bounce signal. Either the owner picks an API/SMTP-relay provider that posts events (SES, SendGrid, Mailgun), or v1 reads a DSN mailbox (polling, more moving parts). This is O1.

## 8. Web push specifics

- Table `PushSubscription` (tenant-owned, user-scoped, isolation test): `UserId`, `EndpointHash` (unique), endpoint and keys **protected** with Data Protection, `UserAgent` (short), `CreatedAt`, `LastSeenAt`. API: register and delete the caller's own subscription (self-scoped, authentication only, in the probe matrix like `/api/me/...`); the server key is exposed as the public VAPID key only.
- A user with no subscription simply has no push row to send; deleting a subscription removes its pending outbox rows.

## 9. Tasks (proposed split; lanes for the Lead)

1. Contracts: channels, settings keys (`Comm.*` caps, TTLs), problem codes, DTOs (Lead).
2. Provider abstractions, fakes, SMTP provider, not-configured defaults (BE).
3. `DeliveryOutbox`, publisher integration in one transaction, dispatcher job, lease recovery, backoff, expiry (BE). Tests: concurrent claim sends once; crash recovery; backoff table with a fake clock; permanent failure ends; transaction rollback leaves no outbox row; isolation.
4. Rate limiters (provider, client, user) and fairness (BE). Tests: bucket, 429 penalty, daily cap delays not drops, no starvation.
5. Suppression list, unsubscribe, preference checks at send time (BE + FE for the unsubscribe landing page).
6. Webhook intake (verifier abstraction, limiter, receipts) and the first provider's events (BE + QA security review: same checklist as `docs/security/m4b-review.md` section 10).
7. SMS provider and Web Push provider plus subscription API (BE, FE service worker).
8. Admin visibility: outbox counts, oldest pending age, dead letters; delayed-by-limit message (BE + FE).
9. Templates ar/en, RTL email, preview in the notification settings screen (FE + UX).

## 10. Open questions for the owner

- **O1 Email provider and bounce signal:** keep SMTP only (no async bounces), or an API provider that posts events (SES, SendGrid, Mailgun)?
- **O2 SMS provider:** which one, which countries (Saudi Arabia, Egypt, ...), sender id registration, cost ceiling.
- **O3 Data location:** are push, SMS and email providers allowed outside the school's region?
- **O4 Quiet hours:** no SMS and no push between, for example, 22:00 and 07:00 local, queued until morning?
- **O5 Defaults of the per-client caps** and who may raise them.
- **O6 Guardians:** do guardians get their own channel preferences, and is their phone number the SMS target for students under a given age?

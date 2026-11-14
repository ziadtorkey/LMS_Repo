# Runbook: communications (email and Web Push)

Scope: how email and push are delivered, how to run them in development, and what must be true before go-live. Channels in v1 are **in-app, email and Web Push**. **SMS is not in v1** (D76): there is no SMS provider, setting, preference column or route; see "Deferred to v2: SMS" in the M6 plan.

## How delivery works

- A notification is written in the app, and in the same transaction one outbox row per enabled channel (`DeliveryOutbox`). A dispatcher job sends the rows through the channel (`IDeliveryChannel`): email through the relay, push through `PushDeliveryChannel`. A row holds no address and no message text; both are resolved at send time.
- Retries: backoff from 30 s, doubled, with jitter, capped at 1 h; the provider `Retry-After` wins. Email gives up after 8 attempts or 24 h, push after 5 attempts or 4 h. Only an error code is kept on the row.
- Caps and quiet hours are per client (Org Admin, client root): daily email and push caps (platform ceilings 100,000 and 200,000), per-recipient caps, quiet hours for push (off by default). Mandatory events and urgent items ignore quiet hours and caps. The client sees one "cap reached" notice per client day.
- A hard bounce or a spam complaint suppresses the address for that client (a hash is stored, never the address).

## Email

- **Relay choice.** The platform sends through one SMTP relay (D67), chosen per deployment. Configure `Email:SmtpHost`, `Email:SmtpPort` and the credentials as secrets (environment or user-secrets, never a committed file). Development uses Mailpit on `localhost:1025`.
- **SPF, DKIM and DMARC.** On the sending domain: an SPF record that includes the relay, the relay's DKIM selector published as a CNAME or TXT, and a DMARC policy (start with `p=none` and a reporting address, move to `quarantine` once reports are clean). Send a test from the Org Admin page and check the headers show SPF, DKIM and DMARC pass.
- **Bounce events.** The relay posts bounce and complaint events to `POST /api/integrations/email/events` (anonymous, signature and timestamp verified; 401 bad signature, 413 oversize, 429 repeated failures). Each relay has its own parser behind `IEmailEventParser`; add the relay's parser and its webhook secret (a secret, not a file) when the relay is chosen.
- A test send is limited to 3 a day per user (429).

## Web Push

- **VAPID keys.** Generate once per environment: `npx web-push generate-vapid-keys`. Set `Comms:Push:PublicKey`, `Comms:Push:PrivateKey` and `Comms:Push:Subject` (`mailto:` or `https:` contact) as secrets (`Comms__Push__PrivateKey` in the environment, or `dotnet user-secrets` in `src/Lms.Api`). The private key never goes in a committed file; without all three, push stays pending with `not_configured`.
- **Rotation.** A new key pair invalidates every browser subscription (the browser subscribed with the old public key): rotate only with a notice, then users re-enable push. Keep the old private key until the new one is live.
- Subscriptions are only accepted for the push services' hosts (`Comms:Push:AllowedHostSuffixes`, default `fcm.googleapis.com`, `push.services.mozilla.com`, `push.apple.com`, `notify.windows.com`). The payload carries a generic title, a local link and ids only.
- Library: Lib.Net.Http.WebPush (MIT).

## Development and end-to-end

- With `Comms:FakeChannels` true (Development and the test host) email and push go to fake channels that record what they were asked to send and return scripted outcomes. Nothing leaves the process.
- Development-only routes (platform admin; they do not exist elsewhere):
  - `GET /api/dev/comms/outbox`: what the fake channels were asked to send (channel, template or event key, **masked** address) and the latest outbox rows.
  - `DELETE /api/dev/comms/outbox`: clears the recorded sends and scripted outcomes.
  - `POST /api/dev/comms/script` with `{ "channel": "Email", "outcome": "Transient", "errorCode": "smtp_4xx", "retryAfterSeconds": 60, "count": 2 }`: queues the next outcomes (`Accepted`, `Transient`, `Permanent`, `NotConfigured`) of a fake channel.
- The M6 demo seed (Development with a seed password) adds the people of the messaging E2E in the school client: `student1`, `student2` (class 10-A, taught by `teacher`, in groups 1 and 2), `guardian1` (guardian of student 1 only) and `guardian2` (of student 2 only), next to the existing `teacher`, `manager` and `orgadmin`. All use `@altagamo3.test` and the dev seed password.

## Go-live prerequisites

Do not turn on real email or push for a client until all of these are recorded:

1. **D65 scanner.** The attachment scanner of D65 is deployed and its result is enforced for message and announcement attachments.
2. **D67 relay.** The email relay is chosen, SPF, DKIM and DMARC pass on the sending domain, the bounce webhook is configured with its secret and tested with a real bounce.
3. **D71 legal retention.** The owner's answer on legal retention of messages and delivery logs is recorded (the delivery log is kept 90 days by default; message retention follows the answer).
4. **D73 provider data region.** For each provider record the processing region: the email relay and each push service (FCM, Mozilla autopush, Apple, Windows) that the platform sends through. Prefer in-Kingdom or GCC where the provider offers it; where it does not, record the reason and the owner's sign-off. Only message metadata and generic text leave the platform, never message content.
5. **Push consent wording.** The Arabic and English consent text shown before push is enabled is reviewed by the owner, and its version is stored with each consent.
6. **VAPID keys** are set as secrets in the environment and a test push reaches a real device on Android and on an installed iOS web app.
7. **SMS is not in v1** (D76). Do not add an SMS provider; see "Deferred to v2: SMS".

# Lms.E2ETests — Playwright end-to-end tests

Browser tests against the **running** app: real OIDC login (authorization code + PKCE), the dev seed, the Blazor shell in Arabic (RTL, default) and English. xUnit + Microsoft.Playwright (Chromium).

## Run locally

```bash
docker compose up -d --wait mysql minio                # waits until MySQL accepts TCP connections
RateLimiting__Auth__PermitLimit=1000 dotnet run --project src/Lms.Api --launch-profile http
dotnet build tests/Lms.E2ETests
pwsh tests/Lms.E2ETests/bin/Debug/net10.0/playwright.ps1 install chromium   # once per machine
dotnet test tests/Lms.E2ETests
```

The API runs in Development, so it migrates, seeds and serves the app on `:5132`. The fixture waits up to 90 s for `GET /health` to answer `Healthy`, then fails with a hint if the API isn't running.

**Why the rate-limit override:** login and forgot-password POSTs are limited to 5 per minute per IP (X-08, #82). The suite signs in many times from one IP, so the E2E API gets a high limit (the CI `e2e` job sets the same). The limiter itself is covered by integration tests (`PasswordResetTests`).

If port 3306 is taken by a local MySQL service, set `LMS_MYSQL_PORT=3307` for compose and point the API at it with `127.0.0.1` (on Windows, `localhost` may resolve to IPv6 and reach another process):
`ConnectionStrings__Lms="Server=127.0.0.1;Port=3307;Database=lms;User=lms;Password=lms_dev_pw"`.

### Settings (environment variables override the defaults)

| Variable | Default |
|---|---|
| `LMS_E2E_BASEURL` | `http://localhost:5132` |
| `LMS_E2E_ADMIN_EMAIL` | `Seed:PlatformAdminEmail` in `src/Lms.Api/appsettings.Development.json` |
| `LMS_E2E_PASSWORD` | `Seed:DefaultPassword` in the same file |
| `LMS_E2E_MULTI_EMAIL` | `Seed:MultiContextEmail`, else `multi@altagamo3.test` |
| `LMS_E2E_HEADED=1` | show the browser |
| `LMS_E2E_SLOWMO=250` | slow every action down (ms) |

Credentials are never copied into the tests: they come from the dev seed config.

## Conventions
- **Selectors:** `data-testid` only (`login-*`, `app-root`, `lang-switch`, `user-menu`, `context-switcher`, `context-option-<assignmentId>`, `no-contexts`, `nav-toggle`, `nav-<key>`, `forbidden-page`). Ask FE for a test id rather than selecting by CSS or text structure.
- **Expected text** comes from `src/Lms.Client/Resources/SharedResource.{ar,en}.resx` (`Resx.Ar/En`), never hard-coded UI strings. Seeded business data (role labels, unit names) may be literal.
- **Isolation:** every test gets a fresh browser context (empty `localStorage`), so it starts in Arabic with no stored context. Tests share the seeded database and run sequentially (`[Collection("e2e")]`).
- **Never break shared users:** the blocked-login test uses the dedicated `deactivated-e2e`, `expired-e2e` and `withdrawn-e2e` seed users (one per blocked status). The lockout test uses the dedicated `lockout-e2e@altagamo3.test`. That account stays locked for 15 minutes after a run, and longer across local re-runs on the same database volume. That is expected: a locked account shows the same message as an unknown one, so the test still passes. Don't "fix" it by unlocking the user in the seed.
- **Form posts that re-render the same URL** (e.g. a failed login): neither the URL, a load state nor the response event distinguishes the old document from the new one. Mark the current document before clicking and wait for a loaded document without the mark (see `LoginPage.SubmitAsync`).
- **One E2E stack at a time** on a machine (port 5132 is the registered OIDC redirect origin). Take `e2e.lock` in the team's shared notes folder before starting compose and the API, remove it when done, and never stop another session's API (rules: `E2E-RUN-RULES.md` there).
- **Waiting:** Playwright auto-waiting (`Expect(...)`, `WaitForURLAsync`); no fixed sleeps.

## Artifacts
Each test writes `TestResults/e2e/<test>/trace.zip` and `final.png`. Open a trace with
`pwsh tests/Lms.E2ETests/bin/Debug/net10.0/playwright.ps1 show-trace TestResults/e2e/<test>/trace.zip`.
CI uploads `TestResults/` and `api.log` as the `e2e-artifacts` artifact when the job fails.

## Scenarios (M0, plan Task 12)
| Test | Covers |
|---|---|
| `Platform_admin_logs_in_and_sees_clients_menu` | FR-SEC-01, platform-only nav |
| `Multi_context_user_chooses_context_then_sees_arabic_rtl_shell` | FR-SEC-03, X-01 RTL, Arabic labels |
| `Teacher_does_not_see_roles_or_audit_menu` | FR-SEC-04, single context auto-selected |
| `Switching_language_to_english_renders_ltr` | X-01, choice persists across reload |
| `Context_switch_updates_menu` | FR-SEC-03 switch from the header |
| `Stale_stored_context_leads_to_chooser_not_forbidden` | QA follow-up from #28 |
| `Locked_out_after_five_failed_logins_with_same_message_as_unknown_user` | X-08 lockout, no user enumeration |
| `Login_page_is_arabic_rtl_by_default` | X-01 on the server-rendered login page |
| `Shell_is_usable_at_360px_in_arabic` | X-10 (360 px) |

## Scenarios (M1, plan Task 38): `M1/AuditAndNotificationsTests.cs`, `M1/YearsTests.cs`
| Test | Covers |
|---|---|
| `Deactivated_user_sees_blocked_message_on_login` (ar, en × deactivated, expired, withdrawn) | FR-SEC-07: exact blocked-login copy, no session |

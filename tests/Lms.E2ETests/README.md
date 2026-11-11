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

## Scenarios (M1, plan Task 36 — `M1/ClientsAndTreesTests`)
Each runs in Arabic and English (`[Theory]` with `"ar"`/`"en"`; `StartAsync(culture:)` sets the browser language and the app's `lms.culture`). Every test creates its own client with unique names and emails (`M1Names`), so reruns never collide.

| Test | Covers | Needs on main |
|---|---|---|
| `Platform_admin_creates_school_group_and_university_with_first_admins` | FR-ORG-01, both sample client types, invitation sent | #92 |
| `Org_admin_sets_password_from_invitation_email_and_logs_in` | FR-SEC-02 invitation → set password → sign in → client menu | #92 |
| `School_admin_builds_level_types_and_tree` | FR-ORG-02/03: levels, School › Stage › Section tree | #92, #83, #99, #114 |
| `Stages_marked_as_level_transfer_show_the_transfer_path` | FR-ORG-09: transfer flag, next level, RTL arrow | #148, #114 |
| `University_admin_builds_faculty_tree_and_moves_a_specialization` | FR-ORG-03/04: faculty tree, move with confirm | #99, #114 |
| `Delete_unit_with_children_is_refused_with_message` | FR-ORG-05 | #99 |
| `Seeded_org_admin_adds_reorders_and_deletes_a_level_type` | FR-ORG-02 | #83 |

**Mailpit:** invitation and reset emails are read from Mailpit (`MailpitClient`, `LMS_E2E_MAILPIT`, default `http://localhost:8025`). Start it with the stack (`docker compose up -d --wait mysql minio mailpit`; the CI `e2e` job does). Only the link's **path and query** are used: `App:PublicBaseUrl` in Development points at the https profile, while E2E runs on `:5132`.

## Scenarios (M1, plan Task 38): `M1/AuditAndNotificationsTests.cs`, `M1/YearsTests.cs`
| Test | Covers |
|---|---|
| `Deactivated_user_sees_blocked_message_on_login` (ar, en × deactivated, expired, withdrawn) | FR-SEC-07: exact blocked-login copy, no session |

## Scenarios (M1, plan Task 37: `M1/ConfigurationTests`, `M1/AccessAdminTests`)
Run as the seeded school client's Org Admin. Each test puts the shared seeded data back in a `finally` block (policy off, value reset).

| Test | Covers |
|---|---|
| `Root_admin_sets_CreateOrgUnits_on_KG_and_add_is_hidden_below_it` | FR-ORG-06/10: policy on KG hides add on KG and below, not on Primary; inherited switch is disabled below |
| `Locked_root_setting_is_read_only_at_school_level` | FR-ORG-07: a value locked at the root is read-only and shows "Locked by" below |
| `Feature_flag_enabled_at_KG_only_is_off_at_Primary` | FR-ORG-08: flag set at KG is inherited by its sections, default at Primary |
| `Root_admin_creates_Exam_Officer_custom_role_and_renames_Manager_label` | FR-SEC-06, §3.3: custom role with one permission; built-in label renamed and restored |
| `Copy_Primary_branch_under_the_school_with_a_code_suffix` | FR-ORG-11: copy a stage with its sections, codes get the suffix, parent kept |
| save as template and apply with the Year placeholder | FR-ORG-11: waits for #207 |

Assigning a role to a person moved to the M2 staff demo (D31: roles go to existing staff members).

## Scenarios (M2, plan Task 35: `M2/StaffTests`)
Run as the seeded school group's Org Admin with unique people per test (`M2Names`), in Arabic and English. `Infrastructure/JobsClient` runs a job through the Development-only trigger (`POST /api/dev/jobs/{id}/run?asOf=`, BE #201) as a platform admin, in a throw-away browser context.

| Test | Covers | Status |
|---|---|---|
| `School_admin_creates_teacher_who_activates_from_email_and_sees_the_section_context` | FR-STF-01/02, activation email | runs |
| `Duplicate_email_in_a_sibling_stage_is_refused_with_the_field_message` | FR-STF-01, D26 client-wide duplicate | runs |
| `Staff_expiring_soon_notifies_admins_then_expired_staff_is_blocked_after_the_job_run` | FR-STF-06, D35: notice in the bell, then Expired and the login message | runs |
| `Admin_deactivates_teacher_with_reason_teacher_sees_blocked_message_then_reactivated_can_log_in` | FR-STF-03, FR-SEC-07 | runs |
| `Root_admin_shares_teacher_to_a_sibling_stage_and_the_list_shows_them_as_shared` | FR-STF-05, D29 | runs |

## Scenarios (M2, plan Task 38 — `M2/ExternalLoginTests`)
Each runs in Arabic and English. They need the `mock-oidc` compose service (navikt/mock-oauth2-server, interactive login, `http://localhost:8090/client-a`; `LMS_E2E_MOCK_OIDC` overrides the host): `docker compose up -d --wait mysql minio mailpit mock-oidc`.

| Test | Covers |
|---|---|
| `Root_admin_configures_oidc_provider_tests_connection_and_staff_signs_in_with_organization_account` | FR-SEC-08: provider created and tested through the API, staff member found by e-mail domain, first sign-in links the account and lands in their own context |
| `Unknown_identity_sees_not_found_message` | FR-SEC-08: identity nobody owns → generic message in `login-error`, no account, no session |
| `Dual_client_user_signed_in_via_client_A_provider_does_not_see_client_B_context` | Review Focus 2: session bound to the provider's client; the other client's context is refused (403 OutOfScope) and absent from `/api/me` |

| `Blocked_account_signing_in_through_the_provider_sees_the_blocked_message` (× deactivated, expired, withdrawn) | FR-SEC-07: the blocked-status message appears in `login-error` on the external path too, and no session starts |

Notes:
- **Not covered here, on purpose:** a person who belongs *only* to another client (client B) and tries client A's provider is refused with the generic not-found message. That is `ExternalLoginLinkerTests.Email_of_user_without_profile_in_this_client_is_not_found` (no E2E duplicate; the dual-client scenario above covers the session binding).
- **The provider admin page is a separate task (T32).** Until it lands the provider is created and deleted through the API with the signed-in Org Admin's own token (`E2EApi`: token from sessionStorage, context from `/api/me` when the app has not stored one). The "configures in the UI" step joins the first scenario then.
- **`MockOidc.SignInAsync`** fills the mock's sign-in form: the user name becomes `sub`, the claims JSON carries `email`. The issuer is `http://localhost:8090/client-a` for the browser *and* the API (the server builds `iss` from the request host), so never use the container name.
- **Dedicated seed user `dual-e2e`** is a Teacher in the school (KG) and in the university (FCI-1-GEN), used only here.
- The person signs in from the base browser context; the admin works in a second one, so no password session interferes.

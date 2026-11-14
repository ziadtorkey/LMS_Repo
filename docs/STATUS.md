# Project Status

_Updated by the Team Lead after every merge cycle._

**Milestone:** M0 — DONE · M1 Organization & Access — DONE (#73 via #200) · M2 People — closing (#177 via #321) · M3 Academic Structure — in progress
**Last update:** 2026-11-14 03:15 — Nothing merged since 00:07: CI runs were dying from the test memory leak. With the owner's OK, all CI except #622 (test sharding fix) was cancelled so it lands first; approved PRs re-run after. M5 drafts: contracts wave 1 #625 (QA changes requested), wave 2 #629, wave 3 #634; T5 terms #633, T6 calc #631 (approved), T22 grading clients #630, T31 spike #632; QA scaffolds #624, #627, #628. Earlier: CI leak fixes approved (#618 guard, #620 host disposal, #622 test sharding; #622 merges first, then hang-failed PRs re-run). #513 approved, main merged in, re-running CI. M5: plan #621 and security checklist #623 approved; contract wave 1 #625 (BE1 draft), wave 2 (BE4) and wave 3 (BE3) being drafted; design spec #626; probe suite #624. Earlier: Merged since restart: #333 (enrollment exceptions), #603 (Hangfire FLOAT fix), #602 (roles tests), #595 (flaky test), #433 (people restyle), #592 (stepper CSS), #582 (RefRemoved contract), #560 (Stitch foundation), #549 (D57 contract), #522 (T20 probes), #589 (grading), #538 (storage streaming), #525 (plan page), #332 (teacher eligibility), #448 (forums), #518 (IMeetingProvider), #502 (add to lesson), #536 (debounce save fix), #541 (CI skips docs-only pushes), #550 (T21 checklist), #546 (CI pool diagnostics), #543 (player spec), #504 (T13 live client), #493 (T3 corpus), #453 (quiz attempts), #531, #410, #524, #444, #501, #447, #450, #294, #292, #401, #509 (ar number format), #507 (live status chip), #432 (plan authoring), #322 (class students), #309 (guardian My children). Owner decisions: D58–D61 (M5 defaults, Saudi grading and year standards, free-text rooms, excuses and alerts); M5 and M6 planning started (#611 BE proposal); third-party downloads approved (h5p-standalone + 5 real packages; agents await the owner in their own chats); D57 package grading method per module (#549); Stitch redesign replaces the approved design (#553, designer foundation PR in progress). M4b: ADR #528 approved (gates T9a/9b/10/11/17); T3 corpus #493, T4 #513, T8a #523, T8b #534, live FE #504/#515/#510 approved, waiting on CI. CI runner pool saturated: owner OK to cancel draft runs; #541 skips CI for docs-only pushes. Lead merges by hand.

## Agents
| Agent | Model | Session id | Current issue / PR | State |
|---|---|---|---|---|
| Team Lead | Opus | `local_95ef9dc7-9378-4012-a8f2-c7d1f944bdec` (lms-lead) | merges by hand; CI queue triage; M5 contracts review; dashboard | coordinating |
| Backend 1 | Sonnet | `local_19f31b04-ca2f-43d1-a2cb-8a128722a11f` (lms-backend) | #625 QA fix (wave 1); M5 T4 scales and Saudi preset | working |
| Backend 2 | Sonnet | `local_d36e7dc2-e9e6-4f73-b7b3-b078b17bbe41` (lms-backend-2) | #631 follow-ups; M5 T9 published-only reads (on #631) | working |
| Backend 3 | Sonnet | `local_2e8038c6-e5ba-4e13-ade0-9440d9beb565` (lms-backend-3) | M5 T13 periods and time zone (on #629) | working |
| Backend 4 | Sonnet | `local_fd5c4410-8364-4c55-a5a8-3b8aa37afc1f` (lms-backend-4) | M5 T19 excuse workflow (on #634) | working |
| Frontend | Sonnet | `local_40667dce-5be7-478b-95cb-4f14ecd203b9` (lms-frontend) | M5 T23 scales and weight templates admin (on #630) | working |
| Frontend 2 | Sonnet | `local_3d9e8938-4064-4f83-a04d-920704d41a87` (lms-frontend-2) | M5 T33 attendance clients (on #634) | working |
| QA | Sonnet | `local_91a7af3c-7b02-4d61-ac11-2c22629cf89f` (lms-qa) | review #629, #633 | working |
| QA 2 | Sonnet | `local_54d26c64-4403-4a13-b128-5be5e9752d97` (lms-qa-2) | review #634, #632, #630; re-review #613/#615/#626 | working |
| QA 3 | Sonnet | `local_6d46b60b-d70a-4cfa-af2f-5bf90cdf7c08` (lms-qa-3) | review #627, #628; #625 re-review | working |
| UI/UX Designer | Opus | `local_bf63c8eb-13da-4d42-bcc1-11596186a520` (lms-ux) | #626 and #615 QA fixes | working |

## M0 issue map
| Task | Issue | Lane | Depends on |
|---|---|---|---|
| T1 scaffold + CI | #1 | BE1 | — |
| Contract: Permissions/Roles/Policies/ContextHeader | #2 | Lead | #1 |
| Contract: Me DTOs/Routes | #3 | Lead | #1 |
| T2 kernel + isolation | #4 | BE1 | #1 |
| T3 audit | #5 | BE2 | #4 |
| T4 identity/OpenIddict | #6 | BE2 | #4 |
| T5 roles + context | #7 | BE1 | #4, #2 |
| T6 settings/policies | #8 | BE2 | #4 |
| T7 access evaluator | #9 | BE1 | #7, #8 |
| T8 files | #10 | BE2 | #4 |
| T9 Me endpoints | #11 | BE1 | #9, #3 |
| T10 client shell | #12 | FE | #1, #2, #3 |
| T11 context chooser + nav | #13 | FE | #12 |
| T12 dev seed | #14 | BE1 | #7, #11 |
| T12 E2E smoke | #15 | QA | #14, #13 |
| M0 review checklist | #16 | QA | all |
| Bug: OrgAdmin explicit permissions | #21 | Lead | — |
| Audit roles/assignments; unique built-in role; D15 inactive units | #30 | BE1 | #5, #7 |
| RF2 against real PolicyEvaluator | #32 | BE1 | #8, #9 |

## Blockers
- `gh pr ready` is denied by the permission classifier in the FE2 and BE1 sessions (#195, #202); the owner has been asked to allow it there.
- Production prerequisite from #194: the Data Protection certificate (PFX) must be provisioned on every node (`docs/runbook-key-protection.md`).
None. (Board: [LMS v1](https://github.com/users/ziadtorkey/projects/1) — Todo → In Progress → In Review → Done.)

## Decisions needed from the product owner
- 2026-11-11: QA agents run on Sonnet instead of Opus (owner decision); `.claude/agents/qa-engineer.md` and DEV-PLAN updated.
- 2026-11-11: CI was blocked by GitHub Actions billing on the private repo (#152); owner chose to **make the repo public** — done after a secrets scan; CI running again.
- 2026-11-11: M2 plan approved; 13 answers recorded as D23–D36 (students, guardians and change requests move to M3). #103 closed.

## Recently merged
- #276 student profile (closes #246) · #284 enrollment engine (closes #232) · #277 classes (closes #221)
- #305 (closes #72) · #304 · #205 staff import (closes #165) · #300 staff import page (closes #172) · #270 students UI (closes #245) · #267 external-login E2E (closes #178) · #273 guardians (closes #227) · #299 short codes (closes #288) · #298 relationship scopes (closes #220)
- #271 IdP admin UI (closes #173) · #261 M1 config/roles E2E · #296 · #295 · #198 staff lifecycle (closes #163) · #202 import framework (closes #159) · #264 courses UI (closes #237) · #208 (closes #161) · #280 contracts · #281 (closes #70) · #265 academic kernel (closes #213)
- #206 test(e2e): M2 staff lifecycle, sharing and expiry (closes #176) · #278 fix(ci): Playwright install fallback, inotify limits
- #259 M3 contracts (closes #209–#212) · #207 branch templates (closes #52) · #258 copy branch (closes #50) · #196 staff sharing (closes #164) · #268 OIDC SameSite fix (closes #266) · #257 · #187 staff create/list (closes #162) · #262 · #263 · #260 (closes #71) · #204 staff import page (closes #172) · #195 import wizard (closes #169) · #190 (closes #171) · #192 (closes #174) · #182 M2 probes · #193 external sign-in (closes #168) · #148 level transfer (closes #48) · #184 role blocking (closes #160)
- #192 assign roles to existing staff (closes #174) · #201 dev-only job trigger + Jobs:TimeZone (closes #199) · #194 key-ring certificate protection (closes #186) · #197 staff expiry job (closes #166) · #191 person status gate (closes #158) · #121 M1 E2E clients and trees · #189 staff pages (closes #170) · #181 people kernel (closes #157)
- #188 fix(identity): SSRF guard for provider discovery and backchannel (closes #185) · #183 feat(identity): per-client identity provider configuration (closes #167)
- #179 M2 contracts: staff, imports, identity providers (closes #154, #155, #156)
- #180 test(security): dedupe #149/#153 isolation tests · #120 test(e2e): blocked-login E2E ar/en (part of #73) · #145 test(security): one probe-row file per module
- #141 copy branch and template library (closes #63)
- #97 audit viewer, subtree visibility and export (closes #53)
- #99 org units (closes #44) · #129 notification center (closes #69) · #136 assignment Lows · #128 year selector (closes #68) · #135 403 handling (closes #132)
- #114 move unit + unit settings (closes #61) · #92 clients + first Org Admin (closes #42) · #107 role assignments (closes #56) · #116 academic years pages (closes #67)
- #105 notification preferences (closes #51) · #96 roles admin (closes #54) · #124 academic calendar (closes #57) · #130 revoke live pushes (closes #122) · #83 level types (closes #43) · #112 org tree page (closes #60) · #119 M1 probe suite part 1
- #118 fix(notifications): scope live pushes to the active client (closes #93)
- #123 refactor(api): self-registering ILmsModule
- #126 fix(test): allow-list #102 bypasses
- #111 test(arch): IsolationBypass guard (closes #109)
- #102 feat(years): academic years and terms (closes #55)
- #88 feat(auth): forwarded headers (closes #85)
- #100 feat(settings): settings catalog API (closes #49)
- #87/#89/#91 FE roles admin, assignments, audit viewer (closes #64–#66)
- #90 feat(notifications): notification core, in-app center API and SignalR hub (closes #45)
- #110 feat(audit): security audit events fanned out per client (closes #101)
- #104 test(e2e): un-skip stale-context test
- #81 feat(client): clients and level types pages (closes #59)
- #98 feat(contracts): StoredFile in the audit catalog
- #95 fix(ci): MySQL healthcheck over TCP + E2E auth rate limit
- #74 feat(client): API plumbing, problem messages and shared admin components (closes #58, fixes #84)
- #86 feat(access): audit role changes, unique built-in roles, inactive-unit contexts (closes #30)
- #82 feat(auth): forgot/reset password, invitations and blocked-login messages (closes #41)
- #80 feat(access): unit-targeted access checks and org tree service (closes #40)
- #34 test(e2e): Playwright smoke tests (closes #15)
- #77 feat(seed): dev seed with sample clients, roles and users (closes #14)
- #79 feat(contracts): M1 contracts T2–T4 (closes #37, #38, #39)
- #75 feat(contracts): M1 permissions, built-in matrix, problem codes, organization DTOs (closes #36)
- #35 feat(files): MinIO file storage with presigned downloads (closes #10)
- #31 feat(auth): identity with OpenIddict authorization code + PKCE (closes #6)
- #33 test(access): Review Focus 2 against the real PolicyEvaluator (closes #32)
- #29 feat(me): current user contexts and effective permissions (closes #11)
- #27 feat(access): access evaluator and RequirePermission endpoint filter (closes #9)
- #25 feat(config): inheritable settings with locks and policy evaluator (closes #8)
- #28 feat(client): context chooser and permission-driven navigation (closes #13)
- #26 feat(access): roles, assignments, default role provisioner and active context (closes #7)
- #23 feat(audit): capture audit entries for audited entities (closes #5)
- #24 feat(client): MudBlazor shell with OIDC auth and ar/en RTL (closes #12)
- #20 feat(kernel): persistence kernel with client and subtree isolation (closes #4)
- #22 fix(contracts): explicit OrgAdmin permission list and platform-only guard (fixes #21)
- #19 feat(contracts): Me DTOs and API routes (closes #3)
- #18 feat(contracts): permission catalog, built-in roles, policy catalog, context header (closes #2)
- #17 chore: scaffold solution, docker compose and CI (closes #1)

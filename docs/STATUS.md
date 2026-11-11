# Project Status

_Updated by the Team Lead after every merge cycle._

**Milestone:** M0 Foundation — DONE (19 / 19) · M1 Organization & Access — in progress (42 / 49 issues closed) · M2 People — in progress (6 / 27)
**Last update:** 2026-11-11 — Merged #183 (identity provider config, closes #167) and #188 (SSRF fix, closes #185). E2E stack freed. In review: #181 people kernel (approved; re-merging main), #193 external sign-in (QA3), #194 key-ring protection #186 (extra QA), #189/#190/#192 FE1 staff pages, #184 role blocking (changes requested), #121 M1 E2E (QA2). Open: #187 staff create (BE2), #191 person gate (BE4), #195 import wizard (FE2, draft), #182 M2 probes (QA2, draft).

## Agents
| Agent | Model | Session id | Current issue / PR | State |
|---|---|---|---|---|
| Team Lead | Opus | `local_95ef9dc7-9378-4012-a8f2-c7d1f944bdec` (lms-lead) | merges; M2 coordination | coordinating |
| Backend 1 | Sonnet | `local_19f31b04-ca2f-43d1-a2cb-8a128722a11f` (lms-backend) | #148 fix; then M2 #160 (T8), #159 (T7), #161 (T9) | working |
| Backend 2 | Sonnet | `local_d36e7dc2-e9e6-4f73-b7b3-b078b17bbe41` (lms-backend-2) | M2 #162 (T10 staff), then #163, #164, #165 | working |
| Frontend | Sonnet | `local_40667dce-5be7-478b-95cb-4f14ecd203b9` (lms-frontend) | M2 #170 (T24 staff pages), then #171, #174 | working |
| Frontend 2 | Sonnet | `local_3d9e8938-4064-4f83-a04d-920704d41a87` (lms-frontend-2) | E2E #72; then M2 #169 (T23), #172, #173 | working |
| QA | Opus | `local_91a7af3c-7b02-4d61-ac11-2c22629cf89f` (lms-qa) | E2E #121/#72; M2 reviews; then #176, #177 | working |
| QA 2 | Opus | `local_54d26c64-4403-4a13-b128-5be5e9752d97` (lms-qa-2) | #145; #149/#153 dedupe; then M2 #175 (T34 probes) | working |
| Backend 3 | Sonnet | `local_2e8038c6-e5ba-4e13-ade0-9440d9beb565` (lms-backend-3) | M2 #167 (T21 IdP), #168 (T22); M1 #52 after #50 | working |
| Backend 4 | Sonnet | `local_fd5c4410-8364-4c55-a5a8-3b8aa37afc1f` (lms-backend-4) | M2 #157 (T5 people kernel, critical path), #158, #166; M1 #50 | working |
| QA 3 | Opus | `local_6d46b60b-d70a-4cfa-af2f-5bf90cdf7c08` (lms-qa-3) | #145 spot-check; E2E #120; then M2 #178 (T38) | reviewing |

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
None. (Board: [LMS v1](https://github.com/users/ziadtorkey/projects/1) — Todo → In Progress → In Review → Done.)

## Decisions needed from the product owner
- 2026-11-11: CI was blocked by GitHub Actions billing on the private repo (#152); owner chose to **make the repo public** — done after a secrets scan; CI running again.
- 2026-11-11: M2 plan approved; 13 answers recorded as D23–D36 (students, guardians and change requests move to M3). #103 closed.

## Recently merged
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

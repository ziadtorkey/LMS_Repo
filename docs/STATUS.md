# Project Status

_Updated by the Team Lead after every merge cycle._

**Milestone:** M0 Foundation — all code merged (18 / 19; QA closing checklist #16) · M1 Organization & Access — in progress (9 / 44 issues closed)
**Last update:** 2026-11-11 — Merged CI fix #95 (MySQL healthcheck + E2E rate limit), contract #98 (StoredFile in audit catalog), FE clients/level-types pages #81 (closes #59). Most open PRs need `main` merged in for the CI fix. Approved and waiting for that: #88, #100, #110. QA 4/5 could not be started over Remote Control; two QA subagents review #92/#99 and #94/#100/#105. M2 plan drafted (#103).

## Agents
| Agent | Model | Session id | Current issue / PR | State |
|---|---|---|---|---|
| Team Lead | Opus | `local_95ef9dc7-9378-4012-a8f2-c7d1f944bdec` (lms-lead) | coordinating M0 close-out + M1 | coordinating |
| Backend 1 | Sonnet | `local_19f31b04-ca2f-43d1-a2cb-8a128722a11f` (lms-backend) | M1 #42 clients; then #44, #46, #48, #50, #52 | working |
| Backend 2 | Sonnet | `local_d36e7dc2-e9e6-4f73-b7b3-b078b17bbe41` (lms-backend-2) | PRs #83 (#43), #88 (#85), #90 (#45); then #47, #49, #51 | working |
| Frontend | Sonnet | `local_40667dce-5be7-478b-95cb-4f14ecd203b9` (lms-frontend) | PR #81 (#59) fixes; then #60 org tree | working |
| Frontend 2 | Sonnet | `local_3d9e8938-4064-4f83-a04d-920704d41a87` (lms-frontend-2) | PRs #87 (#64), #89 (#65); #66 audit viewer | working |
| QA | Opus | `local_91a7af3c-7b02-4d61-ac11-2c22629cf89f` (lms-qa) | review #74 (M1), #14; fix + un-draft E2E PR #34 (#15) | working |
| QA 2 | Opus | `local_54d26c64-4403-4a13-b128-5be5e9752d97` (lms-qa-2) | #34 full run; #30; M1 BE2 PRs | reviewing |
| Backend 3 | Sonnet | `local_2e8038c6-e5ba-4e13-ade0-9440d9beb565` (lms-backend-3) | M1 #53 audit viewer; then #55, #57 | working |
| Backend 4 | Sonnet | `local_fd5c4410-8364-4c55-a5a8-3b8aa37afc1f` (lms-backend-4) | M1 #54 roles admin; then #56 | working |
| QA 3 | Opus | `local_6d46b60b-d70a-4cfa-af2f-5bf90cdf7c08` (lms-qa-3) | reviews BE3, BE4, FE2 PRs (#87, #89 now) | reviewing |

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
- M2 plan approval + 13 questions: issue #103 (draft plan on main; Lead asks via popup when M1 is near done — not blocking anything now).

## Recently merged
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

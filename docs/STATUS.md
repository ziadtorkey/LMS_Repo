# Project Status

_Updated by the Team Lead after every merge cycle._

**Milestone:** M0 Foundation — in progress (15 / 19 issues closed)
**Last update:** 2026-11-10 — Files PR #35 merged (closes #10). M0 left: #14 seed, #15 E2E (PR #34), #30, #16. M1 contract PRs #75/#76/#78/#79 (#36–#39) in QA 2 review; FE1 PR #74 (#58) in QA 1 review.

## Agents
| Agent | Model | Session id | Current issue / PR | State |
|---|---|---|---|---|
| Team Lead | Opus | `local_95ef9dc7-9378-4012-a8f2-c7d1f944bdec` (lms-lead) | M1 contract PRs #75, #76, #78, #79 in review | coordinating |
| Backend 1 | Sonnet | `local_19f31b04-ca2f-43d1-a2cb-8a128722a11f` (lms-backend) | #14 dev seed (users now unblocked); then #30; M1 #40 after contract #36 | working |
| Backend 2 | Sonnet | `local_d36e7dc2-e9e6-4f73-b7b3-b078b17bbe41` (lms-backend-2) | M1 #41 T6 forgot/reset password (after contract #75) | starting |
| Frontend | Sonnet | `local_40667dce-5be7-478b-95cb-4f14ecd203b9` (lms-frontend) | PR #74 (#58 T23) in review; next #59 | working |
| Frontend 2 | Sonnet | (waiting for owner to click Start) | M1 #64–#69 (T29–T34) after #58 | not started |
| QA | Opus | `local_91a7af3c-7b02-4d61-ac11-2c22629cf89f` (lms-qa) | review #74 (M1), #14; fix + un-draft E2E PR #34 (#15) | working |
| QA 2 | Opus | `local_54d26c64-4403-4a13-b128-5be5e9752d97` (lms-qa-2) | contract PRs #75–#79; #34 full run; #30 | reviewing |

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
None. (2026-11-10: M1 plan approved; D16–D22 recorded; 2nd FE approved.) (2026-11-10: owner approved Lead merging QA-approved green PRs, and a 2nd QA agent; D15: no access via Inactive units.)

## Recently merged
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

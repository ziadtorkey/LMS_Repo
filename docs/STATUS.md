# Project Status

_Updated by the Team Lead after every merge cycle._

**Milestone:** M0 Foundation — in progress (6 / 18 issues closed)
**Last update:** 2026-11-10 — Client shell PR #24 merged (closes #12). QA approved #23 (audit) and #26 (roles/context), but both must merge main first (conflicts after #20's squash). New PRs: #28 (context chooser, #13) and #29 (Me endpoints, #11). Owner decision D15: no access via Inactive units (follow-up #30).

## Agents
| Agent | Model | Session id | Current issue / PR | State |
|---|---|---|---|---|
| Team Lead | Opus | `local_95ef9dc7-9378-4012-a8f2-c7d1f944bdec` (lms-lead) | M0 coordination; M1 plan prep | coordinating |
| Backend 1 | Sonnet | `local_19f31b04-ca2f-43d1-a2cb-8a128722a11f` (lms-backend) | merging main into #26 (#7); PRs #27 (#9), #29 (#11) in review; next #30 | working |
| Backend 2 | Sonnet | `local_d36e7dc2-e9e6-4f73-b7b3-b078b17bbe41` (lms-backend-2) | merging main into #23 (#5, approved); fixing #25 (#8); then #6 | working |
| Frontend | Sonnet | `local_40667dce-5be7-478b-95cb-4f14ecd203b9` (lms-frontend) | PR #28 (#13) in review | waiting for review |
| QA | Opus | `local_91a7af3c-7b02-4d61-ac11-2c22629cf89f` (lms-qa) | #27, #29 reviews; spot-check #23/#26 merges; E2E #15 (#16) | reviewing |
| QA 2 | Opus | (waiting for owner to click Start) | #25, #28; then #6, #10 | not started |

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

## Blockers
None. (Board: [LMS v1](https://github.com/users/ziadtorkey/projects/1) — Todo → In Progress → In Review → Done.)

## Decisions needed from the product owner
None. (2026-11-10: owner approved Lead merging QA-approved green PRs, and a 2nd QA agent; D15: no access via Inactive units.)

## Recently merged
- #24 feat(client): MudBlazor shell with OIDC auth and ar/en RTL (closes #12)
- #20 feat(kernel): persistence kernel with client and subtree isolation (closes #4)
- #22 fix(contracts): explicit OrgAdmin permission list and platform-only guard (fixes #21)
- #19 feat(contracts): Me DTOs and API routes (closes #3)
- #18 feat(contracts): permission catalog, built-in roles, policy catalog, context header (closes #2)
- #17 chore: scaffold solution, docker compose and CI (closes #1)

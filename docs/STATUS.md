# Project Status

_Updated by the Team Lead after every merge cycle._

**Milestone:** M0 Foundation — in progress (5 / 17 issues closed)
**Last update:** 2026-11-10 — Kernel PR #20 merged (closes #4). Owner approved a 2nd QA agent (being started) because review is the bottleneck: #23–#27 are queued. Access evaluator PR #27 (#9) up; Backend 1 on #11.

## Agents
| Agent | Model | Session id | Current issue / PR | State |
|---|---|---|---|---|
| Team Lead | Opus | `local_95ef9dc7-9378-4012-a8f2-c7d1f944bdec` (lms-lead) | M0 coordination; M1 plan prep | coordinating |
| Backend 1 | Sonnet | `local_19f31b04-ca2f-43d1-a2cb-8a128722a11f` (lms-backend) | PRs #26 (#7), #27 (#9) in review; #11 T9 Me endpoints | working |
| Backend 2 | Sonnet | `local_d36e7dc2-e9e6-4f73-b7b3-b078b17bbe41` (lms-backend-2) | PR #25 (#8) in review; fixing QA changes on PR #23 (#5); then #6 | working |
| Frontend | Sonnet | `local_40667dce-5be7-478b-95cb-4f14ecd203b9` (lms-frontend) | fixing QA changes on PR #24 (#12); #13 T11 context chooser | working |
| QA | Opus | `local_91a7af3c-7b02-4d61-ac11-2c22629cf89f` (lms-qa) | #26, #27, then #11, #14 reviews; E2E #15 (#16) | reviewing |
| QA 2 | Opus | (being started) | #24, #25, #23 reviews; then #6, #10, #13 | starting |

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

## Blockers
None. (Board: [LMS v1](https://github.com/users/ziadtorkey/projects/1) — Todo → In Progress → In Review → Done.)

## Decisions needed from the product owner
None. (2026-11-10: owner approved Lead merging QA-approved green PRs, and a 2nd QA agent.)

## Recently merged
- #20 feat(kernel): persistence kernel with client and subtree isolation (closes #4)
- #22 fix(contracts): explicit OrgAdmin permission list and platform-only guard (fixes #21)
- #19 feat(contracts): Me DTOs and API routes (closes #3)
- #18 feat(contracts): permission catalog, built-in roles, policy catalog, context header (closes #2)
- #17 chore: scaffold solution, docker compose and CI (closes #1)

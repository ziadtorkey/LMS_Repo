# Project Status

_Updated by the Team Lead after every merge cycle._

**Milestone:** M0 Foundation — in progress (0 / 16 issues closed)
**Last update:** 2026-11-10 — M0 issues #1–#16 opened; T1 scaffold (#1) assigned to Backend; FE reviewing T10/T11; QA on review duty (#16).

## Agents
| Agent | Model | Session id | Current issue / PR | State |
|---|---|---|---|---|
| Team Lead | Opus | `local_95ef9dc7-9378-4012-a8f2-c7d1f944bdec` (lms-lead) | #2, #3 contract PRs (after #1) | coordinating |
| Backend | Sonnet | `local_19f31b04-ca2f-43d1-a2cb-8a128722a11f` (lms-backend) | #1 T1 scaffold + CI | working |
| Frontend | Sonnet | `local_40667dce-5be7-478b-95cb-4f14ecd203b9` (lms-frontend) | #12, #13 (review only until #1 merges) | waiting on #1 |
| QA | Opus | `local_91a7af3c-7b02-4d61-ac11-2c22629cf89f` (lms-qa) | #16 review duty, #15 E2E prep | waiting for first PR |

## M0 issue map
| Task | Issue | Lane | Depends on |
|---|---|---|---|
| T1 scaffold + CI | #1 | BE | — |
| Contract: Permissions/Roles/Policies/ContextHeader | #2 | Lead | #1 |
| Contract: Me DTOs/Routes | #3 | Lead | #1 |
| T2 kernel + isolation | #4 | BE | #1 |
| T3 audit | #5 | BE | #4 |
| T4 identity/OpenIddict | #6 | BE | #4 |
| T5 roles + context | #7 | BE | #4, #2 |
| T6 settings/policies | #8 | BE | #4 |
| T7 access evaluator | #9 | BE | #7, #8 |
| T8 files | #10 | BE | #4 |
| T9 Me endpoints | #11 | BE | #9, #3 |
| T10 client shell | #12 | FE | #1, #2, #3 |
| T11 context chooser + nav | #13 | FE | #12 |
| T12 dev seed | #14 | BE | #7, #11 |
| T12 E2E smoke | #15 | QA | #14, #13 |
| M0 review checklist | #16 | QA | all |

## Blockers
- GitHub Project board "LMS v1" not created yet: the `gh` token lacks the `project` scope (owner granting it). Issues will be added once available.

## Decisions needed from the product owner
None.

## Recently merged
—

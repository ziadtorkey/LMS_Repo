# M4b probe suite: mutation checks and pins (Task 20, QA2, #489)

Each check edits the code under test, runs the named tests, expects them to fail, and restores the code. Part 1 (#522) added the matrix and the live-session and package rows;
this part adds the visibility column, the webhook and isolation probes, and the pins.

## Mutations run (main at c9d7bcba)

| Mutation | Result |
| --- | --- |
| `LiveSessionHandlers.cs`: a student's module visibility check always true (`IModuleAccess` use) | `M4bVisibilityProbeTests.Every_caller_gets_the_answer_of_the_route_table("live session get (group 1 module)")` fails (group 2 reads a group 1 session) |
| `PackageToken.cs`: drop the audience check | `PackageHostTests.Token_from_another_package_client_or_audience_is_404` fails |
| `PackageRevocationCache.cs`: revocation never applies | `PackageHostTests.Revocation_of_client_a_does_not_affect_client_b` and `Revoked_student_token_is_refused_within_the_cache_window` fail |
| `PackageFileEndpoint.cs`: allow `..` in a path segment | `PackageHostTests.Path_rules_reject_everything_that_is_not_a_clean_relative_path` fails for `../other.txt` and `a/../../other.txt` |
| `PackageValidationJob.cs`: read the upload without the payload's organization | `PackageValidationJobTests.Validation_job_cannot_read_another_clients_upload` fails |
| `SettingEndpoints.cs`: `PUT` setting needs `Modules.Manage` instead of `Settings.Update` | `M4bIsolationTests.Teacher_cannot_enable_cloud_recording` fails (the teacher is no longer refused with 403). With `Settings.View` instead it still passes: a teacher holds neither, so that edit is equivalent. |

## To run when the code lands

| Backend | Mutation | Must fail |
| --- | --- | --- |
| #544 | remove the relationship or visibility check from join, recording or participants | `M4bVisibilityProbeTests` rows `live session join`, `recording set`, `participants` |
| #544 | return the join URL in the session DTO or the list | `A_student_lists_only_the_sessions_their_group_may_see_and_never_a_join_url` |
| #511 | verify the signature after the rate limit, or skip the timestamp window | `M4bWebhookProbeTests` (`stale`, `future`, `Repeated_failures_...`) |
| #511 | process an event of client A's meeting in client B | `M4bIsolationTests.Webhook_for_client_a_meeting_cannot_touch_client_b_rows` (fails with instructions the moment `ZoomEventProcessor.cs` exists; write the body then) |
| #576 | launch for staff, attempt commit by another student, `IModuleAccess` skipped on launch | the `package launch`, `package attempt commit` and `package attempt finish` rows (their fixtures throw until written with #576) |

## Pins (`M4bPending.Pins`)

`ROUTE` join, recording, participants, upcoming (#544), webhook and `FILE ZoomEventProcessor.cs` (#511), launch, preview, commit, finish (#576). A pinned key that is live fails
`M4bPendingProbeTests.Every_pin_is_still_pending` until the pin is removed; a key that is not live and not pinned fails the probe that asks for it; every M4b probe row whose route is not
mapped must be pinned (`Every_M4b_probe_row_that_is_not_mapped_is_pinned`). The pins for the package host, the validation job and the revocation cache were dropped while writing this: the
pin test showed them already live on main (their tests are the backend's, checked by the mutations above and named by `The_named_cross_client_tests_of_the_plan_exist`).

## Bypass sites

`M4bIsolationTests.Only_the_reviewed_M4b_files_bypass_the_query_filters_and_each_says_why` lists the files that may call `IgnoreQueryFilters` in M4b code (`EfHostSlotStore`, `LiveSessionService`, and
the Task 7 sites `ZoomEventProcessor`, `ZoomPollingJob`, `ZoomSyncSweepJob` once they exist) and requires the `// ISOLATION-BYPASS:` reason next to each use.

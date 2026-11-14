# M6 probe suite: mutation checks and pins (Task 31, QA2)

A probe is worth what it fails on. Each check edits the code under test, runs the named tests, expects them to fail, and restores the code.

## Done on this branch (contract and tests only)

| Mutation | Result |
| --- | --- |
| `MessagingRuleDefaults`: let `Student` reach `Student` (`Relationship`) | `M6SafeguardingMatrixTests.Contract_default_equals_the_plan_matrix` fails for `Student>Student` and `Guardian>Student` (2 cases) |
| `NotificationChannels`: add a public `Sms` constant | `M6NoSmsProbeTests.No_public_type_or_member_of_the_contract_domain_or_infrastructure_is_named_sms` and `No_constant_value_of_the_contract_is_sms` fail |

## To run with the backend, and what must fail

Record the failing test names here in the PR that merges each backend. Until then the probes skip through `M6Pending` (pinned, listed in the test output as `PENDING`), and `M6PendingProbeTests.Every_pin_is_still_pending` fails the moment a pinned route, table or type is on the branch, so the probes cannot stay skipped by accident.

| Backend | Mutation | Must fail |
| --- | --- | --- |
| #733 | remove the `ConversationAccess` call from one message route (`GET .../messages`, then `POST`, then `DELETE`) | `M6ConversationsProbeRows` wall rows via `M1EndpointMatrixTests` (a non-participant gets 200), `M6ParticipantIsolationTests.A_non_participant_cannot_read_the_conversation_or_its_messages` |
| #733 | make the route 404 for everyone (wrong handler) | `M6ParticipantColumnTests.The_participant_gets_a_working_route` (the positive column) |
| #733, #747 | remove the `min(stored, default)` in the policy (store a looser cell, e.g. `Student>Student = Allow`) | `M6SafeguardingMatrixTests.Create_direct_follows_the_matrix` and `Recipient_search_lists_exactly_the_people_the_matrix_allows` for the student pairs |
| #733 | answer a denied start with `rule` or `relationship` | `Create_direct_follows_the_matrix` (reason) and `Every_denied_start_has_the_same_body` |
| #747 | filter the recipient window after taking 180 candidates | `M6RecipientSearchTests.The_allowed_person_is_found_among_more_than_180_matches` |
| #750 | remove the outbox's `Sms` refusal (L9) | `M6PendingProbeTests.The_outbox_writer_refuses_an_sms_row_and_the_dispatcher_never_claims_it` (written with #750; today it fails with instructions the moment the writer type exists) |

## Rows added from the #762 checklist

| Checklist row | Probe | Waits for |
| --- | --- | --- |
| 1.4 moderation list read is audited | `M6PendingProbeTests.Reading_the_moderation_list_is_audited` (fails with instructions once the route exists) | #760 |
| `Cache-Control: private, no-store` on every `/api` response | `M6PendingProbeTests.Every_api_response_is_private_and_no_store` (`/api/me`, plus the conversation, messages, search and unread routes once mapped) | #736 |
| push endpoint host allow-list | `M6PendingProbeTests.Push_subscription_endpoint_must_be_an_allowed_push_service` (other host, plain http, link-local, localhost) | #767 |
| 2.3 denied start looks the same | `M6SafeguardingMatrixTests.Every_denied_start_has_the_same_body` | #733 |

## Pins (`M6Pending.Pins`)

`ROUTE POST /api/conversations` (#733), `ROUTE GET /api/messaging/recipients` (#747), `ROUTE GET /api/moderation/reports` (#760), `ROUTE POST /api/me/push-subscriptions` (#767), `TABLE DeliveryOutbox` (#750), `TYPE Lms.Infrastructure.Comms.DeliveryPlanner` (#750), `HEADER Cache-Control no-store on /api/me` (#736). Remove a pin in the PR that makes it live; a probe whose key is neither live nor pinned fails.

## Allowed names for the no-SMS scan (L9)

`M6NoSmsProbeTests.AllowedPublicNames` is empty. The outbox refuses an `Sms` row through a private constant or a literal, which the scan does not see; a public name for the refusal is added there, by exact name with the reason, in the PR that adds it.

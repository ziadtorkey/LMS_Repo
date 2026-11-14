namespace Lms.Shared;

/// <summary>
/// Values of the ProblemDetails extension <c>code</c> (M1 Global Constraints). Each value equals its name;
/// the client shows the resource <c>Error_{code}</c>. Append only; never rename.
/// </summary>
public static class ProblemCodes
{
    public const string not_found = nameof(not_found);
    public const string validation_failed = nameof(validation_failed);
    public const string code_taken = nameof(code_taken);
    public const string name_taken = nameof(name_taken);
    public const string level_type_in_use = nameof(level_type_in_use);
    public const string invalid_level_type = nameof(invalid_level_type);
    public const string unit_has_children = nameof(unit_has_children);
    public const string unit_has_assignments = nameof(unit_has_assignments);
    public const string unit_in_use = nameof(unit_in_use);
    public const string root_unit = nameof(root_unit);
    public const string invalid_move = nameof(invalid_move);
    public const string invalid_transfer_target = nameof(invalid_transfer_target);
    public const string transfer_target_in_use = nameof(transfer_target_in_use);
    public const string setting_locked = nameof(setting_locked);
    public const string policy_inherited = nameof(policy_inherited);
    public const string built_in_role = nameof(built_in_role);
    public const string role_in_use = nameof(role_in_use);
    public const string platform_only_permission = nameof(platform_only_permission);
    public const string permission_escalation = nameof(permission_escalation);
    public const string last_admin_permission = nameof(last_admin_permission);
    public const string assignment_exists = nameof(assignment_exists);
    public const string own_assignment = nameof(own_assignment);
    public const string branch_too_large = nameof(branch_too_large);
    public const string placeholder_missing = nameof(placeholder_missing);
    public const string placeholder_unknown = nameof(placeholder_unknown);
    public const string export_too_large = nameof(export_too_large);
    public const string year_overlap = nameof(year_overlap);
    public const string year_closed = nameof(year_closed);
    public const string year_active = nameof(year_active);
    public const string active_year_exists = nameof(active_year_exists);
    public const string term_overlap = nameof(term_overlap);
    public const string term_outside_year = nameof(term_outside_year);
    public const string event_outside_year = nameof(event_outside_year);
    public const string in_app_required = nameof(in_app_required);

    // M2 people: staff (D23, D26). 409 duplicate_person carries extension `field` (email | mobile | nationalId) only.
    public const string duplicate_person = nameof(duplicate_person);
    public const string person_not_found = nameof(person_not_found);
    public const string invalid_role_kind = nameof(invalid_role_kind);
    public const string cannot_change_own_status = nameof(cannot_change_own_status);
    public const string already_activated = nameof(already_activated);
    public const string invalid_expiry = nameof(invalid_expiry);

    // M2 imports (X-06, D34)
    public const string import_file_invalid = nameof(import_file_invalid);
    public const string import_too_many_rows = nameof(import_too_many_rows);
    public const string import_not_valid = nameof(import_not_valid);
    public const string import_in_progress = nameof(import_in_progress);
    public const string import_already_committed = nameof(import_already_committed);

    // M2 external sign-in (FR-SEC-08, D33)
    public const string provider_slug_taken = nameof(provider_slug_taken);
    public const string provider_unreachable = nameof(provider_unreachable);
    public const string external_account_not_found = nameof(external_account_not_found);
    public const string external_account_ambiguous = nameof(external_account_ambiguous);

    // M3 students, guardians and change requests (M2 appendix, D24, D25). invalid_level_type and year_closed exist above.
    public const string possible_duplicate = nameof(possible_duplicate);
    public const string student_code_taken = nameof(student_code_taken);
    public const string student_code_required = nameof(student_code_required);
    public const string student_code_not_allowed = nameof(student_code_not_allowed);
    public const string student_code_exhausted = nameof(student_code_exhausted);
    public const string invalid_code_pattern = nameof(invalid_code_pattern);
    public const string guardian_required = nameof(guardian_required);
    public const string primary_guardian_required = nameof(primary_guardian_required);
    public const string guardian_link_exists = nameof(guardian_link_exists);
    public const string guardian_incomplete = nameof(guardian_incomplete);
    public const string critical_field = nameof(critical_field);
    public const string change_request_pending = nameof(change_request_pending);
    public const string change_request_closed = nameof(change_request_closed);
    public const string change_request_stale = nameof(change_request_stale);
    public const string password_change_required = nameof(password_change_required);

    // M3 academic structure (courses, blueprints, classes, teachers, enrollment, groups, transfers)
    public const string invalid_owner_unit = nameof(invalid_owner_unit);
    public const string course_code_taken = nameof(course_code_taken);
    public const string course_in_use = nameof(course_in_use);
    public const string course_not_available = nameof(course_not_available);
    public const string allocation_exists = nameof(allocation_exists);
    public const string allocation_in_use = nameof(allocation_in_use);
    public const string restriction_not_met = nameof(restriction_not_met);
    public const string exception_pending = nameof(exception_pending);
    public const string exception_closed = nameof(exception_closed);
    public const string blueprint_draft_exists = nameof(blueprint_draft_exists);
    public const string blueprint_no_draft = nameof(blueprint_no_draft);
    public const string blueprint_stale = nameof(blueprint_stale);
    public const string blueprint_empty = nameof(blueprint_empty);
    public const string class_code_taken = nameof(class_code_taken);
    public const string class_in_use = nameof(class_in_use);
    public const string class_closed = nameof(class_closed);
    public const string class_full = nameof(class_full);
    public const string over_capacity = nameof(over_capacity);
    public const string year_not_effective = nameof(year_not_effective);
    public const string class_course_exists = nameof(class_course_exists);
    public const string class_course_in_use = nameof(class_course_in_use);
    public const string enrollment_not_optional = nameof(enrollment_not_optional);
    public const string already_enrolled = nameof(already_enrolled);
    public const string not_in_class = nameof(not_in_class);
    public const string already_placed = nameof(already_placed);
    public const string class_unit_mismatch = nameof(class_unit_mismatch);
    public const string teacher_not_eligible = nameof(teacher_not_eligible);
    public const string teacher_not_in_unit = nameof(teacher_not_in_unit);
    public const string primary_teacher_exists = nameof(primary_teacher_exists);
    public const string already_in_group = nameof(already_in_group);
    public const string not_enrolled = nameof(not_enrolled);
    public const string transfer_invalid = nameof(transfer_invalid);
    public const string too_many_items = nameof(too_many_items);
    public const string short_code_taken = nameof(short_code_taken);
    public const string invalid_short_code = nameof(invalid_short_code);
    public const string teacher_already_assigned = nameof(teacher_already_assigned);
    public const string assignment_ended = nameof(assignment_ended);

    // M4a wave 1 (lesson modules, assets, completion; M4a plan Task 1)
    /// <summary>Scheduled visibility needs a from and/or to date, and from must be before to.</summary>
    public const string module_visibility_range_invalid = nameof(module_visibility_range_invalid);

    /// <summary>A group target must name a live group of the module's ClassCourse; a ClassCourse target names none.</summary>
    public const string module_target_invalid = nameof(module_target_invalid);

    /// <summary>The file's extension / content type is not in <c>AssetTypes.All</c>, or does not match the module kind.</summary>
    public const string asset_type_not_allowed = nameof(asset_type_not_allowed);

    /// <summary>The file is larger than the effective <c>Files.MaxUploadMb</c>.</summary>
    public const string asset_too_large = nameof(asset_too_large);

    /// <summary>Only a module with the Manual completion rule can be marked complete by the student.</summary>
    public const string completion_not_manual = nameof(completion_not_manual);

    // M4a wave 2 (assignments, quizzes, library, forums; M4a plan Task 2)
    /// <summary><c>Penalty</c> needs <c>PenaltyPercent</c> 1–100; <c>Allow</c> and <c>Block</c> need none.</summary>
    public const string late_policy_invalid = nameof(late_policy_invalid);

    /// <summary>The assignment no longer takes submissions (past due under <c>Block</c>, or not visible yet).</summary>
    public const string submission_closed = nameof(submission_closed);

    /// <summary>The resubmission rule does not allow another submission now.</summary>
    public const string resubmission_not_allowed = nameof(resubmission_not_allowed);

    /// <summary>The quiz has closed (<c>ClosesAtUtc</c> passed): no new attempt.</summary>
    public const string quiz_closed = nameof(quiz_closed);

    /// <summary>The quiz has not opened yet (<c>OpensAtUtc</c> in the future) or has no questions.</summary>
    public const string quiz_not_open = nameof(quiz_not_open);

    /// <summary>The student has used every allowed attempt.</summary>
    public const string attempt_limit_reached = nameof(attempt_limit_reached);

    /// <summary>The attempt has ended (time limit or close, plus the grace) or was already submitted.</summary>
    public const string attempt_expired = nameof(attempt_expired);

    /// <summary>Correct options, points or the pool cannot change once a student has started an attempt.</summary>
    public const string quiz_has_attempts = nameof(quiz_has_attempts);

    /// <summary><c>DrawCount</c> must be between 1 and the number of questions.</summary>
    public const string pool_draw_invalid = nameof(pool_draw_invalid);

    /// <summary>The thread is locked: no new replies.</summary>
    public const string thread_locked = nameof(thread_locked);

    /// <summary>Download is disabled for this library resource; use the in-browser reader (D13).</summary>
    public const string download_disabled = nameof(download_disabled);

    // M4b wave 3b: SCORM and H5P packages (the reject reason of package_invalid is a PackageRejectReasons value in ProblemDetails.reason).

    /// <summary>The archive or its content failed validation; <c>reason</c> is a <c>PackageRejectReasons</c> value.</summary>
    public const string package_invalid = nameof(package_invalid);

    /// <summary>The archive is larger than <c>Files.MaxUploadMb</c> or would exceed the client's package storage.</summary>
    public const string package_too_large = nameof(package_too_large);

    /// <summary>The package is not <c>Ready</c> (still validating, or rejected): it cannot be attached, launched or previewed.</summary>
    public const string package_not_ready = nameof(package_not_ready);

    /// <summary>The package has no module yet, so its grading method cannot be changed (D57).</summary>
    public const string package_not_attached = nameof(package_not_attached);

    /// <summary>A tracking commit or finish was refused (closed attempt, preview token, oversize, unknown key, rate).</summary>
    public const string package_commit_rejected = nameof(package_commit_rejected);

    /// <summary>No separate content origin is configured (D56 fail-closed): upload, attach, launch and preview answer this (409).</summary>
    public const string package_host_not_configured = nameof(package_host_not_configured);
    // M4b wave 3a (live sessions; M4b plan Task 1)
    /// <summary>
    /// The meeting provider cannot take the request now: the host pool or the client's host share is exhausted, the
    /// daily create cap is reached, or Zoom is rate limiting / down (409 or 503, with <c>Retry-After</c>).
    /// </summary>
    public const string provider_unavailable = nameof(provider_unavailable);

    /// <summary>The platform meeting provider (Zoom) is not configured or not healthy, so the feature cannot be enabled.</summary>
    public const string provider_not_configured = nameof(provider_not_configured);

    /// <summary>The session is outside its join window (by server time): too early, ended or cancelled.</summary>
    public const string live_session_not_open = nameof(live_session_not_open);

    /// <summary>The feature flag for this action (e.g. <c>Features.VirtualClasses</c>) is off for the unit (403).</summary>
    public const string feature_disabled = nameof(feature_disabled);

    /// <summary>
    /// The recurrence is invalid: no end date and no count, both given, more than 52 occurrences, no weekday, an
    /// unknown time zone, or an end before the start.
    /// </summary>
    public const string recurrence_invalid = nameof(recurrence_invalid);

    // M5 wave 2: timetable (M5 plan Task 2).
    /// <summary>A slot clashes with another one of the term in <c>Block</c> mode (409); the answer lists the clashes, with details only for slots inside the caller's unit subtree (see <c>SlotConflictDto</c>).</summary>
    public const string timetable_conflict = nameof(timetable_conflict);

    /// <summary>A period overlaps another period of the same unit, or its start is not before its end (400).</summary>
    public const string period_overlap = nameof(period_overlap);

    /// <summary>A slot is used by a period that cannot be deleted (409).</summary>
    public const string period_in_use = nameof(period_in_use);

    /// <summary>The action needs a published slot: an exception on a draft slot (409).</summary>
    public const string timetable_not_published = nameof(timetable_not_published);

    /// <summary>An exception date is off the slot's weekday, outside the term, or two exceptions share a date (400).</summary>
    public const string exception_date_invalid = nameof(exception_date_invalid);

    /// <summary>The substitute of an exception is not a teacher of the client, is the teacher of record, or is already teaching in that period on that date (400).</summary>
    public const string exception_substitute_invalid = nameof(exception_substitute_invalid);

    // M6 wave 1: messaging, announcements, moderation and delivery (#613). A block is never revealed: a message to someone who blocked the sender is messaging_not_allowed.
    /// <summary>The rules matrix, a restriction or a block does not let the sender write to this person (403).</summary>
    public const string messaging_not_allowed = nameof(messaging_not_allowed);

    /// <summary>A moderator locked the conversation (409).</summary>
    public const string conversation_closed = nameof(conversation_closed);

    /// <summary>The message is longer than the limit or empty (400).</summary>
    public const string message_too_long = nameof(message_too_long);

    /// <summary>The message can no longer be deleted for everyone: the 15-minute window passed (409).</summary>
    public const string message_delete_window_closed = nameof(message_delete_window_closed);

    /// <summary>The user already reported this message (409).</summary>
    public const string report_duplicate = nameof(report_duplicate);

    /// <summary>A cap or rate limit was reached: messages a minute, new conversations or announcements a day, searches, test emails (429).</summary>
    public const string rate_limited = nameof(rate_limited);

    /// <summary>A student cannot post in a group (D74), or the group was set read-only; guardians may post only in teacher-run groups that include guardians (D77), staff always (403).</summary>
    public const string group_read_only = nameof(group_read_only);

    /// <summary>A mandatory notification cannot be switched off (409).</summary>
    public const string notification_mandatory = nameof(notification_mandatory);

    /// <summary>The announcement targets are not valid together, or a required target is missing (400).</summary>
    public const string announcement_target_invalid = nameof(announcement_target_invalid);

    /// <summary>An uploaded attachment is refused: a type outside the asset allow-list, a size over the limit, a content sniff mismatch, or more than <c>Announcements.MaxAttachments</c> (400).</summary>
    public const string attachment_invalid = nameof(attachment_invalid);

    /// <summary>A published announcement allows only its expiry, pin and withdrawal to change (409).</summary>
    public const string announcement_not_editable = nameof(announcement_not_editable);

    /// <summary>A client may only narrow the D69 messaging rules; this cell is looser than its default (400).</summary>
    public const string rules_looser_than_default = nameof(rules_looser_than_default);

    // M5 wave 1: grading (M5 plan Task 1).
    /// <summary>The gradebook is locked: writes are refused and reads serve the lock snapshot (409, L5).</summary>
    public const string gradebook_locked = nameof(gradebook_locked);

    /// <summary>Publishing under a provisional scale needs <c>AcknowledgeProvisionalScale = true</c> (409, L7).</summary>
    public const string scale_provisional = nameof(scale_provisional);

    /// <summary>The bands of a scale leave a gap or overlap, or there are too many (400).</summary>
    public const string scale_bands_invalid = nameof(scale_bands_invalid);

    /// <summary>A gradebook uses this scale: it cannot be deleted (409).</summary>
    public const string scale_in_use = nameof(scale_in_use);

    /// <summary>A category or template weight is outside 0-100 (400).</summary>
    public const string weights_invalid = nameof(weights_invalid);

    /// <summary>A score is below 0 or above the item's maximum points (422).</summary>
    public const string grade_out_of_range = nameof(grade_out_of_range);

    /// <summary>An override needs a reason of 1-500 characters (422).</summary>
    public const string grade_override_reason_required = nameof(grade_override_reason_required);

    /// <summary>The grade import file is not an accepted <c>.xlsx</c> or exceeds the limits (400, L9).</summary>
    public const string grade_import_invalid = nameof(grade_import_invalid);

    /// <summary>The import preview token expired or does not belong to this caller, gradebook, item and file (400, L9).</summary>
    public const string grade_import_expired = nameof(grade_import_expired);

    /// <summary>A term of the year already has a gradebook, slot or attendance session: the term structure cannot change (409, L8).</summary>
    public const string terms_have_dependants = nameof(terms_have_dependants);

    /// <summary>The row changed since it was loaded (a stale <c>Version</c>); the answer carries the current value (409, L12).</summary>
    public const string concurrency_conflict = nameof(concurrency_conflict);

    // M5 wave 3: attendance and excuses (M5 plan Task 3).
    /// <summary>The edit window of a taken session is over; a teacher cannot edit, a user with <c>Attendance.Edit</c> can, with a reason (409, L14).</summary>
    public const string attendance_edit_window_closed = nameof(attendance_edit_window_closed);

    /// <summary>The session date is after the client's local today: it cannot be taken or created yet (409).</summary>
    public const string attendance_session_future = nameof(attendance_session_future);

    /// <summary>A Taken session has roster students without a status (422).</summary>
    public const string attendance_unmarked_rows = nameof(attendance_unmarked_rows);

    /// <summary>An edit after the window needs a reason of 1-500 characters (422, L14).</summary>
    public const string attendance_reason_required = nameof(attendance_reason_required);

    /// <summary>Confirm and live mappings apply only to a session proposed from a live session (409).</summary>
    public const string attendance_not_a_proposal = nameof(attendance_not_a_proposal);

    /// <summary>The excuse is outside the policy; the <c>reason</c> extension is one of <c>ExcusePolicyViolations</c> (409, L13).</summary>
    public const string excuse_out_of_policy = nameof(excuse_out_of_policy);

    /// <summary>The caller may not submit for this student under <c>Excuses.Submitters</c> (403, D61).</summary>
    public const string excuse_submitter_not_allowed = nameof(excuse_submitter_not_allowed);

    /// <summary>The reviewer is the submitter of the excuse (409, L14).</summary>
    public const string excuse_reviewer_is_submitter = nameof(excuse_reviewer_is_submitter);

    /// <summary>A rejection needs a comment of 1-500 characters (400).</summary>
    public const string excuse_comment_required = nameof(excuse_comment_required);

    /// <summary>The attachment type, size or content is not accepted (400, L10).</summary>
    public const string excuse_attachment_invalid = nameof(excuse_attachment_invalid);

    /// <summary>The excuse is no longer <c>Submitted</c> or <c>Escalated</c>: it cannot be decided, escalated or withdrawn (409).</summary>
    public const string excuse_not_pending = nameof(excuse_not_pending);
}

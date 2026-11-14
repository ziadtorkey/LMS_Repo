# M6 FE2 pages: data-testid map for E2E (Tasks 33 to 35)

Announcements (`/announcements`, `/announcements/{id}`, `/announcements/new`, `/announcements/{id}/edit`)
- list: `anns-title`, `anns-new`, `anns-tabs`, `anns-feed`, `ann-card-{id}`, `ann-unread-{id}`, `ann-read-{id}`, `ann-open-{id}`, `anns-empty`, `anns-error`
- manage: `anns-status`, `anns-table`, `ann-row-{id}`, `ann-status-{id}`, `ann-edit-{id}`, `ann-publish-{id}`, `ann-withdraw-{id}`, `ann-delete-{id}`
- detail: `annd-title`, `annd-body`, `annd-status`, `annd-reach`, `annd-files`, `annd-notfound`
- composer: `annc-title`, `annc-error`, `ann-compose`, `ann-title-ar`, `ann-title-en`, `ann-body-ar`, `ann-body-en`, `ann-scope-{Kind}`, `audience-select`, `role-{Staff|Students|Guardians}`, `reach`, `ann-mode-now`, `ann-mode-schedule`, `ann-date`, `ann-time`, `ann-pin`, `ann-notify`, `ann-expires`, `ann-save-draft`, `ann-publish`, `ann-cancel`

Preferences (`/notifications/preferences`)
- `prefs`, `prefs-col-{InApp|Email|Push}`, `prefs-row-{eventKey}`, `prefs-{eventKey}-{channel}`, `prefs-source-{eventKey}-{channel}`, `prefs-bar`, `prefs-save`, `prefs-discard`, `prefs-error`, `prefs-email-bounced`, `prefs-test-email`, `prefs-test-result`, `prefs-devices`, `prefs-device-{id}`, `prefs-device-remove-{id}`
- push: `push-alert`, `push-on`, `push-consent`, `push-consent-agree`, `push-consent-cancel`, `push-unsupported`

Messaging rules (`/settings/messaging-rules`)
- `rules-title`, `rules-matrix`, `rules-row-{From}`, `rules-{From}-{To}`, `rules-cell-error-{From}-{To}`, `rules-refused`, `rules-error`, `rules-bar`, `rules-save`, `rules-discard`

Communication settings (`/settings/communication`)
- `comms-title`, `comms-channels`, `comms-email`, `comms-push`, `comms-{Field}` (the nine caps and limits), `comms-quiet-on`, `comms-quiet-start`, `comms-quiet-end`, `comms-quiet-zone`, `comms-receipts`, `provider-{Channel}`, `comms-bar`, `comms-save`, `comms-discard`, `comms-error`

Moderation (`/moderation/messages`)
- `mod-title`, `mod-stats`, `mod-stat-open`, `mod-queue`, `mod-filter-status`, `mod-filter-reason`, `mod-keep-selected`, `mod-table`, `mod-row-{id}` (`data-safety`), `mod-pick-{id}`, `mod-status-{id}`, `mod-open-{id}`
- detail: `mod-detail`, `mod-due`, `mod-note`, `mod-sender`, `mod-context` (`role=log`), `mod-msg-{messageId}`, `mod-frozen`, `mod-decision`, `mod-{Action}`, `mod-days`, `mod-note-to-people`, `mod-apply`, `mod-resolved`
- restrictions: `mod-restrictions`, `mod-restriction-{userId}`, `mod-lift-{userId}`, `mod-no-restrictions`

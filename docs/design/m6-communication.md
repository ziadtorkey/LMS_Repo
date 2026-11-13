# M6 design spec (draft): communication

**Status:** draft, revision 2 (QA2's review on #615, aligned with FE1's #612 and BE1's #613). It isn't a plan task yet; the plan decides scope and order. Names that must match the backend (`ModerationActions`, `AnnouncementTargetKinds`, statuses) follow #613; §11 lists what this spec asks #612 and #613 to change.
**PRD:** FR-NOT-01, FR-NOT-02, FR-NOT-03, FR-MSG-01, FR-MSG-02, FR-MSG-03, FR-MSG-04; X-01 (bilingual), X-07 (notifications), X-10 (accessibility, 360 px).
**Mockups:** `docs/design/mockups/m6.html`. It's static HTML in the Stitch tokens from `LmsTheme.cs` and `lms-stitch.css`. Open it with `?screen=inbox|thread|composer-states|compose|announcements|announce-compose|notifications|prefs|moderation|rules&lang=ar|en&theme=light|dark` (and `screen=prefs&push=denied`).
**Screenshots:** `docs/screenshots/ux-m6/<screen>-<lang>-<theme>-<width>.png`. Each screen is shot at 1440 px (en light, ar dark) and at 360 px (ar light, en dark): 44 shots, none scrolls sideways.

Everything in `docs/DESIGN.md` (the Stitch section and its deviations table) applies. This file only adds what M6 needs. The new classes in §8 go into `lms-stitch.css` in a small `lane:ux` PR before the M6 pages start.

---

## 1. Shared rules for M6

- **Rules decide what can be chosen; the UI explains them.** The messaging rules (FR-MSG-02) are enforced by the API. The UI shows what the rules allow and, where someone could reasonably expect more, says why it isn't there:
  - The recipient picker lists a near-miss as a **disabled option with its reason** (for example, a class the teacher doesn't teach). It never lists people the user can't know about.
  - In a read-only conversation, the composer becomes an info alert with the rule in plain words.
  - **A blocked person is never told they are blocked** (PRD, #612, #613). They see the generic "You can't send messages in this conversation." Only the person who blocked sees "You blocked Ahmad Yousef…" with **Unblock**. Both states are in `screen=composer-states`, with the moderator restriction, messaging turned off, and the rate limit.
- **Two different tools, two different words.** A **block** is one person's choice about another (`UserBlock`, `PUT /api/me/blocks/{id}`); only the blocker can lift it. A **restriction** is a moderator's decision that a person can't send for a time (`MessagingRestriction`, with an end date). The UI never mixes the words: "Block" and "Unblock" belong to the person; "Restrict from messaging" and "Lift restriction" belong to moderators. A moderator never sees or lifts a person's own blocks, so a moderator can't expose someone who blocked a harasser.
- **Dates and times** follow DESIGN.md: `yyyy-MM-dd` and `HH:mm`, invariant, inside `<span dir="ltr">`. In lists, recent items use relative words: today shows the time, then "Yesterday" and the weekday name for this week, then the date.
- **Names and mixed scripts.** Every person or group name sits in its own `<bdi>` (`dir="auto"`), so an English name in an Arabic sentence ("Huda Al-Harbi sent you a message") keeps its order. Message text is shown with `dir="auto"` for each message.
- **Counted text in Arabic** uses the shared plural helper with the four keys from the M4b spec: "3 new messages", "24 people", "Read by 412 of 590".
- **Realtime.** New messages and notifications arrive over SignalR (FR-NOT-02):
  - The thread body is `role="log"` with `aria-live="polite"`, and only messages from other people are announced.
  - The unread counters (sidebar badge, bell) update silently.
  - The snackbar for a new message is shown only when the user isn't already in that conversation.
- **Disabled, not hidden** for actions: permissions decide whether an action exists, and state (rules, blocks, scheduling) disables it with a caption.
- **Server states the composer shows** (all in `screen=composer-states`):

  | State (problem code) | What the sender sees |
  |---|---|
  | Rules don't allow it (`messaging_not_allowed`) | The rule in plain words, read-only. |
  | Blocked by the other person (`recipient_blocked`) | "You can't send messages in this conversation." Nothing more. |
  | The sender blocked them | "You blocked [name]…" and **Unblock**. |
  | Restricted by a moderator (403 restricted) | "You can't send messages until yyyy-MM-dd. You can still read your conversations." |
  | Feature off (`feature_disabled`) | "Messaging is not enabled for your school." The UI doesn't pre-hide by flag (as in #612 and M4b). |
  | Rate limit (429, `Retry-After`) | The text stays in the field, Send is disabled, and a `role="status"` line says "You can send again in 20 s", counting down. |
- **Icons:** Rounded only.
  - **Messages and groups:** Forum (Messages), EditSquare (new message), Groups (group), Person (a person).
  - **Announcements:** Campaign (announcement), PushPin (pinned), Schedule / ScheduleSend (scheduled).
  - **Moderation:** ShieldPerson (moderation), Flag (report), Block (block).
  - **Composer and channels:** AttachFile, Send (mirrored in RTL), Mail, Sms, PhoneIphone (push), NotificationsOff, Lock (set by the school).

## 2. Inbox and thread (FR-MSG-01, FR-MSG-02)

Mockups: `screen=inbox` (a one-to-one conversation) and `screen=thread` (a group conversation, the message menu, a removed message and a read-only composer).

1. **Route and nav.** The inbox is `/messages`, and a conversation is `/messages/{conversationId}`.
   - The Stitch sidebar (#578) gets a **Communication** group with Messages (unread badge), Announcements and Notifications.
   - **Moderation** (`/moderation/messages`, the route in #612) and **Messaging rules** (`/settings/messaging-rules`, §7a) go in the existing **Security and settings** group, which is #612's "Administration".
   - #612's Messages icon next to the bell is kept as a shortcut in the app bar, with the same badge.
   - **The Messages badge** counts conversations with unread messages, not messages. Its accessible name says so: "Messages, 3 conversations with new messages". #612's `MessagingState` should expose the same number.
2. **Layout.**
   - **At 900 px and wider:** one `SectionCard Flush` holds two panes (`lms-inbox`). The conversation list is 360 px wide and the thread fills the rest. The card fills the height under the page header, so the list and the thread scroll on their own and the composer stays visible.
   - **Below 900 px:** one pane at a time. `/messages` shows the list and `/messages/{id}` shows the thread, with a **Back to messages** icon button in the thread header (the arrow mirrors in RTL). Back uses browser history, so the list keeps its scroll and filter.
3. **List tools.** A search field (recessed input, placeholder "Search messages", searches names and message text) and a segmented control: All, Unread, Groups.
4. **Conversation row (`lms-conv`).**
   - **Content:** an avatar (initials, or the Groups icon for a group), the name (ellipsis), the time (end-aligned), and a one-line preview. In a group, the preview has the author first: "Sara: …", or "You: …" for the user's own messages.
   - **Unread:** the name and preview are bold, with a count badge in primary at the end. This is the only place a count badge is primary instead of error, because unread messages are normal, not alarming.
   - **Current row:** `aria-current="true"`, a primary tint and a 3 px bar on the inline-start edge.
   - **Accessibility:** each row is one link. Its accessible name is "name, n unread, time, preview".
5. **Thread header.** Avatar, name, and a caption: a person's role and context ("Guardian of Omar Saleh · 10-A"), or the group size ("Group · 24 people"). The participants are always discoverable: a group's caption opens **Members**, so a student can see exactly who can read the conversation. Nobody outside the participant list can read it (#613 privacy rules): guardians have no access to their child's conversations with staff unless the owner decides otherwise (Q2), and the UI shows no "Shared with guardians" state until then. The header ends with a More actions menu: Mute conversation, Members (for groups), and Leave group (for groups the user didn't create).
6. **Messages.**
   - **Bubbles:** the user's own messages sit at the inline end, tinted with 12 % primary. Other people's sit at the inline start, on the surface colour with a 1 px border. Bubbles are at most 560 px wide (88 % at 360 px). The corner nearest the sender is 4 px.
   - **Sender names:** in a group, the sender's name is shown above their first bubble in a run.
   - **Day separators:** a centred `lms-day` chip.
   - **Meta line:** the time, then, for the user's own messages, the state: "Sent" or "Seen" (with the DoneAll icon). Read receipts are optional (FR-MSG-01) and follow the client setting; when they're off, "Seen" never shows.
   - **Attachments:** a recessed row inside the bubble with the file-type icon, the name and the size. Images show a thumbnail at most 240 px wide, and open in a new tab.
   - **Hidden messages:** `lms-msg-removed` shows a dashed outline and the italic text "This message was hidden by a moderator." (`HideMessage`, state `HiddenByModerator` in #613). The original text isn't shown to anyone in the conversation.
7. **Message menu.** On hover or focus of a message, a More icon button appears (it stays visible on touch screens). Its menu has Copy text and **Delete for me** (#612; whether senders may delete for everyone in a window, as #613 proposes, is #612 Q5), then a divider, then **Report message** and **Block [name]** in the error colour, last. Report opens a small dialog: a reason (radio: Harassment, Spam, Safety concern, Other) and an optional note. Block confirms with what it does: "You won't get messages from Ahmad. They aren't told."
8. **Composer** (`lms-composer`, sticky at the bottom of the thread):
   - an Attach icon button, a multi-line recessed text field ("Write a message"), and a filled Send button with its label (the icon mirrors in RTL);
   - **Enter adds a new line and Ctrl+Enter (Cmd+Enter) sends.** #612 proposes Enter to send; this spec keeps Ctrl+Enter, because Arabic input methods confirm candidates with Enter and touch keyboards have no Shift, so Enter-to-send sends half-written messages. A hint under the field says "Ctrl+Enter to send". #612 should follow (its Q13);
   - the Send button is always visible, so the shortcut is never the only way;
   - while sending, the button is disabled, and a failed message stays in the thread with "Not sent · Try again" in the error colour.
9. **Read-only conversation** (`lms-composer-off`): the composer is replaced by an info alert with the reason, for example "You can only read this conversation. Messaging rules at your school don't allow students to message other students."
10. **Empty states.** No conversations: "No messages yet", with New message as the action when the user may send. No search results: "No conversations match".

## 3. Compose and the recipient picker

Mockup: `screen=compose`.

1. **New message** opens a dialog (full screen below 600 px). It's a dialog, not a page, so the inbox stays in place behind it.
2. **To.** A recessed multi-select autocomplete (`MudAutocomplete` with chips). The results come from a server endpoint that applies the messaging rules, after 2 typed characters, debounced at 300 ms. They're grouped:
   - **Classes and courses:** "Physics 1 · 10-A — guardians" and "— students", each with its size at the end. They're only offered where the user teaches or manages, and the rules allow it.
   - **People:** an avatar, the name, and a caption with the role and context. Picked people get a check mark.
   - A **disabled option with its reason** for a near-miss the user would expect: "Physics 1 · 10-B — guardians · Not allowed: you don't teach 10-B". It's shown **only for a class inside the sender's own unit scope**, at most one row, **never a person, and never a size**. Nothing outside the sender's scope is named. The server returns it with the rules summary (#612's recipients read); if it can't do that cheaply, drop the row and keep the hint line.
3. **Subject** (optional) becomes the group name when there are several recipients. Its help text says so.
4. **Message:** a recessed multi-line field, 5 lines high.
5. **Group note:** when more than one person is picked, an info alert says "Replies go to everyone in the group. To reply privately, people start a new message."
6. **Actions:** Attach a file (a text button, at the inline start), Cancel, and Send (filled). Sending opens the new conversation.
7. **Starting from elsewhere.** Starting a message from a person (for example, a Message button on a student's guardian card) opens the same dialog with that person already picked.

## 4. Announcements (FR-MSG-03)

Mockups: `screen=announcements` (the list) and `screen=announce-compose` (new or edit).

1. **Route.** `/announcements` lists the announcements for the user's audience. Course announcements from M4a stay on the class course's Announcements tab, and they also appear here with their course chip.
2. **Filters.** A segmented control on the statuses of #613: Current (`Published`), Scheduled, Drafts, Expired and Withdrawn. Everything after Current is shown only to people who can announce, and lists only their own announcements plus those they manage.
3. **Card** (`lms-ann`, at most 860 px wide):
   - **Header:** a section icon (PushPin when pinned, Schedule when scheduled, Campaign otherwise), then the title (h2) and status chips: **Pinned** (success, with a dot) and **Scheduled** (info, with a dot).
   - **Meta line:** the author or office, the date, and the "Until yyyy-MM-dd" expiry.
   - **Body:** the text, clamped to 4 lines, with "Read more".
   - **Audience chips:** one per target, using #613's `AnnouncementTargetKinds` only: Domain (Organization), AccountTree (Unit), Groups (Class), MenuBook (Course = ClassCourse), Diversity3 (Group), Badge (Role). When `IncludeGuardians` is on, one more chip says "Guardians included". There's no Level target.
   - **Read count:** "Read by 412 of 590", shown only to the author and to managers.
   - **Order:** pinned cards first (with a 35 % primary border), then newest first.
   - **More actions menu:** Edit (Draft and Scheduled; once Published only pin and expiry change, #613), then **Withdraw** (error colour, last, with a confirmation; #613's word and status).
4. **New announcement** dialog:
   - **Titles:** in Arabic and in English, side by side, each field in its own direction; the text field is below them. Both titles are required, as for every bilingual business name (`NameAr`/`NameEn`); the error is shown at the empty field: "Enter the title in English too. Each reader sees the title in their language." (#613 has one title and #612 has two: see §11.)
   - **Audience** (a fieldset with the legend "Who should see it"):
     - a segmented scope control with #613's kinds: Organization, Unit, Class, Course (ClassCourse), Group, Role. Only the kinds the author may use are shown (a teacher sees Course and Group only);
     - the targets of that kind as a multi-select with chips (for Role: the role and the unit it applies at);
     - an **Include guardians** toggle row (`IncludeGuardians`, default as #613 Q20 decides);
     - a reach estimate in an info alert (`role="status"`, debounced 500 ms) from `…/audience-preview`: counted **only inside the author's scope**, and rounded above 100 ("About 600 people"), so it can't be used to measure a part of the school the author can't see.
   - **Publishing:** a segmented Now / Schedule control. Schedule reveals the date and time fields and the time-zone caption, worded as in the M4b spec.
   - **Toggle rows** (`lms-toggle-row`): Pin to the top, and Notify the audience. Each has a one-line help text.
   - **Attachments:** up to 5 files, as in messages (#612). #613 has no announcement attachments yet: see §11.
   - **Expires on:** an optional date, with help text.
   - **Buttons:** Cancel, **Save as draft** (outlined), and Publish or Schedule (filled). The label follows the publishing mode.

## 5. Notification centre (FR-NOT-02)

Mockup: `screen=notifications`. This extends the bell that exists today (`NotificationBell.razor`). It doesn't replace it.

1. **Bell popover.** It's 400 px wide (full width below 600 px) and becomes a card:
   - **Header:** the "Notifications" title (h2), a **Mark all as read** text button, and a Settings icon button that links to the preferences.
   - **Body:** the newest first, with an "Earlier" group label after today's items.
   - **Footer:** **See all notifications**.
2. **Item** (`lms-notif`):
   - **Layout:** a kind tile in a recessed square with an icon per event kind, the sentence with the actor in bold (as a `bdi`), an optional one-line quote of the message, and the relative time.
   - **Unread:** a soft primary tint and an `--lms-accent` dot at the end. The dot isn't the only signal, because the accessible name starts with "Unread:".
3. **Grouping.** Messages in one conversation collapse to one item: "3 new messages in Physics 1 · 10-A guardians". Opening any item marks it read and deep-links to the target.
4. **Badge.** The bell's badge keeps the error colour and shows "99+" past 99. The Messages badge counts conversations with unread messages (§2.1), with that wording in its accessible name.
5. **Notifications page.** The `/notifications` page keeps its table. It gains the same kind icons, and a filter segment: All, Unread.

## 6. Channel preferences (FR-NOT-01, FR-NOT-03)

Mockup: `screen=prefs`. This extends the existing `/notifications/preferences` page and the 2 × 2 phone grid from #590.

1. **Columns:** In-app, Email, SMS, Push.
   - Each column header has an icon, the channel name, and a caption with the address in use: the masked email, the masked phone, or "This device: on/off" for push.
   - In-app is always on: its switches are locked, with the Lock icon *before* the switch.
2. **Locked cells.** Events the school makes mandatory (for example, absence of my child, or a pinned announcement by email) show a locked switch with the Lock icon. A footer caption explains the icon: "Set by your school: can't be turned off."
3. **Push on this device.** When push isn't enabled for this browser:
   - a warning alert above the table offers **Turn on for this browser** (an outlined button) and explains that the browser will ask for permission;
   - if the browser permission was denied, the alert says how to allow it in the browser settings instead, with no button (`screen=prefs&push=denied`, shot at 360 px too);
   - if the browser can't do push, the Push column shows "Not available on this browser" and its switches are disabled.
4. **No phone number.** SMS switches are disabled when there's no mobile number on file, with the caption "Add a mobile number in your profile", which links to the profile.
5. **Save.** A filled Save button in a footer bar. When the table is long, the footer becomes the `lms-save-bar` while there are unsaved changes.
6. **At 360 px:** each event is a block, with the event name first and then one row per channel: the label at the start, and the lock and switch at the end. These are simple rows, not a grid, so the switches line up.

## 7. Moderation queue (FR-MSG-04)

Mockup: `screen=moderation`.

1. **Route:** `/moderation/messages` (#612), behind `Messaging.Moderate` (Org Admin per the PRD matrix; see Q4). The queue is scoped by `ModerationUnitPath` (#613).
2. **Stat tiles:** Open reports, New today, **Restricted from messaging**, Median time to decide. Below 600 px they're two per row, with the icon above the value (the #580 rule).
3. **Queue.** A table card with a segmented control (Open reports, Closed) and an error chip with the target time: "Safety concern: review within 24 h".
   - **Columns:** Reported message (a one-line excerpt), Reason, Reported by, When, Status. Status is a soft chip: New (info), In review (warning), Removed or Kept (neutral).
   - **Safety concern:** a "Safety concern" report shows its New chip in the error colour and sorts first. The notification to moderators for it is mandatory and ignores quiet hours (#613 §4, mandatory/urgent events).
   - **Selection:** the selected row is tinted. Below 600 px the rows stack as labelled cards (`DataLabel`).
4. **Detail panel.** At 1100 px and wider it's a 380 px card beside the table. Below that it opens as a dialog (full screen at 360 px). It contains:
   - the conversation name and size;
   - the **context**: the message before and after the reported one, read-only, with the reported message outlined in the warning colour and a Reported message chip;
   - the reporter's reason and note;
   - the sender line: name, role, class, and the number of earlier reports and removals;
   - the **decision**, a radio group of soft rows (`lms-toggle-row` with a radio), mapped one-to-one to #613's `ModerationActions`:

     | UI option | Action | What happens |
     |---|---|---|
     | Dismiss the report | `Dismiss` | The message stays; the reporter is told it was reviewed. |
     | Hide the message | `HideMessage` | Everyone in the conversation sees "hidden by a moderator". |
     | Hide it and restrict the sender, **for [7 days ▾]** | `HideMessage` + `Restrict` (`UntilUtc`) | The sender can read but can't send until the end date, shown as "Until yyyy-MM-dd". Durations: 1, 7, 30 days. |
     | Lock the conversation | `LockConversation` | Nobody can send in it any more (for a group gone wrong). |

     The last two have their labels in the error colour, last. #613's `DeleteMessage` has no UI in v1: hiding is enough for participants, and hard deletion is a retention question (Q6). #612's `WarnSender` becomes the optional note to the sender. Each option has one line explaining what happens.
   - an optional note to the people involved, and **Save decision** (filled).
5. **After a decision.**
   - **Who is told:** the reporter and the sender are notified (`evMod`), but the reporter never learns which action was taken against the sender. The reporter sees "reviewed".
   - **Audit:** every decision is recorded (X-02).
   - **The message:** hiding it replaces the text for everyone with the hidden state from §2.6.
6. **Lifting a restriction.** The **Restricted from messaging** tile filters to a list of `MessagingRestriction`s (person, until, by, reason) with a **Lift restriction** action in each row. It never lists or touches people's own blocks (§1).

## 7a. Messaging rules (FR-MSG-02)

Mockup: `screen=rules`. This answers #613 Must 7 (a page for what `Comms.Manage` owns).

1. **Route:** `/settings/messaging-rules`, behind `Comms.Manage` at the client root unit, in the **Security and settings** group.
2. **The matrix:** the rows are the sender's role ("From") and the columns the recipient's role ("To"), using the built-in roles. Each cell is a small select showing #613's `MessagingRule.Via`: **Anyone** (`Anywhere`, meaning anyone in the sender's unit scope), **Same unit** (`SameUnit`), **Own classes** (`Relationship`), or **Not allowed**. The chips are tinted by permissiveness: success for Anyone, info for the narrower two, neutral for Not allowed.
3. An info alert above it says student to student is off by default, that changes apply to new conversations and new messages, and that nothing already sent is hidden.
4. **Save rules** (filled) at the top, and the save bar while there are changes. Saving is audited (`[Audited]`).
5. **At 360 px:** each "From" role becomes a block with one labelled row per recipient role.
6. Caps, quiet hours and provider status (also `Comms.Manage`) go on a sibling **Communication settings** page, which is not drawn here; it's simple settings rows (`lms-toggle-row` and recessed inputs).

## 8. New classes (`lms-stitch.css`)

These are to be added in one `lane:ux` PR before the M6 pages start. The reference CSS is the "M6 additions" block in the mockup. It uses logical properties and palette variables only, so `StitchStylesheetTests` passes as is.

| Class | Use |
|---|---|
| `lms-inbox`, `lms-inbox-list`, `lms-inbox-tools`, `lms-convs` | The two-pane inbox, and one pane below 900 px (`show-thread` picks the pane) |
| `lms-conv` (+ `.unread`, `[aria-current]`), `lms-conv-main`, `lms-conv-top`, `lms-conv-name`, `lms-conv-preview` | A conversation row |
| `lms-thread`, `lms-thread-head`, `lms-thread-body`, `lms-day` | The thread pane and the day separators |
| `lms-msg` (+ `-me`, `-them`, `-removed`), `lms-bubble`, `lms-msg-meta`, `lms-msg-from`, `lms-attach` | Messages |
| `lms-composer`, `lms-composer-off` | The composer and its read-only replacement |
| `lms-picker-group`, `lms-picker-opt` | The grouped recipient options (in the `MudAutocomplete` popover class) |
| `lms-ann` (+ `.pinned`), `lms-ann-meta`, `lms-ann-text`, `lms-audience` | Announcement cards |
| `lms-notif` (+ `.unread`), `lms-kind` | Notification items |
| `lms-mod`, `lms-flagged` | The moderation layout and the outline on the reported message |

## 9. Accessibility checklist for M6

- [ ] The thread is a `role="log"` region. A new message from someone else is announced once, politely, as "name: text".
- [ ] Each conversation row is one link with a full accessible name. The unread state is in that name, not only in the styling.
- [ ] Message action buttons appear on focus as well as on hover, and are always visible on touch screens. They're reachable with Tab, and the menu closes with Escape and returns focus to its button.
- [ ] The recipient picker is a combobox with grouped options (`role="group"` with a label). Disabled options have `aria-disabled` and their reason in their name.
- [ ] Sending never depends on a key shortcut alone: there's always a visible Send button.
- [ ] The reach estimate and the "N new messages" counts are `role="status"`, and they aren't re-announced on every keystroke (debounce of 500 ms).
- [ ] The moderation decision is a fieldset with a legend, and the destructive option has more than its colour (its help text says what happens).
- [ ] Nothing scrolls sideways at 360 px, and every table cell has a `DataLabel`. All 44 mockup shots were checked with `scrollWidth`.
- [ ] The rate-limit countdown is a `role="status"` that announces the start and the end only, not every second.
- [ ] The Messages badge's accessible name counts conversations ("3 conversations with new messages").

## 10. Questions for the Team Lead (owner decisions)

- **Q1 Read receipts:** on or off by default for each client? Should students see "Seen" on staff messages? (The mockup shows "Seen" with receipts on.)
- **Q2 Guardian visibility:** may guardians read their child's conversations with staff? The PRD doesn't say. **Default (safeguarding): no.** If the owner says yes, a "Shared with guardians" caption goes in the thread header; until then nothing is shown and nothing is shared.
- **Q3 Report reasons:** is the list fixed (Harassment, Spam, Safety concern, Other), or set for each client? The proposal is fixed, with "Safety concern" reviewed within 24 h and its moderator notification exempt from quiet hours.
- **Q4 Moderators:** only Org Admin (as in the PRD matrix), or can it be delegated to Managers or Counselors for each unit?
- **Q5 SMS cost:** should SMS be limited to certain events or roles, for each client? The preferences page would then show those cells as locked off, with "Not offered by your school".
- **Q6 Removed messages:** how long is a removed message's original text kept for audit, and who can see it?

## 11. Alignment with #612 and #613

What this spec chose where the three drafts differed (QA2's table on #613), and what it asks the others to change:

| Topic | This spec | Ask |
|---|---|---|
| Send key | Enter = new line, Ctrl+Enter sends | #612 follows (its Q13) |
| Block vs restriction | Block is the person's; restriction is the moderator's; the blocked side is never told | Already in #612/#613; #612 names the UI strings as in §1 |
| Moderation actions | Dismiss, Hide, Hide + Restrict (with end date), Lock | #612 drops `DeleteMessage`/`WarnSender` from the decision (Warn = the note); #613 keeps `DeleteMessage` API-only |
| Moderation route | `/moderation/messages` | none (this spec now follows #612) |
| Announcement targets | #613's kinds + `IncludeGuardians`; no Level, no role chips | #612 drops `includeSubtree` from the UI or #613 adds it |
| Announcement statuses | Draft, Scheduled, Published, Expired, Withdrawn; "Withdraw" | none |
| Announcement titles | Arabic and English titles, both required | #613 adds the second title (its Q19), as for every bilingual name |
| Announcement attachments | Up to 5 files (as #612) | #613 adds them, or #612 and this spec drop them for v1 |
| Message edit/delete | "Delete for me" only | #612 Q5 / #613 decide on delete-for-everyone |
| Messages badge | Conversations with unread messages | #612's `MessagingState` uses the same count |
| Rules matrix page | `/settings/messaging-rules` (§7a) | #612 adds the page task (QA2's #613 Must 7) |
| Nav | Communication group + Moderation and rules under Security and settings + the app-bar shortcut | #612 agrees |

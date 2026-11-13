# M6 design spec (draft): communication

**Status:** draft for FE1's M6 proposal. It isn't a plan task yet; the plan decides scope and order.
**PRD:** FR-NOT-01, FR-NOT-02, FR-NOT-03, FR-MSG-01, FR-MSG-02, FR-MSG-03, FR-MSG-04; X-01 (bilingual), X-07 (notifications), X-10 (accessibility, 360 px).
**Mockups:** `docs/design/mockups/m6.html`. It's static HTML in the Stitch tokens from `LmsTheme.cs` and `lms-stitch.css`. Open it with `?screen=inbox|thread|compose|announcements|announce-compose|notifications|prefs|moderation&lang=ar|en&theme=light|dark`.
**Screenshots:** `docs/screenshots/ux-m6/<screen>-<lang>-<theme>-<width>.png`. Each screen is shot at 1440 px (en light, ar dark) and at 360 px (ar light, en dark).

Everything in `docs/DESIGN.md` (the Stitch section and its deviations table) applies. This file only adds what M6 needs. The new classes in §8 go into `lms-stitch.css` in a small `lane:ux` PR before the M6 pages start.

---

## 1. Shared rules for M6

- **Rules decide what can be chosen; the UI explains them.** The messaging rules (FR-MSG-02) are enforced by the API. The UI shows what the rules allow and, where someone could reasonably expect more, says why it isn't there:
  - The recipient picker lists a near-miss as a **disabled option with its reason** (for example, a class the teacher doesn't teach). It never lists people the user can't know about.
  - In a read-only conversation, the composer becomes an info alert with the rule in plain words.
  - A blocked user sees the same read-only alert, worded for blocking.
- **Dates and times** follow DESIGN.md: `yyyy-MM-dd` and `HH:mm`, invariant, inside `<span dir="ltr">`. In lists, recent items use relative words: today shows the time, then "Yesterday" and the weekday name for this week, then the date.
- **Names and mixed scripts.** Every person or group name sits in its own `<bdi>` (`dir="auto"`), so an English name in an Arabic sentence ("Huda Al-Harbi sent you a message") keeps its order. Message text is shown with `dir="auto"` for each message.
- **Counted text in Arabic** uses the shared plural helper with the four keys from the M4b spec: "3 new messages", "24 people", "Read by 412 of 590".
- **Realtime.** New messages and notifications arrive over SignalR (FR-NOT-02):
  - The thread body is `role="log"` with `aria-live="polite"`, and only messages from other people are announced.
  - The unread counters (sidebar badge, bell) update silently.
  - The snackbar for a new message is shown only when the user isn't already in that conversation.
- **Disabled, not hidden** for actions: permissions decide whether an action exists, and state (rules, blocks, scheduling) disables it with a caption.
- **Icons:** Rounded only.
  - **Messages and groups:** Forum (Messages), EditSquare (new message), Groups (group), Person (a person).
  - **Announcements:** Campaign (announcement), PushPin (pinned), Schedule / ScheduleSend (scheduled).
  - **Moderation:** ShieldPerson (moderation), Flag (report), Block (block).
  - **Composer and channels:** AttachFile, Send (mirrored in RTL), Mail, Sms, PhoneIphone (push), NotificationsOff, Lock (set by the school).

## 2. Inbox and thread (FR-MSG-01, FR-MSG-02)

Mockups: `screen=inbox` (a one-to-one conversation) and `screen=thread` (a group conversation, the message menu, a removed message and a read-only composer).

1. **Route and nav.** The inbox is `/messages`, and a conversation is `/messages/{conversationId}`. The page gets a new **Communication** sidebar group with Messages, Announcements, Notifications and Moderation, in that order. Moderation is visible only to holders of the moderation permission. The Messages item has an unread badge.
2. **Layout.**
   - **At 900 px and wider:** one `SectionCard Flush` holds two panes (`lms-inbox`). The conversation list is 360 px wide and the thread fills the rest. The card fills the height under the page header, so the list and the thread scroll on their own and the composer stays visible.
   - **Below 900 px:** one pane at a time. `/messages` shows the list and `/messages/{id}` shows the thread, with a **Back to messages** icon button in the thread header (the arrow mirrors in RTL). Back uses browser history, so the list keeps its scroll and filter.
3. **List tools.** A search field (recessed input, placeholder "Search messages", searches names and message text) and a segmented control: All, Unread, Groups.
4. **Conversation row (`lms-conv`).**
   - **Content:** an avatar (initials, or the Groups icon for a group), the name (ellipsis), the time (end-aligned), and a one-line preview. In a group, the preview has the author first: "Sara: …", or "You: …" for the user's own messages.
   - **Unread:** the name and preview are bold, with a count badge in primary at the end. This is the only place a count badge is primary instead of error, because unread messages are normal, not alarming.
   - **Current row:** `aria-current="true"`, a primary tint and a 3 px bar on the inline-start edge.
   - **Accessibility:** each row is one link. Its accessible name is "name, n unread, time, preview".
5. **Thread header.** Avatar, name, and a caption: a person's role and context ("Guardian of Omar Saleh · 10-A"), or the group size ("Group · 24 people"). It ends with a More actions menu: Mute conversation, Members (for groups), and Leave group (for groups the user didn't create).
6. **Messages.**
   - **Bubbles:** the user's own messages sit at the inline end, tinted with 12 % primary. Other people's sit at the inline start, on the surface colour with a 1 px border. Bubbles are at most 560 px wide (88 % at 360 px). The corner nearest the sender is 4 px.
   - **Sender names:** in a group, the sender's name is shown above their first bubble in a run.
   - **Day separators:** a centred `lms-day` chip.
   - **Meta line:** the time, then, for the user's own messages, the state: "Sent" or "Seen" (with the DoneAll icon). Read receipts are optional (FR-MSG-01) and follow the client setting; when they're off, "Seen" never shows.
   - **Attachments:** a recessed row inside the bubble with the file-type icon, the name and the size. Images show a thumbnail at most 240 px wide, and open in a new tab.
   - **Removed messages:** `lms-msg-removed` shows a dashed outline and the italic text "This message was removed by a moderator." The original text isn't shown to anyone in the conversation.
7. **Message menu.** On hover or focus of a message, a More icon button appears (it stays visible on touch screens). Its menu has Copy text, then a divider, then **Report message** and **Block [name]** in the error colour, last. Report opens a small dialog: a reason (radio: Harassment, Spam, Safety concern, Other) and an optional note. Block confirms with what it does: "You won't get messages from Ahmad. They aren't told."
8. **Composer** (`lms-composer`, sticky at the bottom of the thread):
   - an Attach icon button, a multi-line recessed text field ("Write a message"), and a filled Send button with its label (the icon mirrors in RTL);
   - Enter adds a new line and Ctrl+Enter sends; the Send button is the documented way;
   - while sending, the button is disabled, and a failed message stays in the thread with "Not sent · Try again" in the error colour.
9. **Read-only conversation** (`lms-composer-off`): the composer is replaced by an info alert with the reason, for example "You can only read this conversation. Messaging rules at your school don't allow students to message other students."
10. **Empty states.** No conversations: "No messages yet", with New message as the action when the user may send. No search results: "No conversations match".

## 3. Compose and the recipient picker

Mockup: `screen=compose`.

1. **New message** opens a dialog (full screen below 600 px). It's a dialog, not a page, so the inbox stays in place behind it.
2. **To.** A recessed multi-select autocomplete (`MudAutocomplete` with chips). The results come from a server endpoint that applies the messaging rules, after 2 typed characters, debounced at 300 ms. They're grouped:
   - **Classes and courses:** "Physics 1 · 10-A — guardians" and "— students", each with its size at the end. They're only offered where the user teaches or manages, and the rules allow it.
   - **People:** an avatar, the name, and a caption with the role and context. Picked people get a check mark.
   - A **disabled option with its reason** for a near-miss the user would expect: "Physics 1 · 10-B — guardians · Not allowed: you don't teach 10-B". At most one such row, and never a person.
3. **Subject** (optional) becomes the group name when there are several recipients. Its help text says so.
4. **Message:** a recessed multi-line field, 5 lines high.
5. **Group note:** when more than one person is picked, an info alert says "Replies go to everyone in the group. To reply privately, people start a new message."
6. **Actions:** Attach a file (a text button, at the inline start), Cancel, and Send (filled). Sending opens the new conversation.
7. **Starting from elsewhere.** Starting a message from a person (for example, a Message button on a student's guardian card) opens the same dialog with that person already picked.

## 4. Announcements (FR-MSG-03)

Mockups: `screen=announcements` (the list) and `screen=announce-compose` (new or edit).

1. **Route.** `/announcements` lists the announcements for the user's audience. Course announcements from M4a stay on the class course's Announcements tab, and they also appear here with their course chip.
2. **Filters.** A segmented control: Current, Scheduled, Expired and Mine. Scheduled and Mine are shown only to people who can announce.
3. **Card** (`lms-ann`, at most 860 px wide):
   - **Header:** a section icon (PushPin when pinned, Schedule when scheduled, Campaign otherwise), then the title (h2) and status chips: **Pinned** (success, with a dot) and **Scheduled** (info, with a dot).
   - **Meta line:** the author or office, the date, and the "Until yyyy-MM-dd" expiry.
   - **Body:** the text, clamped to 4 lines, with "Read more".
   - **Audience chips:** one per target, each with its icon: Domain (organization), AccountTree (unit), Groups (class), MenuBook (course), Stairs (level), plus the roles.
   - **Read count:** "Read by 412 of 590", shown only to the author and to managers.
   - **Order:** pinned cards first (with a 35 % primary border), then newest first.
   - **More actions menu:** Edit, then Unpublish (error colour, last, with a confirmation).
4. **New announcement** dialog:
   - **Titles:** in Arabic and in English, side by side, each field in its own direction; the text field is below them.
   - **Audience** (a fieldset with the legend "Who should see it"):
     - a segmented scope control: Organization, Unit, Class, Course, Group, Role (the options are limited to what the user may target);
     - the targets as a multi-select with chips;
     - the roles in those targets as filter chips: Staff, Students, Guardians;
     - a live reach estimate in an info alert (`role="status"`): "About 590 people will see it".
   - **Publishing:** a segmented Now / Schedule control. Schedule reveals the date and time fields and the time-zone caption, worded as in the M4b spec.
   - **Toggle rows** (`lms-toggle-row`): Pin to the top, and Notify the audience. Each has a one-line help text.
   - **Expires on:** an optional date, with help text.
   - **Button:** Publish, or Schedule when scheduled. The label follows the publishing mode.

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
4. **Badge.** The bell's badge keeps the error colour and shows "99+" past 99. The sidebar's Messages badge counts unread conversations, not unread notifications.
5. **Notifications page.** The `/notifications` page keeps its table. It gains the same kind icons, and a filter segment: All, Unread.

## 6. Channel preferences (FR-NOT-01, FR-NOT-03)

Mockup: `screen=prefs`. This extends the existing `/notifications/preferences` page and the 2 × 2 phone grid from #590.

1. **Columns:** In-app, Email, SMS, Push.
   - Each column header has an icon, the channel name, and a caption with the address in use: the masked email, the masked phone, or "This device: on/off" for push.
   - In-app is always on: its switches are locked, with the Lock icon *before* the switch.
2. **Locked cells.** Events the school makes mandatory (for example, absence of my child, or a pinned announcement by email) show a locked switch with the Lock icon. A footer caption explains the icon: "Set by your school: can't be turned off."
3. **Push on this device.** When push isn't enabled for this browser:
   - a warning alert above the table offers **Turn on for this browser** (an outlined button) and explains that the browser will ask for permission;
   - if the browser permission was denied, the alert says how to allow it in the browser settings instead, with no button;
   - if the browser can't do push, the Push column shows "Not available on this browser" and its switches are disabled.
4. **No phone number.** SMS switches are disabled when there's no mobile number on file, with the caption "Add a mobile number in your profile", which links to the profile.
5. **Save.** A filled Save button in a footer bar. When the table is long, the footer becomes the `lms-save-bar` while there are unsaved changes.
6. **At 360 px:** each event is a block, with the event name first and then one row per channel: the label at the start, and the lock and switch at the end. These are simple rows, not a grid, so the switches line up.

## 7. Moderation queue (FR-MSG-04)

Mockup: `screen=moderation`.

1. **Route:** `/moderation`, behind the moderation permission (Org Admin per the PRD matrix; see question Q4).
2. **Stat tiles:** Open reports, New today, People blocked, Median time to decide. Below 600 px they're two per row, with the icon above the value (the #580 rule).
3. **Queue.** A table card with a segmented control (Open reports, Closed).
   - **Columns:** Reported message (a one-line excerpt), Reason, Reported by, When, Status. Status is a soft chip: New (info), In review (warning), Removed or Kept (neutral).
   - **Safety concern:** a "Safety concern" report shows its New chip in the error colour and sorts first.
   - **Selection:** the selected row is tinted. Below 600 px the rows stack as labelled cards (`DataLabel`).
4. **Detail panel.** At 1100 px and wider it's a 380 px card beside the table. Below that it opens as a dialog (full screen at 360 px). It contains:
   - the conversation name and size;
   - the **context**: the message before and after the reported one, read-only, with the reported message outlined in the warning colour and a Reported message chip;
   - the reporter's reason and note;
   - the sender line: name, role, class, and the number of earlier reports and removals;
   - the **decision**, a radio group of soft rows (`lms-toggle-row` with a radio):
     - Keep the message;
     - Remove the message;
     - Remove and block the sender, last, with its label in the error colour.

     Each option has one line explaining what happens.
   - an optional note to the people involved, and **Save decision** (filled).
5. **After a decision.**
   - **Who is told:** the reporter and the sender are notified (`evMod`), but the reporter never learns which action was taken against the sender. The reporter sees "reviewed".
   - **Audit:** every decision is recorded (X-02).
   - **The message:** removing it replaces the text for everyone with the removed state from §2.6.
6. **Unblocking.** It's done from the People blocked tile, which filters to a list with an **Unblock** action in each row.

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
- [ ] Nothing scrolls sideways at 360 px, and every table cell has a `DataLabel`. All 32 mockup shots were checked with `scrollWidth`.

## 10. Questions for the Team Lead (owner decisions)

- **Q1 Read receipts:** on or off by default for each client? Should students see "Seen" on staff messages? (The mockup shows "Seen" with receipts on.)
- **Q2 Guardian visibility:** may guardians read their child's conversations with teachers? The PRD doesn't say. If yes, a "Shared with guardians" caption goes in the thread header.
- **Q3 Report reasons:** is the list fixed (Harassment, Spam, Safety concern, Other), or set for each client?
- **Q4 Moderators:** only Org Admin (as in the PRD matrix), or can it be delegated to Managers or Counselors for each unit?
- **Q5 SMS cost:** should SMS be limited to certain events or roles, for each client? The preferences page would then show those cells as locked off, with "Not offered by your school".
- **Q6 Removed messages:** how long is a removed message's original text kept for audit, and who can see it?

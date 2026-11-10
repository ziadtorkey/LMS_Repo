# LMS Project Control dashboard

Live page for the product owner: https://claude.ai/artifact/UXmPzSpvxubvpF2w9LdLoN

It shows the current milestone, agents and their work, the roadmap (M0–M7), the
board for the current milestone, pull requests (CI + QA verdict) and the Lead's
blockers / decisions / recent merges. It reads five JSON snapshot files attached
to the dashboard. **The Team Lead refreshes them after every merge cycle** (and at
least once a day while work is active).

## Refresh procedure (Team Lead)

1. Make sure `docs/STATUS.md` is current (agents table, blockers, decisions, recently merged, last update) and pushed.
2. Generate the snapshot:
   ```
   python -I -X utf8 scripts/dashboard/snapshot.py
   ```
   Output goes to `.dashboard/` (git-ignored): `issues.json`, `pull_requests.json`,
   `milestones.json`, `agents.json`, `notes.json`.
3. Upload each file to the dashboard, one call per file:
   `Artifact` → `action: publish`, `url: https://claude.ai/artifact/UXmPzSpvxubvpF2w9LdLoN`, `asset: true`, `file_path: .dashboard/<name>.json`.
   Each upload returns a new `/_blob/<id>` url.
4. Read the datasets (`ArtifactData` `list` on collection `datasets`) to get each document's `version`, then one
   `ArtifactData` `batch` of `update` writes (with `if_version`) on `datasets/issues`, `datasets/pull_requests`,
   `datasets/milestones`, `datasets/agents`, `datasets/notes` setting:
   - `source.url` → the new `/_blob/<id>` for that file
   - `updated` → `{at: <UTC ISO now>, by: "Team Lead (snapshot script)"}`
   - `edit` → `{via: "claude", at: <UTC ISO now>}`
5. Delete the previous uploads (`Artifact` `action: delete`, `url` + `path: <old blob id>`) so stale copies don't pile up.
6. Read `data/users/me/check` and `data/users/me/runs` on the dashboard; if `check` is `failed`, report it to the owner.

Board status per issue comes from the "LMS v1" project's Status column; closed issues are Done and
issues labelled `blocked` show as Blocked. Keep the project board Status column accurate.

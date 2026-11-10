"""Collect a project snapshot for the LMS Project Control dashboard.

Reads GitHub (issues, PRs, milestones, the "LMS v1" project board) through the
`gh` CLI and the agents table / blockers / owner decisions from docs/STATUS.md,
then writes one JSON file of rows per dataset into the output directory:

    issues.json, pull_requests.json, milestones.json, agents.json, notes.json

Usage:  python -I scripts/dashboard/snapshot.py [--repo D:/AI/LMS] [--out D:/AI/LMS/.dashboard]
"""
from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
from datetime import datetime, timezone
from pathlib import Path

OWNER = "ziadtorkey"
REPO = "ziadtorkey/LMS_Repo"
PROJECT_NUMBER = 1
GH_FALLBACK = r"C:\Program Files\GitHub CLI\gh.exe"


def gh(*args: str) -> object:
    exe = shutil.which("gh") or GH_FALLBACK
    out = subprocess.run([exe, *args], capture_output=True, text=True, encoding="utf-8", check=True)
    return json.loads(out.stdout or "null")


def label_value(labels: list[str], prefix: str) -> str | None:
    for name in labels:
        if name.startswith(prefix):
            return name[len(prefix):]
    return None


def milestone_order(title: str | None) -> int | None:
    m = re.match(r"M(\d+)", title or "")
    return int(m.group(1)) if m else None


def board_status() -> dict[int, str]:
    """Issue number -> Status column on the project board (empty if unavailable)."""
    try:
        data = gh("project", "item-list", str(PROJECT_NUMBER), "--owner", OWNER, "--format", "json", "--limit", "500")
    except subprocess.CalledProcessError:
        return {}
    result: dict[int, str] = {}
    for item in (data or {}).get("items", []):
        content = item.get("content") or {}
        if content.get("type") == "Issue" and content.get("number"):
            result[int(content["number"])] = item.get("status") or ""
    return result


def closes_refs(body: str | None) -> list[int]:
    return [int(n) for n in re.findall(r"(?i)\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)\s+#(\d+)", body or "")]


def checks_state(rollup: list[dict] | None) -> str:
    if not rollup:
        return "none"
    states = {(c.get("conclusion") or c.get("state") or c.get("status") or "").upper() for c in rollup}
    if states & {"FAILURE", "ERROR", "CANCELLED", "TIMED_OUT", "ACTION_REQUIRED"}:
        return "failing"
    if states & {"PENDING", "IN_PROGRESS", "QUEUED", "EXPECTED", "", "WAITING"}:
        return "running"
    return "passing"


def collect_prs() -> list[dict]:
    prs = gh("pr", "list", "--repo", REPO, "--state", "all", "--limit", "200", "--json",
             "number,title,state,headRefName,createdAt,mergedAt,closedAt,labels,body,url,statusCheckRollup,isDraft")
    rows = []
    for p in prs or []:
        labels = [l["name"] for l in p.get("labels", [])]
        qa = "approved" if "qa:approved" in labels else "changes requested" if "qa:changes-requested" in labels else "pending"
        branch = p.get("headRefName") or ""
        lane = branch.split("/", 1)[0] if "/" in branch else ""
        rows.append({
            "number": p["number"],
            "title": p["title"],
            "state": p["state"].lower(),          # open | merged | closed
            "draft": bool(p.get("isDraft")),
            "lane": lane,
            "branch": branch,
            "qa": qa,
            "checks": checks_state(p.get("statusCheckRollup")),
            "closes": ",".join(str(n) for n in closes_refs(p.get("body"))),
            "created_at": p.get("createdAt"),
            "merged_at": p.get("mergedAt"),
            "url": p.get("url"),
        })
    return rows


def collect_issues(prs: list[dict]) -> list[dict]:
    issues = gh("issue", "list", "--repo", REPO, "--state", "all", "--limit", "500", "--json",
                "number,title,state,labels,milestone,createdAt,closedAt,url")
    board = board_status()
    open_pr_for: dict[int, int] = {}
    for pr in prs:
        if pr["state"] == "open":
            for n in filter(None, pr["closes"].split(",")):
                open_pr_for[int(n)] = pr["number"]
    rows = []
    for i in issues or []:
        labels = [l["name"] for l in i.get("labels", [])]
        state = i["state"].lower()
        status = board.get(i["number"], "")
        if state == "closed":
            status = "Done"
        elif "blocked" in labels:
            status = "Blocked"
        elif not status:
            status = "In Review" if i["number"] in open_pr_for else "Todo"
        ms = (i.get("milestone") or {}).get("title")
        rows.append({
            "number": i["number"],
            "title": i["title"],
            "lane": label_value(labels, "lane:") or "",
            "area": label_value(labels, "area:") or "",
            "kind": "bug" if "bug" in labels else "task",
            "severity": label_value(labels, "sev:") or "",
            "milestone": ms or "",
            "milestone_order": milestone_order(ms),
            "state": state,
            "status": status,
            "pr": open_pr_for.get(i["number"]),
            "needs_owner": "needs-owner" in labels,
            "created_at": i.get("createdAt"),
            "closed_at": i.get("closedAt"),
            "url": i.get("url"),
        })
    return rows


def collect_milestones() -> list[dict]:
    ms = gh("api", f"repos/{REPO}/milestones?state=all&per_page=100")
    rows = []
    for m in ms or []:
        rows.append({
            "milestone": m["title"],
            "order": milestone_order(m["title"]),
            "open": m["open_issues"],
            "closed": m["closed_issues"],
            "state": m["state"],
            "url": m.get("html_url"),
        })
    return sorted(rows, key=lambda r: (r["order"] is None, r["order"]))


def parse_status(path: Path) -> tuple[list[dict], list[dict]]:
    text = path.read_text(encoding="utf-8")
    sections: dict[str, list[str]] = {}
    current = "_top"
    for line in text.splitlines():
        if line.startswith("## "):
            current = line[3:].strip()
            sections[current] = []
        else:
            sections.setdefault(current, []).append(line)

    agents = []
    for line in sections.get("Agents", []):
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cells) < 5 or cells[0] in ("Agent", "") or set(cells[0]) <= {"-"}:
            continue
        session = re.search(r"\(([^)]+)\)", cells[2])
        agents.append({
            "agent": cells[0],
            "model": cells[1],
            "handle": session.group(1) if session else "",
            "current": re.sub(r"`", "", cells[3]),
            "state": cells[4],
        })

    notes = []
    for section in ("Blockers", "Decisions needed from the product owner", "Recently merged"):
        for line in sections.get(section, []):
            item = line.strip().lstrip("-*").strip()
            if item and item not in ("—", "-") and not item.lower().startswith("none"):
                notes.append({"section": section, "item": item})
    top = "\n".join(sections.get("_top", []))
    m = re.search(r"\*\*Last update:\*\*\s*(.+)", top)
    if m:
        notes.append({"section": "Last update", "item": m.group(1).strip()})
    return agents, notes


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", default=str(Path(__file__).resolve().parents[2]))
    ap.add_argument("--out", default=None)
    args = ap.parse_args()
    repo = Path(args.repo)
    out = Path(args.out) if args.out else repo / ".dashboard"
    out.mkdir(parents=True, exist_ok=True)

    prs = collect_prs()
    issues = collect_issues(prs)
    agents, notes = parse_status(repo / "docs" / "STATUS.md")
    files = {
        "issues.json": issues,
        "pull_requests.json": prs,
        "milestones.json": collect_milestones(),
        "agents.json": agents,
        "notes.json": notes,
    }
    for name, rows in files.items():
        (out / name).write_text(json.dumps(rows, ensure_ascii=False, indent=1), encoding="utf-8")
    stamp = datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")
    print(json.dumps({"out": str(out), "at": stamp, **{k: len(v) for k, v in files.items()}}))


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Validate the AirPods-Windows task tracking files.

Checks TODO.md against the grammar documented in AGENTS.md section 4 and cross-checks
PROGRESS.md. Standard library only; safe to run from any agent, on any OS.

Usage:
    python scripts/check_tasks.py          # or python3
    python scripts/check_tasks.py --quiet  # only errors/warnings

Exit code 0 = no errors, 1 = errors found, 2 = the files could not be read.
"""

from __future__ import annotations

import re
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TODO_PATH = ROOT / "TODO.md"
PROGRESS_PATH = ROOT / "PROGRESS.md"

STATUSES = {"todo", "doing", "blocked", "review", "done"}
PRIORITIES = ("P0", "P1", "P2", "P3")

TASK_RE = re.compile(
    r"^- \[(?P<box>[ xX])\] "
    r"(?P<id>APW-\d{3}) \| "
    r"(?P<prio>P[0-3]) \| "
    r"(?P<status>[a-z]+) \| "
    r"(?P<ms>M[0-5]) \| "
    r"(?P<rest>.+?)\s*$"
)
MILESTONE_RE = re.compile(r"^##\s+(?P<ms>M[0-5])\b")
ID_RE = re.compile(r"^APW-\d{3}$")


@dataclass
class Task:
    id: str
    checkbox: bool
    priority: str
    status: str
    milestone: str
    deps: list[str] = field(default_factory=list)
    owner: str | None = None
    title: str = ""
    ac: str = ""
    notes: str = ""
    blocked: str = ""
    handoff: str = ""
    line_no: int = 0


def read(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8")
    except OSError as exc:
        print(f"error: cannot read {path}: {exc}")
        sys.exit(2)


def parse_todo(text: str):
    tasks: list[Task] = []
    milestones: list[str] = []
    errors: list[str] = []
    current: Task | None = None

    for number, raw in enumerate(text.splitlines(), start=1):
        line = raw.rstrip()

        ms_match = MILESTONE_RE.match(line)
        if ms_match:
            milestones.append(ms_match.group("ms"))
            current = None
            continue

        task_match = TASK_RE.match(line)
        if task_match:
            parts = task_match.group("rest").split(" | ")
            if not parts[0].lower().startswith("deps:"):
                errors.append(f"TODO.md:{number}: expected 'deps:' segment, got {parts[0]!r}")
                continue
            deps_raw = parts[0][5:].strip()
            deps = [] if deps_raw.lower() in {"none", "-", ""} else [
                d.strip() for d in deps_raw.split(",") if d.strip()
            ]

            owner = None
            title_parts = parts[1:]
            if title_parts and title_parts[0].lower().startswith("owner:"):
                owner = title_parts[0][6:].strip() or None
                title_parts = title_parts[1:]

            current = Task(
                id=task_match.group("id"),
                checkbox=task_match.group("box").lower() == "x",
                priority=task_match.group("prio"),
                status=task_match.group("status"),
                milestone=task_match.group("ms"),
                deps=deps,
                owner=owner,
                title=" | ".join(title_parts).strip(),
                line_no=number,
            )
            tasks.append(current)
            continue

        if line.startswith("- ["):
            errors.append(f"TODO.md:{number}: line looks like a task but does not match the grammar")
            continue

        if current is not None:
            stripped = line.strip()
            lowered = stripped.lower()
            if lowered.startswith("- ac:"):
                current.ac = stripped[5:].strip()
            elif lowered.startswith("- notes:"):
                current.notes += (" " if current.notes else "") + stripped[8:].strip()
            elif lowered.startswith("- blocked:"):
                current.blocked += (" " if current.blocked else "") + stripped[10:].strip()
            elif lowered.startswith("- handoff:"):
                current.handoff += (" " if current.handoff else "") + stripped[10:].strip()

    return tasks, milestones, errors


def find_cycles(tasks: list[Task]) -> list[list[str]]:
    graph = {t.id: [d for d in t.deps if d] for t in tasks}
    state: dict[str, int] = {}
    stack: list[str] = []
    cycles: list[list[str]] = []
    seen: set[frozenset[str]] = set()

    def visit(node: str) -> None:
        state[node] = 1
        stack.append(node)
        for dep in graph.get(node, []):
            if dep not in graph:
                continue
            if state.get(dep) == 1:
                start = stack.index(dep)
                cycle = stack[start:] + [dep]
                key = frozenset(cycle)
                if key not in seen:
                    seen.add(key)
                    cycles.append(cycle)
            elif state.get(dep, 0) == 0:
                visit(dep)
        stack.pop()
        state[node] = 2

    for task in tasks:
        if state.get(task.id, 0) == 0:
            visit(task.id)
    return cycles


def validate(tasks: list[Task], milestones: list[str], parse_errors: list[str]):
    errors: list[str] = list(parse_errors)
    warnings: list[str] = []

    by_id: dict[str, Task] = {}
    for task in tasks:
        if task.id in by_id:
            errors.append(f"{task.id}: duplicate ID (lines {by_id[task.id].line_no} and {task.line_no})")
        by_id.setdefault(task.id, task)

    for task in tasks:
        where = f"{task.id} (TODO.md:{task.line_no})"

        if task.status not in STATUSES:
            errors.append(f"{where}: unknown status {task.status!r}")
            continue
        if task.priority not in PRIORITIES:
            errors.append(f"{where}: unknown priority {task.priority!r}")
        if task.milestone not in milestones:
            errors.append(f"{where}: milestone {task.milestone} has no '## {task.milestone}' heading")
        if task.checkbox != (task.status == "done"):
            errors.append(
                f"{where}: checkbox is {'[x]' if task.checkbox else '[ ]'} but status is {task.status!r}"
            )
        if not task.title:
            errors.append(f"{where}: empty title")
        elif len(task.title) > 120:
            warnings.append(f"{where}: title is {len(task.title)} chars — keep it under 120")
        if not task.ac:
            errors.append(f"{where}: missing '- AC:' acceptance criteria")
        elif len(task.ac) < 20:
            warnings.append(f"{where}: acceptance criteria look too short to be verifiable")
        if task.status in {"doing", "review"} and not task.owner:
            errors.append(f"{where}: status {task.status!r} requires an 'owner:' segment")
        if task.status == "blocked" and not task.blocked:
            warnings.append(f"{where}: blocked without a '- Blocked:' reason")
        if task.handoff and task.status == "done":
            warnings.append(f"{where}: has a handoff note but is marked done")

        for dep in task.deps:
            if not ID_RE.match(dep):
                errors.append(f"{where}: malformed dependency {dep!r}")
                continue
            if dep == task.id:
                errors.append(f"{where}: depends on itself")
                continue
            other = by_id.get(dep)
            if other is None:
                errors.append(f"{where}: dependency {dep} does not exist")
                continue
            if task.status in {"doing", "review", "done"} and other.status != "done":
                errors.append(
                    f"{where}: is {task.status!r} but dependency {dep} is {other.status!r}"
                )
            elif task.status == "todo" and other.status == "blocked":
                warnings.append(f"{where}: dependency {dep} is blocked — this task will stall")

    for cycle in find_cycles(tasks):
        errors.append("dependency cycle: " + " → ".join(cycle))

    return errors, warnings


def check_progress(tasks: list[Task]) -> list[str]:
    warnings: list[str] = []
    if not PROGRESS_PATH.exists():
        return ["PROGRESS.md is missing"]

    text = read(PROGRESS_PATH)
    doing = [t for t in tasks if t.status in {"doing", "review"}]
    current_line = next(
        (ln for ln in text.splitlines() if ln.strip().lower().startswith("**current task:**")),
        "",
    )

    if doing and "none" in current_line.lower():
        warnings.append(
            "PROGRESS.md says the current task is none, but "
            + ", ".join(t.id for t in doing)
            + " is in progress"
        )
    if not doing and current_line and "none" not in current_line.lower():
        warnings.append("PROGRESS.md names a current task, but no task is 'doing' or 'review'")

    if "**last updated:**" not in text.lower():
        warnings.append("PROGRESS.md has no '**Last updated:**' line")

    return warnings


def main() -> int:
    quiet = "--quiet" in sys.argv[1:]

    if not TODO_PATH.exists():
        print("error: TODO.md not found")
        return 2

    tasks, milestones, parse_errors = parse_todo(read(TODO_PATH))
    errors, warnings = validate(tasks, milestones, parse_errors)
    warnings.extend(check_progress(tasks))

    status_counts = Counter(t.status for t in tasks)
    priority_counts = Counter(t.priority for t in tasks)
    milestone_counts = Counter(t.milestone for t in tasks)

    if not quiet:
        print(f"TODO.md — {len(tasks)} tasks in {len(milestones)} milestones")
        print("  status    " + " · ".join(f"{k} {status_counts.get(k, 0)}" for k in
                                          ["done", "doing", "review", "blocked", "todo"]))
        print("  priority  " + " · ".join(f"{p} {priority_counts.get(p, 0)}" for p in PRIORITIES))
        print("  per mile  " + " · ".join(f"{m} {milestone_counts.get(m, 0)}" for m in milestones))

    for warning in warnings:
        print(f"warning: {warning}")
    for error in errors:
        print(f"ERROR: {error}")

    if errors:
        print(f"\n{len(errors)} error(s), {len(warnings)} warning(s)")
        return 1

    if not quiet:
        print(f"\nOK — grammar valid, dependencies resolved, PROGRESS.md consistent "
              f"({len(warnings)} warning(s))")
    return 0


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Checks the conventions in openspec/config.yaml that OpenSpec itself cannot check.

`openspec validate` checks that a change is structurally well formed: the artifacts
exist, the delta parses, scenarios use four hashtags. It knows nothing about the
rules this project layers on top in config.yaml, because those are prose addressed
to whoever is writing the change. Prose is remembered or it is not — this file is
the part that does not depend on remembering.

Every rule here mirrors one in config.yaml. When a rule changes there, change it
here too, or delete it here and accept that it is advisory.
"""
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CHANGES = ROOT / "openspec" / "changes"
DECISIONS = ROOT / "docs" / "architecture" / "decisions"

failures: list[str] = []


def fail(where: str, what: str) -> None:
    failures.append(f"{where}: {what}")


def schema_of(change: Path) -> str:
    """The schema a change was created under, from its own .openspec.yaml.

    Rules are checked against the schema the change actually uses, not the one the
    project defaults to today. A change written before a rule existed is not in
    breach of it, and rewriting history to satisfy a new rule teaches nothing.
    """
    marker = change / ".openspec.yaml"
    if not marker.exists():
        return ""
    match = re.search(r"^schema:\s*(\S+)", marker.read_text(), re.M)
    return match.group(1) if match else ""


def check_change(change: Path) -> None:
    name = f"openspec/changes/{change.name}"
    schema = schema_of(change)

    proposal = change / "proposal.md"
    if proposal.exists():
        text = proposal.read_text()
        if "## Non-goals" not in text:
            fail(f"{name}/proposal.md", 'missing a "## Non-goals" section (rules.proposal)')

    design = change / "design.md"
    if design.exists() and schema == "intent-driven-chatapp":
        text = design.read_text()
        if "```mermaid" not in text:
            fail(
                f"{name}/design.md",
                "no mermaid diagram — rules.design requires the c4-diagrams skill to be "
                "invoked and its diagrams embedded",
            )

    adr = change / "adr.md"
    if adr.exists():
        text = adr.read_text()
        for heading in ("## In-Force ADRs Reviewed", "## New Durable ADRs Created"):
            if heading not in text:
                fail(f"{name}/adr.md", f'missing "{heading}"')
        if "YYYY-MM-DD" in text:
            fail(f"{name}/adr.md", "review date left as the template placeholder")

    tasks = change / "tasks.md"
    if tasks.exists() and not re.search(r"^- \[[ x]\] ", tasks.read_text(), re.M):
        fail(f"{name}/tasks.md", "no `- [ ]` checkboxes, so apply cannot track progress")


def check_adr_immutability() -> None:
    """ADRs are immutable once accepted: supersede, never edit.

    Only meaningful against a base branch, so this is skipped when there is nothing
    to compare with (a shallow clone, or main itself).
    """
    if not DECISIONS.exists():
        return
    base = "origin/main"
    try:
        subprocess.run(["git", "rev-parse", "--verify", base], cwd=ROOT,
                       capture_output=True, check=True)
    except subprocess.CalledProcessError:
        return

    diff = subprocess.run(
        ["git", "diff", "--name-status", f"{base}...HEAD", "--",
         "docs/architecture/decisions/"],
        cwd=ROOT, capture_output=True, text=True,
    )
    for line in diff.stdout.splitlines():
        status, _, path = line.partition("\t")
        if status.startswith(("M", "D", "R")):
            fail(path.strip(), "an accepted ADR was modified or deleted — record a new ADR "
                               "that supersedes it instead")


def main() -> int:
    if CHANGES.exists():
        for change in sorted(CHANGES.iterdir()):
            if change.is_dir() and change.name != "archive":
                check_change(change)

    check_adr_immutability()

    if failures:
        print("Change conventions not met:\n")
        for failure in failures:
            print(f"  - {failure}")
        print("\nThese mirror the rules in openspec/config.yaml.")
        return 1

    print("Change conventions OK.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

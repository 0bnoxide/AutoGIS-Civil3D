#!/usr/bin/env python3
"""PostToolUse feedback hook, shared by both harnesses.

The repo wired only SessionStart and PreToolUse; there was no PostToolUse
feedback. This tightens two dev loops:

  * after an edit, run the touched tool's tests and report ONLY failures
  * after ``git push``, surface the open PR URL for the pushed branch

Wired identically for Claude (``.claude/settings.json``) and Codex
(``.codex/hooks.json``) — one script on a neutral path, both consuming the
``hookSpecificOutput.additionalContext`` envelope that coordination.py already
emits for PreToolUse across both harnesses. Root is derived per session with
``git rev-parse --show-toplevel``; no path literals.

Never blocks and never raises past :func:`main`: a hook that disrupts the
session is worse than one that stays quiet, so every fallible step is guarded
and the process exits 0 with silence when it has nothing useful to say.

Ports P4 of ``docs/superpowers/plans/2026-08-07-port-backlog-verified-gaps.md``.
"""

from __future__ import annotations

import json
import os
import re
import subprocess
import sys
from typing import Callable, Optional

# A Runner maps an argv list to (returncode, combined-output). Injected so the
# self-check drives the logic without running tests, dotnet, or gh for real.
Runner = Callable[[list], "tuple[int, str]"]

EDIT_TOOLS = ("Edit", "Write", "MultiEdit")
SHELL_TOOLS = ("Bash", "PowerShell")

# Opt-in marker for the slow .NET leg. `dotnet test` builds first and that
# build dominates edit latency, so it stays off unless the developer asks.
# ponytail: touched-project scope only (nearest .csproj, or its <Name>.Tests
# by convention); .NET leg marker-gated to bound latency. Widen to dependent
# or full-suite feedback if a cross-project break slips through.
DOTNET_MARKER = "AUTOGIS_HOOK_DOTNET"

_MAX_LINES = 40


def _is_git_push(command: str) -> bool:
    # A real `git … push` at the head of some command segment — not the words
    # "git push" buried inside an echo or a commit message. Split on shell
    # separators, then require the segment to start with `git` and carry `push`.
    for segment in re.split(r"[|&;\n]+", command or ""):
        tokens = segment.split()
        if len(tokens) >= 2 and tokens[0] == "git" and "push" in tokens[1:]:
            return True
    return False


def _tail(text: str, limit: int = _MAX_LINES) -> str:
    lines = (text or "").strip().splitlines()
    return "\n".join(lines[-limit:])


def _real_run(argv: list, cwd: str) -> tuple:
    proc = subprocess.run(
        argv, cwd=cwd, capture_output=True, text=True,
        encoding="utf-8", errors="replace",
    )
    return proc.returncode, (proc.stdout or "") + (proc.stderr or "")


def _rel(path: str, root: str) -> Optional[str]:
    try:
        rel = os.path.relpath(os.path.abspath(path), root)
    except ValueError:
        return None  # different drive on Windows
    if rel.startswith(".."):
        return None  # outside the repo
    return rel.replace("\\", "/")


def _python_tool_dir(rel: str) -> Optional[str]:
    parts = rel.split("/")
    if len(parts) >= 2 and parts[0] == "tools" and rel.endswith(".py"):
        return f"tools/{parts[1]}"
    return None


def _python_feedback(rel: str, root: str, run: Runner) -> Optional[str]:
    tool_dir = _python_tool_dir(rel)
    if tool_dir is None:
        return None
    if not os.path.isdir(os.path.join(root, tool_dir, "tests")):
        return None
    rc, out = run(["python", "-m", "unittest", "discover",
                   "-s", f"{tool_dir}/tests"])
    if rc == 0:
        return None
    return f"Tests for {tool_dir} FAILED after editing {rel}:\n{_tail(out)}"


def _nearest_csproj(rel: str, root: str) -> Optional[str]:
    directory = os.path.dirname(rel)
    while True:
        abs_dir = os.path.join(root, directory) if directory else root
        if os.path.isdir(abs_dir):
            for name in sorted(os.listdir(abs_dir)):
                if name.endswith(".csproj"):
                    joined = f"{directory}/{name}" if directory else name
                    return joined.replace("\\", "/")
        if not directory:
            return None
        directory = os.path.dirname(directory)


def _test_csproj(csproj: str, root: str) -> Optional[str]:
    name = os.path.basename(csproj)
    if name.endswith(".Tests.csproj"):
        return csproj
    project = name[: -len(".csproj")]
    candidate = f"tests/{project}.Tests/{project}.Tests.csproj"
    return candidate if os.path.isfile(os.path.join(root, candidate)) else None


def _dotnet_feedback(rel: str, root: str, env: dict, run: Runner) -> Optional[str]:
    if not rel.endswith(".cs") or not env.get(DOTNET_MARKER):
        return None  # slow leg is opt-in
    csproj = _nearest_csproj(rel, root)
    if csproj is None:
        return None
    test_csproj = _test_csproj(csproj, root)
    if test_csproj is None:
        return None
    rc, out = run(["dotnet", "test", test_csproj, "-c", "Release", "--nologo"])
    if rc == 0:
        return None
    return f".NET tests ({test_csproj}) FAILED after editing {rel}:\n{_tail(out)}"


def _push_feedback(command: str, run: Runner) -> Optional[str]:
    if not _is_git_push(command):
        return None
    rc, out = run(["gh", "pr", "view", "--json", "url", "-q", ".url"])
    url = out.strip().splitlines()[0] if out.strip() else ""
    if rc != 0 or not url:
        return None  # no PR yet (e.g. pushed before opening one): stay quiet
    return f"Pushed. Open PR: {url}"


def handle(payload: dict, root: str, env: dict, run: Runner) -> Optional[str]:
    tool = payload.get("tool_name", "")
    tool_input = payload.get("tool_input") or {}
    if tool in EDIT_TOOLS:
        path = tool_input.get("file_path")
        if not path:
            return None
        rel = _rel(path, root)
        if rel is None:
            return None
        return (_python_feedback(rel, root, run)
                or _dotnet_feedback(rel, root, env, run))
    if tool in SHELL_TOOLS:
        return _push_feedback(tool_input.get("command", ""), run)
    return None


def _git_toplevel(cwd: str) -> str:
    try:
        proc = subprocess.run(
            ["git", "rev-parse", "--show-toplevel"], cwd=cwd,
            capture_output=True, text=True, encoding="utf-8", errors="replace",
        )
    except OSError:
        return ""
    return proc.stdout.strip() if proc.returncode == 0 else ""


def main(argv: list | None = None) -> int:
    try:
        payload = json.loads(sys.stdin.read() or "{}")
    except (json.JSONDecodeError, ValueError):
        return 0
    cwd = payload.get("cwd") or os.getcwd()
    root = _git_toplevel(cwd)
    if not root:
        return 0
    try:
        context = handle(payload, root, os.environ, lambda a: _real_run(a, root))
    except Exception:
        return 0  # a feedback hook must never disrupt the session
    if context:
        print(json.dumps({"hookSpecificOutput": {
            "hookEventName": "PostToolUse",
            "additionalContext": context,
        }}))
    return 0


if __name__ == "__main__":
    sys.exit(main())

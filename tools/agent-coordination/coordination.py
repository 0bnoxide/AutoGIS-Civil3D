#!/usr/bin/env python3
"""AutoGIS-Civil3D agent coordination.

One deep module (accepted architecture, ADR-0002): stateless main protection,
explicit-release claims, init/doctor/status/check/sync-main, ADR-number
allocation, and Git/harness hook entrypoints. Python 3 standard library only;
repository-development tooling, never a product dependency.

Exit contract (stable, adapters depend on it):
  0  allow / success
  1  deny by policy
  2  misuse or bad invocation
  3  operational failure (unreadable registry, missing git, corrupt state)

Two invariants hold everywhere:
- The main rule is STATELESS: it never reads the registry, so registry
  corruption, absence, or lock contention can never turn a main write into
  an allow.
- Claims never expire and are never reaped automatically. `doctor` reports
  stale-suspect claims; only an explicit `release --force <id> --reason ...`
  clears an orphan.
"""

from __future__ import annotations

import argparse
import datetime as _dt
import fnmatch
import json
import os
import re
import shlex
import socket
import subprocess
import sys
import time
import uuid

MAIN_BRANCH = "main"
STATE_DIR = ".agent-state"
REGISTRY_NAME = "claims.json"
LOCK_SUFFIX = ".lock"
STALE_SUSPECT_HOURS = 24
CLAIM_KINDS = ("branch", "worktree", "file_glob", "adr")

ALLOW, DENY, MISUSE, OPFAIL = 0, 1, 2, 3


# --------------------------------------------------------------------------
# Git and repository discovery
# --------------------------------------------------------------------------

def _git(args, cwd=None):
    try:
        proc = subprocess.run(
            ["git", *args], cwd=cwd, capture_output=True, text=True, timeout=30
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        return 3, "", str(exc)
    return proc.returncode, proc.stdout.strip(), proc.stderr.strip()


def _norm(path):
    """Resolve symlinks/reparse points and case, for comparison only."""
    try:
        return os.path.normcase(os.path.realpath(os.path.abspath(path)))
    except OSError:
        return os.path.normcase(os.path.abspath(path))


class Repo:
    """Discovered repository context for a directory."""

    def __init__(self, worktree_root, primary_root, branch):
        self.worktree_root = worktree_root      # root of the tree cwd is in
        self.primary_root = primary_root        # root of the primary checkout
        self.branch = branch                    # branch checked out in cwd tree

    @property
    def registry_path(self):
        return os.path.join(self.primary_root, STATE_DIR, REGISTRY_NAME)


def discover(cwd=None):
    """Return Repo for cwd, or None when cwd is not inside a git repository.

    The primary root comes from --git-common-dir so every linked worktree
    resolves to the same registry.
    """
    cwd = cwd or os.getcwd()
    rc, top, _ = _git(["rev-parse", "--show-toplevel"], cwd=cwd)
    if rc != 0 or not top:
        return None
    rc, common, _ = _git(["rev-parse", "--git-common-dir"], cwd=cwd)
    if rc != 0 or not common:
        return None
    if not os.path.isabs(common):
        common = os.path.join(top, common)
    primary = os.path.dirname(_norm(common))
    rc, branch, _ = _git(["branch", "--show-current"], cwd=cwd)
    if rc != 0:
        branch = ""
    return Repo(_norm(top), primary, branch)


def branch_of_tree(tree_root):
    rc, branch, _ = _git(["branch", "--show-current"], cwd=tree_root)
    return branch if rc == 0 else ""


def tree_containing(path):
    """Return the worktree root governing `path`, or None if outside a repo.

    Walks up to the nearest EXISTING ancestor first: a write that creates a
    new directory under a governed tree must still resolve to that tree, or
    `mkdir new && echo x > new/file` on main would escape the rule.
    """
    probe = path if os.path.isdir(path) else os.path.dirname(path) or "."
    probe = os.path.abspath(probe)
    while probe and not os.path.isdir(probe):
        parent = os.path.dirname(probe)
        if parent == probe:
            return None
        probe = parent
    rc, top, _ = _git(["rev-parse", "--show-toplevel"], cwd=probe)
    if rc != 0 or not top:
        return None
    return _norm(top)


# --------------------------------------------------------------------------
# Stateless main rule — never reads the registry
# --------------------------------------------------------------------------

GIT_MUTATORS = {
    "commit", "merge", "rebase", "cherry-pick", "revert", "reset", "am",
}
REDIRECT_RE = re.compile(r"(?<![<>])>{1,2}\s*([^\s|;&<>]+)")


def deny_reason_for_target(target_path, repo_hint=None):
    """Deny when a file write resolves into a tree with main checked out."""
    tree = tree_containing(target_path)
    if tree is None:
        return None  # outside any repository: not governed
    if repo_hint is not None and tree != repo_hint.worktree_root \
            and tree != repo_hint.primary_root:
        # A different repository entirely; only govern this one.
        this_repo = discover(tree)
        if this_repo is None or _norm(this_repo.primary_root) != _norm(repo_hint.primary_root):
            return None
    if branch_of_tree(tree) == MAIN_BRANCH:
        return (f"'{target_path}' resolves into a checkout of '{MAIN_BRANCH}' "
                f"({tree}). main is read-only: branch first, or use sync-main.")
    return None


# Git global options that consume a following argument. Without skipping
# these, `git -C <path> reset` reads the path as the subcommand and the
# mutator check silently allows it — and reset/rebase have no pre-commit
# backstop.
_GIT_GLOBAL_WITH_ARG = {"-C", "-c", "--git-dir", "--work-tree", "--namespace",
                        "--exec-path", "--super-prefix"}


def _git_subcommand(argv):
    """Return (subcommand, index) skipping git's global options."""
    i = 1
    while i < len(argv):
        tok = argv[i]
        if tok in _GIT_GLOBAL_WITH_ARG:
            i += 2
            continue
        if tok.startswith("--") and "=" in tok or tok.startswith("-"):
            i += 1
            continue
        return tok, i
    return "", len(argv)


def deny_reason_for_git_argv(argv, cwd):
    """Deny git commands that mutate main or push to remote main."""
    if not argv or argv[0] != "git":
        return None
    workdir = cwd
    for i, tok in enumerate(argv):
        if tok == "-C" and i + 1 < len(argv):
            workdir = os.path.join(cwd or ".", argv[i + 1])
    sub, sub_index = _git_subcommand(argv)
    if sub == "push":
        for tok in argv[sub_index + 1:]:
            ref = tok.split(":", 1)[1] if ":" in tok else tok
            ref = ref.lstrip("+")  # force-push shorthand +main
            if ref in (MAIN_BRANCH, f"refs/heads/{MAIN_BRANCH}"):
                return (f"push targets remote '{MAIN_BRANCH}'. Integration "
                        "goes through pull requests only.")
        return None
    if sub in GIT_MUTATORS:
        tree = tree_containing(workdir or ".")
        if tree and branch_of_tree(tree) == MAIN_BRANCH:
            return (f"git {sub} on '{MAIN_BRANCH}' is denied. main is "
                    "read-only: branch first, or use sync-main.")
    return None


def deny_reason_for_shell(command, cwd, repo_hint=None):
    """Deny shell commands that write files on main or run git mutators there.

    ponytail: tokenizes each `&&`/`;`-separated segment and checks git
    commands plus common write forms (>, >>, tee, sed -i, dd of=, truncate).
    A full shell parser is deferred until a bypass is demonstrated.
    """
    effective_cwd = cwd  # `cd` in one segment moves the later segments
    for segment in re.split(r"&&|\|\||;|\n", command):
        segment = segment.strip()
        if not segment:
            continue
        targets = []
        for match in REDIRECT_RE.finditer(segment):
            targets.append(match.group(1))
        # Writers can sit in any pipeline stage (`producer | tee file`), so
        # every stage's argv is inspected, not just the first.
        for stage in segment.split("|"):
            try:
                argv = shlex.split(stage.strip(), posix=True)
            except ValueError:
                argv = stage.split()
            if not argv:
                continue
            if argv[0] == "cd" and len(argv) > 1:
                effective_cwd = argv[1] if os.path.isabs(argv[1]) \
                    else os.path.join(effective_cwd or ".", argv[1])
                continue
            reason = deny_reason_for_git_argv(argv, effective_cwd)
            if reason:
                return reason
            if argv[0] == "tee":
                targets += [a for a in argv[1:] if not a.startswith("-")]
            if argv[0] == "sed" and any(a.startswith("-i") for a in argv[1:]):
                targets += [a for a in argv[1:] if not a.startswith("-")][-1:]
            if argv[0] == "dd":
                targets += [a[3:] for a in argv if a.startswith("of=")]
            if argv[0] == "truncate":
                targets += [a for a in argv[1:] if not a.startswith("-")]
        for target in targets:
            resolved = target if os.path.isabs(target) \
                else os.path.join(effective_cwd or ".", target)
            reason = deny_reason_for_target(resolved, repo_hint)
            if reason:
                return reason
    return None


# --------------------------------------------------------------------------
# Claim registry — locked, atomic, explicit-release
# --------------------------------------------------------------------------

class RegistryError(Exception):
    """Registry unreadable or lock not acquirable: operational failure."""


def _now():
    return _dt.datetime.now(_dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _empty_registry():
    return {"claims": [], "audit": []}


def _load(path):
    if not os.path.exists(path):
        return _empty_registry()
    try:
        with open(path, encoding="utf-8") as fh:
            data = json.load(fh)
    except (OSError, json.JSONDecodeError) as exc:
        raise RegistryError(
            f"claim registry unreadable: {path}: {exc}. New claims and "
            "claim-dependent writes are blocked. Repair: quarantine the file "
            "(rename, never delete) with owner authorization, then run init."
        )
    if not isinstance(data, dict) or "claims" not in data:
        raise RegistryError(
            f"claim registry malformed: {path}. Quarantine and re-init "
            "with owner authorization."
        )
    return data


class _Lock:
    """O_EXCL sibling lockfile. Never stolen: a stuck lock is reported by
    doctor and removed manually — automatic reaping is deferred hardening."""

    def __init__(self, path, timeout=10.0):
        self.lock_path = path + LOCK_SUFFIX
        self.timeout = timeout
        self.fd = None

    def __enter__(self):
        deadline = time.monotonic() + self.timeout
        os.makedirs(os.path.dirname(self.lock_path), exist_ok=True)
        while True:
            try:
                self.fd = os.open(
                    self.lock_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY
                )
                os.write(self.fd, f"{os.getpid()}@{socket.gethostname()} {_now()}".encode())
                return self
            except FileExistsError:
                if time.monotonic() >= deadline:
                    raise RegistryError(
                        f"registry lock held: {self.lock_path}. If the holder "
                        "is dead, remove the lock file manually (doctor "
                        "reports it); automatic reaping is deliberately absent."
                    )
                time.sleep(0.1)

    def __exit__(self, *exc):
        if self.fd is not None:
            os.close(self.fd)
        try:
            os.remove(self.lock_path)
        except OSError:
            pass
        return False


def _save(path, data):
    tmp = f"{path}.{os.getpid()}.{uuid.uuid4().hex[:8]}.tmp"
    with open(tmp, "w", encoding="utf-8") as fh:
        json.dump(data, fh, indent=2, sort_keys=True)
        fh.write("\n")
    for attempt in range(5):
        try:
            os.replace(tmp, path)
            return
        except PermissionError:
            # Windows: destination transiently open by a reader.
            time.sleep(0.05 * (attempt + 1))
    os.replace(tmp, path)


def mutate_registry(repo, fn):
    """Lock, reread, apply fn(data) -> result, atomically replace."""
    path = repo.registry_path
    with _Lock(path):
        data = _load(path)
        result = fn(data)
        _save(path, data)
    return result


def _same_value(kind, a, b):
    if kind in ("worktree",):
        return _norm(a) == _norm(b)
    return a == b


def claim(repo, session, kind, value, harness=""):
    if kind not in CLAIM_KINDS:
        raise ValueError(f"unknown claim kind '{kind}'")

    def apply(data):
        if kind == "adr":
            return _allocate_adr(repo, data, session, harness)
        for existing in data["claims"]:
            if existing["kind"] != kind or existing["session"] == session:
                continue
            if _same_value(kind, existing["value"], value):
                return {"rejected": existing}
            # ponytail: overlap heuristic for globs — either pattern matching
            # the other's literal text counts as a conflict ("src/*" vs
            # "src/x/*"). Precise glob-intersection is deferred hardening.
            if kind == "file_glob" and (
                    fnmatch.fnmatch(value, existing["value"])
                    or fnmatch.fnmatch(existing["value"], value)):
                return {"rejected": existing}
        record = {
            "id": uuid.uuid4().hex[:12],
            "session": session,
            "harness": harness,
            "pid": os.getpid(),
            "host": socket.gethostname(),
            "kind": kind,
            "value": value,
            "created_utc": _now(),
        }
        data["claims"].append(record)
        return {"claimed": record}

    return mutate_registry(repo, apply)


def _allocate_adr(repo, data, session, harness):
    """ADR numbers are tickets: allocated atomically, never reissued.

    The floor is max(existing docs/adr/NNNN-*.md, every prior allocation) —
    an allocation that never becomes a file leaves a permanent gap.
    """
    top = 0
    adr_dir = os.path.join(repo.primary_root, "docs", "adr")
    if os.path.isdir(adr_dir):
        for name in os.listdir(adr_dir):
            match = re.match(r"(\d{4})-", name)
            if match:
                top = max(top, int(match.group(1)))
    for existing in data["claims"]:
        if existing["kind"] == "adr":
            top = max(top, int(existing["value"]))
    number = f"{top + 1:04d}"
    record = {
        "id": uuid.uuid4().hex[:12],
        "session": session,
        "harness": harness,
        "pid": os.getpid(),
        "host": socket.gethostname(),
        "kind": "adr",
        "value": number,
        "created_utc": _now(),
    }
    data["claims"].append(record)
    return {"claimed": record}


def release(repo, claim_id, session=None, force=False, reason=""):
    def apply(data):
        for i, existing in enumerate(data["claims"]):
            if existing["id"] != claim_id:
                continue
            if existing["kind"] == "adr":
                return {"error": "ADR numbers are consumed, never returned"}
            if not force and not session:
                return {"error": "release requires --session (ownership "
                                 "proof); orphan recovery is --force --reason"}
            if not force and existing["session"] != session:
                return {"contested": existing}
            if force and not reason:
                return {"error": "release --force requires --reason"}
            removed = data["claims"].pop(i)
            if force:
                data["audit"].append({
                    "action": "release --force",
                    "claim": removed,
                    "reason": reason,
                    "at": _now(),
                })
            return {"released": removed}
        return {"error": f"no claim with id {claim_id}"}

    return mutate_registry(repo, apply)


def list_claims(repo):
    return _load(repo.registry_path)["claims"]


# --------------------------------------------------------------------------
# Commands
# --------------------------------------------------------------------------

def cmd_init(repo):
    actions = []
    # Hooks resolve per-worktree (core.hooksPath is relative), so verify the
    # tree init runs from. Before the hooks land on main, the primary tree
    # lacks them and git silently skips — an advisory, not a failure.
    hooks_dir = os.path.join(repo.worktree_root, ".githooks")
    for hook in ("pre-commit", "pre-push"):
        if not os.path.exists(os.path.join(hooks_dir, hook)):
            print(f"init: missing {hooks_dir}/{hook}", file=sys.stderr)
            return OPFAIL
    if repo.primary_root != repo.worktree_root and not os.path.exists(
            os.path.join(repo.primary_root, ".githooks", "pre-commit")):
        print("init: note — the primary tree has no .githooks yet; its "
              "protection begins when they merge to main")
    rc, current, _ = _git(["config", "core.hooksPath"], cwd=repo.primary_root)
    if current != ".githooks":
        rc, _, err = _git(
            ["config", "core.hooksPath", ".githooks"], cwd=repo.primary_root
        )
        if rc != 0:
            print(f"init: git config failed: {err}", file=sys.stderr)
            return OPFAIL
        actions.append("set core.hooksPath=.githooks")
    state_dir = os.path.join(repo.primary_root, STATE_DIR)
    if not os.path.isdir(state_dir):
        os.makedirs(state_dir, exist_ok=True)
        actions.append(f"created {STATE_DIR}/")
    if not os.path.exists(repo.registry_path):
        _save(repo.registry_path, _empty_registry())
        actions.append(f"created {STATE_DIR}/{REGISTRY_NAME}")
    print("init: " + ("; ".join(actions) if actions else "already initialized"))
    return ALLOW


def cmd_doctor(repo):
    findings = []
    rc, hooks_path, _ = _git(["config", "core.hooksPath"], cwd=repo.primary_root)
    if hooks_path != ".githooks":
        findings.append("core.hooksPath is not .githooks — run init")
    for hook in ("pre-commit", "pre-push"):
        if not os.path.exists(os.path.join(repo.worktree_root, ".githooks", hook)):
            findings.append(f".githooks/{hook} missing in this worktree")
    lock = repo.registry_path + LOCK_SUFFIX
    if os.path.exists(lock):
        findings.append(f"registry lock present: {lock} — verify the holder "
                        "is alive before removing manually")
    try:
        claims = list_claims(repo)
        now = _dt.datetime.now(_dt.timezone.utc)
        for record in claims:
            created = _dt.datetime.strptime(
                record["created_utc"], "%Y-%m-%dT%H:%M:%SZ"
            ).replace(tzinfo=_dt.timezone.utc)
            age_h = (now - created).total_seconds() / 3600
            if record["kind"] != "adr" and age_h > STALE_SUSPECT_HOURS:
                findings.append(
                    f"stale-suspect claim {record['id']} ({record['kind']}="
                    f"{record['value']}, session {record['session']}, "
                    f"{age_h:.0f}h old) — NOT expired; release explicitly "
                    "if orphaned"
                )
    except RegistryError as exc:
        findings.append(str(exc))
    rc, wt_list, _ = _git(["worktree", "list", "--porcelain"], cwd=repo.primary_root)
    for line in wt_list.splitlines():
        if line.startswith("worktree "):
            path = line.split(" ", 1)[1]
            if _norm(path) == repo.primary_root:
                continue
            rel = os.path.relpath(path, repo.primary_root)
            if not re.match(r"\.worktrees[\\/][a-z]+\+[A-Za-z0-9._-]+$", rel):
                findings.append(f"worktree outside naming convention: {rel} "
                                "(expected .worktrees/<agent>+<slug>)")
    for tool in ("gh", "codebase-memory-mcp"):
        from shutil import which
        if which(tool) is None:
            findings.append(f"optional tool unavailable: {tool} (advisory)")
    findings.append("Codex project-hook trust: unverified until the documented "
                    "/hooks inspection and activation probe are recorded")
    if findings:
        print("doctor findings:")
        for finding in findings:
            print(f"  - {finding}")
    else:
        print("doctor: clean")
    return ALLOW


def cmd_status(repo):
    try:
        claims = list_claims(repo)
    except RegistryError as exc:
        print(str(exc), file=sys.stderr)
        return OPFAIL
    if not claims:
        print("no live claims")
        return ALLOW
    for record in claims:
        print(f"{record['id']}  {record['kind']:9s} {record['value']}  "
              f"session={record['session']}  since={record['created_utc']}")
    return ALLOW


def cmd_check(repo, session, targets):
    """Confirm the current context is safe and inside the approved claim."""
    if repo.branch == MAIN_BRANCH:
        print(f"deny: this tree has '{MAIN_BRANCH}' checked out. main is "
              "read-only: branch first, or use sync-main.", file=sys.stderr)
        return DENY
    for target in targets:
        reason = deny_reason_for_target(target, repo)
        if reason:
            print(f"deny: {reason}", file=sys.stderr)
            return DENY
    # Claim validation is best-effort ON TOP of the stateless rule above.
    try:
        claims = list_claims(repo)
    except RegistryError as exc:
        print(f"operational: {exc}", file=sys.stderr)
        return OPFAIL
    denial = glob_conflict(claims, session, targets)
    if denial:
        print(f"deny: {denial}", file=sys.stderr)
        return DENY
    if session:
        mine = [c for c in claims if c["session"] == session]
        branch_ok = any(
            c["kind"] == "branch" and c["value"] == repo.branch for c in mine
        )
        others = [
            c for c in claims
            if c["kind"] == "branch" and c["value"] == repo.branch
            and c["session"] != session
        ]
        if others:
            other = others[0]
            print(f"deny: branch '{repo.branch}' is claimed by session "
                  f"{other['session']} (claim {other['id']}). Coordinate or "
                  "pick another slice.", file=sys.stderr)
            return DENY
        if not branch_ok:
            # check is the mandatory pre-write confirmation: an unclaimed
            # branch is a denial, not advice.
            print(f"deny: no claim for branch '{repo.branch}' by session "
                  f"{session}. Claim it first:  coordination.py claim "
                  f"--session {session} --kind branch --value {repo.branch}",
                  file=sys.stderr)
            return DENY
    return ALLOW


def glob_conflict(claims, session, targets):
    """Reason string when a target sits in another session's file_glob.

    Paths are made repository-relative against the worktree that governs
    each target, so a claim of `src/*` matches `src/a.cs` identically from
    the primary tree and from any linked worktree.
    """
    for target in targets:
        tree = tree_containing(target)
        if tree is None:
            continue
        rel = os.path.relpath(_norm(target), tree).replace(os.sep, "/")
        for record in claims:
            if record["kind"] == "file_glob" and record["session"] != session \
                    and fnmatch.fnmatch(rel, record["value"]):
                return (f"'{rel}' is inside file_glob '{record['value']}' "
                        f"claimed by session {record['session']} "
                        f"(claim {record['id']}).")
    return None


def cmd_sync_main(repo):
    primary = repo.primary_root
    if branch_of_tree(primary) != MAIN_BRANCH:
        print(f"sync-main: primary tree is not on '{MAIN_BRANCH}'",
              file=sys.stderr)
        return MISUSE
    rc, out, _ = _git(["status", "--porcelain"], cwd=primary)
    if out:
        print("sync-main: primary tree is not clean; refusing", file=sys.stderr)
        return DENY
    rc, _, err = _git(["fetch", "origin"], cwd=primary)
    if rc != 0:
        print(f"sync-main: fetch failed: {err}", file=sys.stderr)
        return OPFAIL
    rc, out, err = _git(
        ["merge", "--ff-only", f"origin/{MAIN_BRANCH}"], cwd=primary
    )
    if rc != 0:
        print(f"sync-main: fast-forward not possible: {err}", file=sys.stderr)
        return DENY
    print(out or "sync-main: up to date")
    return ALLOW


# --------------------------------------------------------------------------
# Hook entrypoints
# --------------------------------------------------------------------------

def hook_pre_commit(repo):
    if repo.branch == MAIN_BRANCH:
        print(f"commit on '{MAIN_BRANCH}' denied: main is read-only. "
              "Branch first; integrate through a pull request.",
              file=sys.stderr)
        return DENY
    return ALLOW


def hook_pre_push(stdin_lines):
    for line in stdin_lines:
        parts = line.split()
        if len(parts) >= 3 and parts[2] in (
            f"refs/heads/{MAIN_BRANCH}", MAIN_BRANCH
        ):
            print(f"push to remote '{MAIN_BRANCH}' denied: integration goes "
                  "through pull requests only.", file=sys.stderr)
            return DENY
    return ALLOW


def hook_pre_tool_use(payload_text):
    """Harness PreToolUse adapter: normalize payload, apply the same rules.

    Emits the Claude/Codex hook decision JSON on deny; silent allow
    otherwise. Fails OPEN on internal error — the stateless git hooks are
    the backstop, and a broken adapter must not lock both agents out.
    """
    try:
        payload = json.loads(payload_text or "{}")
        tool = payload.get("tool_name", "")
        tool_input = payload.get("tool_input", {}) or {}
        cwd = payload.get("cwd") or os.getcwd()
        repo = discover(cwd)
        reason = None
        if tool in ("Edit", "Write", "MultiEdit", "NotebookEdit"):
            target = tool_input.get("file_path") or tool_input.get("notebook_path")
            if target:
                if not os.path.isabs(target):
                    target = os.path.join(cwd, target)
                reason = deny_reason_for_target(target, repo)
                if reason is None and repo is not None:
                    # Claim-aware layer: with a session identity available,
                    # an edit inside another session's file_glob is denied
                    # here too, not only via the explicit `check` command.
                    session = payload.get("session_id") \
                        or os.environ.get("AGENT_SESSION_ID", "")
                    if session:
                        try:
                            reason = glob_conflict(
                                list_claims(repo), session, [target])
                        except RegistryError:
                            reason = None  # stateless rule already passed
        elif tool == "Bash" or tool == "PowerShell":
            command = tool_input.get("command", "")
            reason = deny_reason_for_shell(command, cwd, repo)
        if reason:
            print(json.dumps({
                "hookSpecificOutput": {
                    "hookEventName": "PreToolUse",
                    "permissionDecision": "deny",
                    "permissionDecisionReason": reason,
                }
            }))
        return ALLOW
    except Exception:  # noqa: BLE001 — fail open by contract
        return ALLOW


# --------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------

def main(argv=None):
    parser = argparse.ArgumentParser(prog="coordination.py", description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("init")
    sub.add_parser("doctor")
    sub.add_parser("status")
    p_claim = sub.add_parser("claim")
    p_claim.add_argument("--session", required=True)
    p_claim.add_argument("--kind", required=True, choices=CLAIM_KINDS)
    p_claim.add_argument("--value", default="")
    p_claim.add_argument("--harness", default="")
    p_release = sub.add_parser("release")
    p_release.add_argument("--id", required=True)
    p_release.add_argument("--session", default="")
    p_release.add_argument("--force", action="store_true")
    p_release.add_argument("--reason", default="")
    p_check = sub.add_parser("check")
    p_check.add_argument("--session", default=os.environ.get("AGENT_SESSION_ID", ""))
    p_check.add_argument("--target", action="append", default=[])
    sub.add_parser("sync-main")
    p_hook = sub.add_parser("hook")
    p_hook.add_argument("event", choices=["pre-commit", "pre-push", "pre-tool-use"])

    args = parser.parse_args(argv)
    repo = discover()
    if repo is None:
        if args.command == "hook":
            return ALLOW  # foreign context: not governed
        print("not inside a git repository", file=sys.stderr)
        return OPFAIL

    try:
        if args.command == "init":
            return cmd_init(repo)
        if args.command == "doctor":
            return cmd_doctor(repo)
        if args.command == "status":
            return cmd_status(repo)
        if args.command == "check":
            return cmd_check(repo, args.session, args.target)
        if args.command == "sync-main":
            return cmd_sync_main(repo)
        if args.command == "claim":
            if args.kind != "adr" and not args.value:
                print("claim: --value required for non-adr kinds", file=sys.stderr)
                return MISUSE
            result = claim(repo, args.session, args.kind, args.value,
                           args.harness)
            if "rejected" in result:
                other = result["rejected"]
                print(f"deny: {args.kind} '{args.value}' is claimed by "
                      f"session {other['session']} (claim {other['id']})",
                      file=sys.stderr)
                return DENY
            record = result["claimed"]
            print(f"claimed {record['kind']}={record['value']} id={record['id']}")
            return ALLOW
        if args.command == "release":
            result = release(repo, args.id, session=args.session or None,
                             force=args.force, reason=args.reason)
            if "error" in result:
                print(f"release: {result['error']}", file=sys.stderr)
                return MISUSE
            if "contested" in result:
                other = result["contested"]
                print(f"deny: claim {args.id} belongs to session "
                      f"{other['session']}; use --force --reason with owner "
                      "authorization", file=sys.stderr)
                return DENY
            print(f"released {result['released']['id']}")
            return ALLOW
        if args.command == "hook":
            if args.event == "pre-commit":
                return hook_pre_commit(repo)
            if args.event == "pre-push":
                return hook_pre_push(sys.stdin.read().splitlines())
            if args.event == "pre-tool-use":
                return hook_pre_tool_use(sys.stdin.read())
    except RegistryError as exc:
        print(str(exc), file=sys.stderr)
        return OPFAIL
    return MISUSE


if __name__ == "__main__":
    sys.exit(main())

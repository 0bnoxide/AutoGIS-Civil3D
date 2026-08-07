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
GITHOOKS_DIR = ".githooks"
HOOKSPATH_KEY = "core.hooksPath"
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
    "restore", "clean",
    # Working-tree mutators with no commit, so no hook backstop (#42):
    # rm/mv delete or displace tracked files, stash reverts the tree,
    # apply writes files directly.
    "rm", "mv", "stash", "apply",
    # More working-tree/index writers with no commit backstop (#45):
    # read-tree -u and checkout-index write the tree from index/objects,
    # sparse-checkout adds/removes tracked files, pull fast-forwards the
    # tree and ref with no commit of its own.
    "read-tree", "checkout-index", "sparse-checkout", "pull",
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


def _git_effective_workdir(argv, cwd):
    """The tree a git command will act on.

    Matches git's own resolution order: each -C accumulates against the
    running directory (`git -C a -C b` acts in cwd/a/b), and when both are
    given, --work-tree governs the tree while --git-dir alone implies the
    directory containing the .git dir.
    """
    base = cwd or "."
    work_tree = None
    git_dir = None
    i = 1
    while i < len(argv):
        tok = argv[i]
        key, value = tok, None
        if tok in ("-C", "--work-tree", "--git-dir") and i + 1 < len(argv):
            value, i = argv[i + 1], i + 1
        elif tok.startswith("--work-tree="):
            key, value = "--work-tree", tok[len("--work-tree="):]
        elif tok.startswith("--git-dir="):
            key, value = "--git-dir", tok[len("--git-dir="):]
        if value is not None:
            if key == "-C":
                base = value if os.path.isabs(value) \
                    else os.path.join(base, value)
            elif key == "--work-tree":
                work_tree = value
            else:
                git_dir = value
        i += 1
    if work_tree is not None:
        return work_tree if os.path.isabs(work_tree) \
            else os.path.join(base, work_tree)
    if git_dir is not None:
        resolved = git_dir if os.path.isabs(git_dir) \
            else os.path.join(base, git_dir)
        return os.path.dirname(resolved)
    return base


def _is_checkout_restore(sub, argv, sub_index, workdir):
    """checkout acting as restore: overwrites files with no commit, so no
    hook fires. `-b`/`-B` and bare branch switches stay allowed; a `--`
    pathspec or a positional naming a tracked path is a restore. Tracked
    status comes from git, not filesystem existence — a deleted tracked
    file no longer exists on disk, yet checkout recreates it from the
    index, discarding the uncommitted deletion."""
    if sub != "checkout":
        return False
    rest = argv[sub_index + 1:]
    if "-b" in rest or "-B" in rest:
        return False
    if "--" in rest:
        return True
    for tok in rest:
        if tok.startswith("-"):
            continue
        candidate = tok if os.path.isabs(tok) \
            else os.path.join(workdir or ".", tok)
        if os.path.exists(candidate):
            return True
        rc, _, _err = _git(["-C", workdir or ".", "ls-files",
                            "--error-unmatch", tok])
        if rc == 0:
            return True
    return False


def _short_flags(tok):
    """The letters of a bundled short-flag group (`-Df` -> "Df"); empty for a
    long option or a non-flag. Git bundles single-letter flags, so a
    destructive flag can hide inside a group and must not be matched by exact
    token equality (#45 cold review)."""
    return tok[1:] if tok.startswith("-") and not tok.startswith("--") else ""


def _is_forced_switch(sub, rest):
    """`git switch` overwriting the working tree: --discard-changes/-f/--force
    — including a bundled group like -fq — throw away uncommitted work with no
    commit, so no hook fires. Bare `switch <branch>` and `switch -c` stay
    allowed (#45)."""
    return sub == "switch" and any(
        t in ("--discard-changes", "--force") or "f" in _short_flags(t).lower()
        for t in rest)


def _ref_move_targets_main(sub, rest):
    """A ref-moving command aimed at `main`. Unlike working-tree mutators
    these move, delete, or repoint the shared `main` ref from ANY worktree,
    so they are gated on the target ref, not the cwd's tree (#45) — the same
    shape as the `push` handler. Safe forms carry no main target: listing
    branches, creating/deleting a feature branch, reading a symbolic ref.

    `branch` is dual-use, so it is only destructive with a force/delete/move/
    copy flag (long or bundled short); the check then fails toward deny if
    `main` appears in ANY positional. That is intentionally broad and
    over-denies the obscure safe cases where main is a start-point rather than
    the operated ref (`branch -f x main`) — safe-direction by design.
    `update-ref --stdin` reads its ref from stdin, unparseable here, so it is
    denied outright."""
    main_refs = (MAIN_BRANCH, f"refs/heads/{MAIN_BRANCH}")
    if sub == "branch":
        long_destructive = {"--force", "--delete", "--move", "--copy"}
        if not any(t in long_destructive
                   or set(_short_flags(t).lower()) & set("fdmc")
                   for t in rest):
            return False
    elif sub == "update-ref":
        if "--stdin" in rest:
            return True
    elif sub != "symbolic-ref":
        return False
    return any(t in main_refs for t in rest if not t.startswith("-"))


def deny_reason_for_git_argv(argv, cwd):
    """Deny git commands that mutate main or push to remote main."""
    if not argv or argv[0] != "git":
        return None
    workdir = _git_effective_workdir(argv, cwd)
    sub, sub_index = _git_subcommand(argv)
    if sub == "push":
        for tok in argv[sub_index + 1:]:
            ref = tok.split(":", 1)[1] if ":" in tok else tok
            ref = ref.lstrip("+")  # force-push shorthand +main
            if ref in (MAIN_BRANCH, f"refs/heads/{MAIN_BRANCH}"):
                return (f"push targets remote '{MAIN_BRANCH}'. Integration "
                        "goes through pull requests only.")
        return None
    rest = argv[sub_index + 1:]
    # Ref movers (update-ref/symbolic-ref/destructive branch) shift the shared
    # `main` ref from any worktree, so they are denied by target ref, not by
    # the cwd's tree (#45) — the same treatment as push above.
    if _ref_move_targets_main(sub, rest):
        return (f"git {sub} moves '{MAIN_BRANCH}'. main is read-only: "
                "integration goes through pull requests only.")
    # Read-only / dry-run / object-only forms of the worktree mutators.
    # `stash list/show` is matched on the FIRST token only — `stash push -m
    # "list"` must not slip through on its message text; `stash create/store`
    # touch no tree or index. `apply --check/--stat/--numstat` writes nothing
    # unless --apply forces it; `rm/clean/mv --dry-run` and `sparse-checkout
    # list` likewise.
    if sub == "stash" and rest[:1] \
            and rest[0] in ("list", "show", "create", "store"):
        return None
    if sub == "sparse-checkout" and rest[:1] and rest[0] == "list":
        return None
    if sub == "apply" and "--apply" not in rest \
            and not any(a.startswith("--build-fake-ancestor") for a in rest) \
            and any(a in ("--check", "--stat", "--numstat", "--summary")
                    for a in rest):
        # --build-fake-ancestor writes a file even under --check.
        return None
    if sub in ("rm", "clean", "mv") \
            and any(a in ("-n", "--dry-run") for a in rest):
        return None
    is_mutator = sub in GIT_MUTATORS or _is_forced_switch(sub, rest) \
        or _is_checkout_restore(
            sub, argv, sub_index, _git_effective_workdir(argv, cwd))
    if is_mutator:
        tree = tree_containing(workdir or ".")
        if tree and branch_of_tree(tree) == MAIN_BRANCH:
            return (f"git {sub} on '{MAIN_BRANCH}' is denied. main is "
                    "read-only: branch first, or use sync-main.")
    return None


def deny_reason_for_shell(command, cwd, repo_hint=None, ps=False):
    """Deny shell commands that write files on main or run git mutators there.

    ponytail: tokenizes each `&&`/`;`-separated segment and checks git
    commands plus common write forms (>, >>, tee, sed -i, dd of=, truncate).
    A full shell parser is deferred until a bypass is demonstrated.
    `ps` marks a PowerShell payload: cmdlet write forms apply and
    backslash paths are preserved.
    """
    for event in _shell_events(command, cwd, ps=ps):
        if event[0] == "git":
            reason = deny_reason_for_git_argv(event[1], event[2])
        else:
            reason = deny_reason_for_target(event[1], repo_hint)
        if reason:
            return reason
    return None


def _argv_of(stage):
    try:
        return shlex.split(stage.strip(), posix=True)
    except ValueError:
        return stage.split()


def _copy_move_operands(argv):
    """Split a cp/mv/install argv into (destination, sources).

    `-t DIR` / `--target-directory=DIR` moves the destination out of the
    positionals, so the value token must be consumed rather than treated as
    a source; without that, the last source is mistaken for the destination.
    """
    dest, operands, expect_value = None, [], False
    for arg in argv[1:]:
        if expect_value:
            dest, expect_value = arg, False
        elif arg in ("-t", "--target-directory"):
            expect_value = True
        elif arg.startswith("--target-directory="):
            dest = arg.split("=", 1)[1]
        elif arg.startswith("-") and not arg.startswith("--") and "t" in arg:
            # bundled short flags: -rt DIR, -rtDIR. `t` is the only
            # value-taking short option cp/mv/install share.
            value = arg.partition("t")[2]
            if value:
                dest = value
            else:
                expect_value = True
        elif not arg.startswith("-"):
            operands.append(arg)
    if dest is not None:
        return dest, operands
    if len(operands) >= 2:
        return operands[-1], operands[:-1]
    return None, []


# PowerShell write cmdlets and their aliases (#41). Matching is
# case-insensitive, like PowerShell itself. Copy-class cmdlets write only
# their destination; the rest treat every path operand as written or
# removed (Move-Item removes its sources, like mv).
_PS_WRITE_CMDLETS = {
    "set-content", "add-content", "clear-content", "out-file", "new-item",
    "remove-item", "move-item", "rename-item", "set-item", "clear-item",
    "tee-object", "export-csv", "export-clixml",
    # `sc` is Set-Content under Windows PowerShell 5.1 (gone in pwsh 7,
    # where it would shadow sc.exe). Kept: a false-denied `sc query` on
    # main is deny-safe; dropping it is a 5.1 bypass. Bash payloads are
    # unaffected — the cmdlet layer is PowerShell-gated.
    "sc", "ac", "clc", "ni", "ri", "del", "erase", "rd", "rmdir",
    "move", "mi", "ren", "rni", "tee",
}
_PS_COPY_CMDLETS = {"copy-item", "cpi", "copy"}
_PS_PATH_PARAMS = {"-path", "-literalpath", "-filepath", "-destination",
                   "-target"}
# Parameters known to consume a non-path value. Every OTHER dashed
# parameter is treated as a boolean switch: an unmodeled value-taker can
# only ADD a spurious target (deny direction), whereas the inverse
# default would let an unmodeled switch swallow the path token and
# bypass the rule (`Out-File -NoNewline seed.txt`) — and PowerShell
# writes have no git-hook backstop.
_PS_VALUE_PARAMS = {"-itemtype", "-value", "-encoding", "-filter",
                    "-include", "-exclude", "-newname", "-stream",
                    "-credential", "-erroraction", "-warningaction",
                    "-errorvariable", "-warningvariable", "-outvariable",
                    "-outbuffer", "-delimiter", "-width"}


def _ps_operands(argv):
    """Split a PowerShell cmdlet argv into path-parameter values and
    positional operands. `-Path a,b` claims both."""
    flagged, positional, i = [], [], 1
    while i < len(argv):
        tok = argv[i]
        low = tok.lower()
        if low.startswith("-") and ":" in tok:
            # Colon binding: `-Path:file` carries its value inline. An
            # unknown parameter's inline value is kept as a positional so
            # the unmodeled case still fails toward deny.
            param, _, value = tok.partition(":")
            param = param.lower()
            if param in _PS_PATH_PARAMS:
                flagged += [(param, v) for v in value.split(",")]
            elif param not in _PS_VALUE_PARAMS:
                positional += value.split(",")
            i += 1
        elif low.startswith("-"):
            if low in _PS_PATH_PARAMS and i + 1 < len(argv):
                flagged += [(low, v) for v in argv[i + 1].split(",")]
                i += 2
            elif low in _PS_VALUE_PARAMS and i + 1 < len(argv):
                i += 2  # known value that is not a path
            else:
                i += 1  # switch, or unknown: fail toward deny
        else:
            positional += tok.split(",")
            i += 1
    return flagged, positional


def _ps_write_targets(argv):
    cmd = argv[0].lower()
    flagged, positional = _ps_operands(argv)
    if cmd in _PS_COPY_CMDLETS:
        dests = [v for k, v in flagged if k == "-destination"]
        return dests or positional[-1:]
    if cmd in ("set-item", "si", "clear-item", "cli"):
        # Second positional is the item's VALUE (`Set-Item Env:FOO bar`),
        # never a path.
        return [v for _, v in flagged] + positional[:1]
    return [v for _, v in flagged] + positional


_QUOTE_RE = re.compile(r"'[^']*'|\"(?:\\.|[^\"\\])*\"")
_HEREDOC_RE = re.compile(r"(?<!<)<<-?(?!<)\s*(['\"]?)(\w+)\1")
_REDIR_TARGET_RE = re.compile(r">{1,2}\s*(\"[^\"]*\"|'[^']*'|[^\s|;&<>]+)")


def _mask_literals(command):
    """Blank quoted-string contents and heredoc bodies, preserving positions.

    Operator and redirect scanning runs on the masked text so commit
    messages and heredoc bodies can never yield fake segments or targets;
    argv extraction still reads the real text at the same offsets.
    """
    def blank(m):
        s = m.group(0)
        return s[0] + " " * (len(s) - 2) + s[-1]
    masked = _QUOTE_RE.sub(blank, command)
    # Openers are found in the real text (quote-masking blanks a quoted
    # delimiter like <<'EOF'); masking preserves offsets, so requiring the
    # << itself to be unmasked skips heredoc lookalikes inside strings.
    for m in _HEREDOC_RE.finditer(command):
        if masked[m.start()] != "<":
            continue
        nl = command.find("\n", m.end())
        if nl == -1:
            continue
        term = re.search(r"^[ \t]*%s[ \t]*$" % re.escape(m.group(2)),
                         command[nl + 1:], re.M)
        end = nl + 1 + (term.end() if term else len(masked) - nl - 1)
        masked = masked[:nl + 1] \
            + re.sub(r"[^\n]", " ", masked[nl + 1:end]) + masked[end:]
    return masked


# Command wrappers that run the REAL command given in their trailing argv.
# Peeling them exposes the wrapped command to the argv[0]-keyed checks (#46).
_CMD_PREFIXES = {"sudo", "doas", "env", "nice", "ionice", "chrt", "nohup",
                 "setsid", "stdbuf", "time", "timeout", "watch", "command",
                 "builtin", "exec", "xargs"}
_SH_DASH_C = {"bash", "sh", "zsh", "dash", "ksh"}
_PS_DASH_C = {"pwsh", "powershell"}
_MAX_UNWRAP_DEPTH = 4


def _strip_cmd_prefixes(argv, _depth=0):
    """Peel leading wrapper words (sudo/env/nice/timeout/xargs/...) so the
    wrapped command surfaces (#46). Skips each wrapper's option flags,
    `VAR=val` assignments, and numeric option-values/durations (no command is
    a bare number). A string-valued wrapper option (`sudo -u bob`) is the
    residual: `bob` is then read as the command, which usually just leaves the
    wrapped command unchecked (fail toward today's behaviour) and, in the rare
    case the value matches a mutator name (`sudo -u rm …`), over-denies in the
    safe direction. Neither outcome is a silent new bypass."""
    if _depth >= _MAX_UNWRAP_DEPTH or not argv or argv[0].lower() \
            not in _CMD_PREFIXES:
        return argv
    i = 1
    while i < len(argv):
        tok = argv[i]
        if tok.startswith("-"):
            i += 1  # the wrapper's own option flag
        elif re.match(r"^\w+=", tok):
            i += 1  # env VAR=val
        elif re.match(r"^[\d.]+[smhd]?$", tok):
            i += 1  # numeric option value or duration — never a command name
        else:
            break
    return _strip_cmd_prefixes(argv[i:], _depth + 1)


def _nested_script(argv):
    """If argv is an interpreter invoked on an inline script string, return
    (inner_command, ps) to re-parse; else None. Covers bash/sh/zsh/dash/ksh
    -c, pwsh/powershell -Command/-c, cmd /c, eval, and iex (#46). Arbitrary
    language interpreters (python -c, perl -e) and `$(...)`/backtick command
    substitution are the structural residual — inspecting them means running
    or parsing another language — as is the already-existing `$`/backtick
    redirect-target fail-open below.
    ponytail: nested shell strings only; language interpreters left to the
    git-hook backstop and per-write claim layer."""
    if not argv:
        return None
    head, rest = argv[0].lower(), argv[1:]

    def after(match, ps):
        for j, tok in enumerate(rest):
            if match(tok.lower()) and j + 1 < len(rest):
                return rest[j + 1], ps
        return None

    if head in _SH_DASH_C:
        # A combined short-flag cluster ending in c (-lc, -ec, -xc) carries
        # the script string just like a bare -c; login/error-exit shells make
        # these the common form (cold review).
        return after(lambda t: bool(re.fullmatch(r"-[a-z]*c", t)), False)
    if head in _PS_DASH_C:
        return after(lambda t: t in ("-command", "-c"), True)
    if head in ("cmd", "cmd.exe"):
        return after(lambda t: t in ("/c", "/k"), False)
    if head == "eval" and rest:
        return " ".join(rest), False
    if head in ("iex", "invoke-expression") and rest:
        return rest[0], True
    return None


def _strip_ps_call_block(argv):
    """PowerShell call/dot operator on a script block — `& { cmd }`, `&{cmd}`,
    `. {cmd}`. Strip a leading &/. operator (even when fused to `{`) and all
    brace characters so the inner command reaches the cmdlet checks (#46).
    Top-level `;`/`&&` already split a multi-command block upstream. A leading
    `.\\script.ps1` (dot with no brace) is left untouched."""
    if not argv:
        return argv
    first = argv[0]
    if not (first in ("&", ".") or (first[:1] in ("&", ".") and "{" in first)):
        return argv
    stripped = []
    for i, tok in enumerate(argv):
        if i == 0 and tok[:1] in ("&", "."):
            tok = tok[1:]
        tok = tok.replace("{", "").replace("}", "")
        if tok:
            stripped.append(tok)
    return stripped


def _shell_events(command, cwd, ps=False, _depth=0):
    """Yield ("git", argv, cwd) and ("target", resolved_path) events.

    One parser feeds both the stateless main rule and the claim layer, so a
    write form covered by one is covered by the other.
    """
    effective_cwd = cwd
    if ps:
        # PowerShell's escape character is the backtick, never the
        # backslash, so flipping separators is semantics-preserving and
        # keeps `C:\repo\file` from being eaten by the POSIX tokenizer
        # (which would silently drop the write target).
        command = command.replace("\\", "/")
    masked_all = _mask_literals(command)
    cuts = [m.span() for m in re.finditer(r"&&|\|\||;|\n", masked_all)]
    cuts.append((len(command), len(command)))
    seg_start = 0
    for cut_start, cut_end in cuts:
        seg_real, seg_masked = (command[seg_start:cut_start],
                                masked_all[seg_start:cut_start])
        seg_start = cut_end
        pad = len(seg_real) - len(seg_real.lstrip())
        segment = seg_real.strip()
        seg_masked = seg_masked[pad:pad + len(segment)]
        if not segment or not seg_masked.strip():
            # Fully masked segment: a heredoc body line, not a command.
            continue
        pipe_cuts = [m.start() for m in re.finditer(r"\|", seg_masked)] \
            + [len(segment)]
        stages, last = [], 0
        for p in pipe_cuts:
            stages.append(segment[last:p])
            last = p + 1
        first = _argv_of(stages[0])
        # `cd` moves the parent shell only outside a pipeline; inside one it
        # runs in a subshell and must NOT move later segments.
        if len(stages) == 1 and first and first[0] == "cd" and len(first) > 1:
            effective_cwd = first[1] if os.path.isabs(first[1]) \
                else os.path.join(effective_cwd or ".", first[1])
            continue
        # Locate redirects in the masked text (never inside quotes or
        # heredocs), then read the target token from the real text so a
        # quoted filename survives intact.
        raw_targets = []
        for m in REDIRECT_RE.finditer(seg_masked):
            t = _REDIR_TARGET_RE.match(segment, m.start())
            if t:
                raw_targets.append(t.group(1))
        for stage in stages:
            argv = _strip_cmd_prefixes(_argv_of(stage))
            if ps:
                argv = _strip_ps_call_block(argv)
            if not argv:
                continue
            nested = _nested_script(argv)
            if nested is not None and _depth < _MAX_UNWRAP_DEPTH:
                yield from _shell_events(
                    nested[0], effective_cwd, ps=nested[1], _depth=_depth + 1)
                continue
            yield ("git", argv, effective_cwd)
            if argv[0] == "tee":
                raw_targets += [a for a in argv[1:] if not a.startswith("-")]
            if argv[0] == "sed" and any(a.startswith("-i") for a in argv[1:]):
                raw_targets += [a for a in argv[1:] if not a.startswith("-")][-1:]
            if argv[0] == "dd":
                raw_targets += [a[3:] for a in argv if a.startswith("of=")]
            if argv[0] == "truncate":
                raw_targets += [a for a in argv[1:] if not a.startswith("-")]
            if argv[0] in ("rm", "unlink", "shred"):
                # A deletion never reaches a commit, so no hook can restore
                # it; the adapter is the only layer that sees it.
                raw_targets += [a for a in argv[1:] if not a.startswith("-")]
            low = argv[0].lower()
            if ps and (low in _PS_WRITE_CMDLETS or low in _PS_COPY_CMDLETS):
                # PowerShell payloads only: `copy`, `move`, `del`, `ni`
                # are legitimate command names under other shells.
                # Remove-Item and Move-Item sources are the same
                # no-backstop delete class as rm/mv above.
                raw_targets += _ps_write_targets(argv)
            if argv[0] in ("cp", "mv", "install"):
                dest, sources = _copy_move_operands(argv)
                if dest:
                    raw_targets.append(dest)
                if argv[0] == "mv":
                    # mv removes its sources with no commit, like rm.
                    raw_targets += sources
        for target in raw_targets:
            # Redirect targets are cut from raw text, so quotes survive;
            # argv-derived ones already had theirs stripped by shlex.
            target = target.strip("\"'")
            if ps and re.match(r"[A-Za-z]{2,}:", target):
                # Provider path (Env:, Variable:, Alias:, HKLM:, or a
                # named PSDrive), not a filesystem file. Single letters
                # stay: those are real drives. A multi-letter PSDrive
                # mapped onto the filesystem escapes here — accepted
                # fail-open, like the unexpanded substitutions below.
                continue
            target = os.path.expanduser(target)
            resolved = target if os.path.isabs(target) \
                else os.path.join(effective_cwd or ".", target)
            if "$" in resolved or "`" in resolved:
                # Unexpanded substitution — a `$`/backtick in the token itself,
                # or introduced via the effective cwd (`cd "$D"` then a plain
                # relative target, #56). Checking the RESOLVED path catches both
                # entry paths in one place. Not evaluable here: fail open per
                # the adapter contract (#17) — git hooks backstop write forms;
                # delete-class targets (rm, mv sources) are an accepted residual.
                continue
            yield ("target", resolved)


def shell_write_targets(command, cwd, ps=False):
    return [e[1] for e in _shell_events(command, cwd, ps=ps)
            if e[0] == "target"]


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
                # Only the exclusive create is retryable. A failure after it
                # succeeds means this process holds the lock, and retrying
                # would make it contend with its own lock file.
                self.fd = os.open(
                    self.lock_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY
                )
            except (FileExistsError, PermissionError) as err:
                # Contention shows up as FileExistsError while the holder's
                # lock file exists, and as PermissionError on Windows in the
                # window around its removal, where a sharing violation maps to
                # errno 13. Same contention, different errno: retry, never
                # steal. A denial that outlives the deadline is reported
                # verbatim, because then it is an unwritable state directory
                # rather than contention.
                if time.monotonic() >= deadline:
                    # Decide from the exception captured at the final attempt,
                    # not a filesystem probe that races the holder's removal
                    # (#57): a FileExistsError *is* contention, whether or not
                    # the lock still exists a moment later. PermissionError is
                    # ambiguous — Windows sharing-violation contention vs an
                    # unwritable directory — so it falls back to the probe.
                    if isinstance(err, FileExistsError) \
                            or os.path.exists(self.lock_path):
                        raise RegistryError(
                            f"registry lock held: {self.lock_path}. If the "
                            "holder is dead, remove the lock file manually "
                            "(doctor reports it); automatic reaping is "
                            "deliberately absent."
                        )
                    # No lock file to blame: the state directory itself is
                    # refusing the create, so say that instead of sending
                    # the caller after a lock that does not exist.
                    raise RegistryError(
                        f"registry lock not creatable: {self.lock_path}. "
                        f"The state directory refused the write: {err}"
                    )
                time.sleep(0.1)
            else:
                try:
                    os.write(self.fd,
                             f"{os.getpid()}@{socket.gethostname()} {_now()}".encode())
                except OSError as err:
                    # Holding a lock nobody can release is worse than the
                    # failure itself: drop it first. Report it as a registry
                    # error, since RegistryError is not an OSError and a bare
                    # one escapes main() as the traceback this fix removes.
                    self.__exit__()
                    self.fd = None
                    raise RegistryError(
                        f"registry lock acquired but not writable: "
                        f"{self.lock_path}. The lock was released: {err}"
                    ) from err
                return self

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


def _glob_prefix(value):
    """A claimable glob is a literal path or `<dir>/*` (whole-subtree claim).

    Restricting the domain makes overlap exactly decidable — a prefix
    relation — where arbitrary patterns would need glob intersection.
    Returns the claimed prefix ("dir/" or the literal), or None if invalid.
    """
    value = value.replace("\\", "/")
    if re.fullmatch(r"[^*?\[\]]+/\*", value):
        return value[:-1]  # "src/*" -> "src/"
    if re.fullmatch(r"[^*?\[\]]+", value):
        return value
    return None


def _globs_overlap(a, b):
    pa, pb = _glob_prefix(a), _glob_prefix(b)
    if pa is None or pb is None:
        return True  # unvalidatable legacy pattern: treat as conflicting
    # Segment-boundary comparison: `src` and `srclib/*` are siblings, not a
    # conflict, and `src/main` vs `src/main2` differ. A directory claim
    # conflicts with anything at or under its prefix.
    la, is_dir_a = pa.rstrip("/"), pa.endswith("/")
    lb, is_dir_b = pb.rstrip("/"), pb.endswith("/")
    if la == lb:
        return True
    if is_dir_a and lb.startswith(la + "/"):
        return True
    if is_dir_b and la.startswith(lb + "/"):
        return True
    return False


def claim(repo, session, kind, value, harness=""):
    if kind not in CLAIM_KINDS:
        raise ValueError(f"unknown claim kind '{kind}'")

    def apply(data):
        if kind == "adr":
            return _allocate_adr(repo, data, session, harness)
        if kind == "file_glob" and _glob_prefix(value) is None:
            return {"error": "file_glob must be a literal path or a "
                             "directory claim of the form <dir>/* — "
                             "arbitrary patterns make overlap undecidable"}
        for existing in data["claims"]:
            if existing["kind"] != kind or existing["session"] == session:
                continue
            if _same_value(kind, existing["value"], value):
                return {"rejected": existing}
            if kind == "file_glob" and _globs_overlap(value, existing["value"]):
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
    hooks_dir = os.path.join(repo.worktree_root, GITHOOKS_DIR)
    for hook in ("pre-commit", "pre-push"):
        if not os.path.exists(os.path.join(hooks_dir, hook)):
            print(f"init: missing {hooks_dir}/{hook}", file=sys.stderr)
            return OPFAIL
    if repo.primary_root != repo.worktree_root and not os.path.exists(
            os.path.join(repo.primary_root, GITHOOKS_DIR, "pre-commit")):
        print("init: note — the primary tree has no .githooks yet; its "
              "protection begins when they merge to main")
    rc, current, _ = _git(["config", HOOKSPATH_KEY], cwd=repo.primary_root)
    if current != GITHOOKS_DIR:
        rc, _, err = _git(
            ["config", HOOKSPATH_KEY, GITHOOKS_DIR], cwd=repo.primary_root
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


def codex_hook_trust_date(repo):
    """Return the recorded hook-trust verification date, if complete."""
    evidence_path = os.path.join(
        repo.worktree_root, "docs", "verification",
        "codex-project-hook-trust.md")
    try:
        with open(evidence_path, encoding="utf-8") as fh:
            lines = {line.rstrip("\r\n") for line in fh}
    except OSError:
        return None
    date_prefix = "Verification date: "
    dates = [line[len(date_prefix):] for line in lines
             if line.startswith(date_prefix)]
    if len(dates) != 1 or not {
            "Hooks inspection: passed", "Activation probe: passed"} <= lines:
        return None
    try:
        _dt.date.fromisoformat(dates[0])
    except ValueError:
        return None
    return dates[0]


def cmd_doctor(repo):
    findings = []
    rc, hooks_path, _ = _git(["config", HOOKSPATH_KEY], cwd=repo.primary_root)
    if hooks_path != GITHOOKS_DIR:
        findings.append("core.hooksPath is not .githooks — run init")
    for hook in ("pre-commit", "pre-push"):
        if not os.path.exists(os.path.join(repo.worktree_root, GITHOOKS_DIR, hook)):
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
            rel = os.path.relpath(path, repo.primary_root).replace("\\", "/")
            # .claude/worktrees/ is Claude Code's fixed native location; it
            # cannot be relocated into .worktrees/, so doctor accepts it.
            if not re.match(r"(\.worktrees/[a-z]+\+"
                            r"|\.claude/worktrees/)[A-Za-z0-9._-]+$", rel):
                findings.append(f"worktree outside naming convention: {rel} "
                                "(expected .worktrees/<agent>+<slug> or "
                                ".claude/worktrees/<slug>)")
    sync_script = os.path.join(repo.worktree_root, "tools", "agent-assets",
                               "sync.py")
    if os.path.exists(sync_script):
        # doctor is advisory: a hanging, unspawnable, or garbage-emitting
        # sync must become a finding, never abort the diagnostic run.
        try:
            proc = subprocess.run(
                [sys.executable, sync_script, "--check"],
                capture_output=True, text=True, encoding="utf-8",
                errors="replace", timeout=60,
            )
            rc = proc.returncode
        except (subprocess.TimeoutExpired, OSError):
            rc = -1
        if rc == 1:
            findings.append("agent-asset drift (rendered copies edited "
                            "directly?): run tools/agent-assets/sync.py")
        elif rc != 0:
            findings.append("agent-asset drift check failed to run (advisory)")
    for tool in ("gh", "codebase-memory-mcp"):
        from shutil import which
        if which(tool) is None:
            findings.append(f"optional tool unavailable: {tool} (advisory)")
    trust_date = codex_hook_trust_date(repo)
    if trust_date is not None:
        findings.append(f"Codex project-hook trust: verified ({trust_date})")
    else:
        findings.append("Codex project-hook trust: unverified until the "
                        "documented /hooks inspection and activation probe "
                        "are recorded")
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
    denial = claim_denial(claims, session, targets)
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


def claim_denial(claims, session, targets):
    """Positive claim validation, shared by `check` and the adapter so the
    automatic path is never weaker than the explicit command: others' globs,
    the caller's own file scope, and the branch claim of each target's tree.
    """
    reason = glob_conflict(claims, session, targets)
    if reason or not session:
        return reason
    my_globs = [c["value"] for c in claims
                if c["kind"] == "file_glob" and c["session"] == session]
    for target in targets:
        tree = tree_containing(target)
        if tree is None:
            continue
        rel = os.path.relpath(_norm(target), tree).replace(os.sep, "/")
        if my_globs and not any(fnmatch.fnmatch(rel, g) for g in my_globs):
            return (f"'{rel}' is outside your claimed file scope "
                    f"({', '.join(my_globs)}). Claim it or narrow the change.")
        reason = _branch_claim_denial(claims, session, branch_of_tree(tree))
        if reason:
            return reason
    return None


def _branch_claim_denial(claims, session, branch):
    """Reason string when `branch` is claimed by someone else, or unclaimed."""
    if not branch or branch == MAIN_BRANCH:
        return None
    branch_claims = [c for c in claims
                     if c["kind"] == "branch" and c["value"] == branch]
    other = next((c for c in branch_claims
                  if c["session"] != session), None)
    if other:
        return (f"branch '{branch}' is claimed by session "
                f"{other['session']} (claim {other['id']}). "
                "Coordinate or pick another slice.")
    if not any(c["session"] == session for c in branch_claims):
        return (f"no claim for branch '{branch}' by session "
                f"{session}. Claim it first:  coordination.py claim "
                f"--session {session} --kind branch --value {branch}")
    return None


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
                            reason = claim_denial(
                                list_claims(repo), session, [target])
                        except RegistryError as exc:
                            # Claim-dependent writes are blocked on corrupt
                            # state (architecture rule) — there is no
                            # backstop for file ownership below this layer.
                            reason = str(exc)
        elif tool == "Bash" or tool == "PowerShell":
            command = tool_input.get("command", "")
            ps = tool == "PowerShell"
            reason = deny_reason_for_shell(command, cwd, repo, ps=ps)
            if reason is None and repo is not None:
                # Shell write forms get the same claim layer as Edit/Write.
                session = payload.get("session_id") \
                    or os.environ.get("AGENT_SESSION_ID", "")
                if session:
                    try:
                        reason = claim_denial(
                            list_claims(repo), session,
                            shell_write_targets(command, cwd, ps=ps))
                    except RegistryError as exc:
                        reason = str(exc)
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
            # pre-tool-use governs the payload's cwd, not the hook process's:
            # hook_pre_tool_use does its own discover(payload cwd), so a write
            # into the main checkout must reach it even when the harness runs
            # the hook from outside any repository (#47). The git hooks always
            # run in-repo, so their foreign-context short-circuit is sound.
            if args.event == "pre-tool-use":
                return hook_pre_tool_use(sys.stdin.read())
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
            if "error" in result:
                print(f"claim: {result['error']}", file=sys.stderr)
                return MISUSE
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

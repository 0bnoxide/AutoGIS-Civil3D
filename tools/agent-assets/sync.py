#!/usr/bin/env python3
"""Deterministic agent-asset sync (Phase 0 plan, Step 9).

Canonical sources live here; harness discovery paths are render targets and
never edited directly:

  skills/<name>/**  -> .claude/skills/<name>/**   (byte copy)
                    -> .agents/skills/<name>/**   (byte copy)
  agents/<name>.md  -> .claude/agents/<name>.md   (byte copy)
                    -> .codex/agents/<name>.toml  (generated translation)

One canonical copy per asset is the point: the AutoGIS per-harness copies
drifted (three of five hooks diverged, one announced the wrong harness).
Sync recreates destinations cleanly and prunes destination-only files, so an
upstream deletion cannot linger. `--check` reports drift and exits 1 without
writing. Standard library only. Exit: 0 clean/synced, 1 drift (check mode),
3 operational failure.
"""

from __future__ import annotations

import argparse

import os
import shutil
import sys

ASSETS_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(os.path.dirname(ASSETS_DIR))
SKILL_TARGETS = (".claude/skills", ".agents/skills")
AGENT_MD_TARGET = ".claude/agents"
AGENT_TOML_TARGET = ".codex/agents"


def canonical_skills():
    src = os.path.join(ASSETS_DIR, "skills")
    if not os.path.isdir(src):
        return {}
    return {name: os.path.join(src, name) for name in sorted(os.listdir(src))
            if os.path.isdir(os.path.join(src, name))}


def canonical_agents():
    src = os.path.join(ASSETS_DIR, "agents")
    if not os.path.isdir(src):
        return {}
    return {name[:-3]: os.path.join(src, name)
            for name in sorted(os.listdir(src)) if name.endswith(".md")}


def parse_frontmatter(md_text):
    """Minimal YAML frontmatter reader: name/description scalars only.

    Line-based rather than one DOTALL regex: the lazy `(.*?)` form is
    super-linear on input with no closing fence (SonarQube S8786).
    """
    lines = md_text.splitlines(keepends=True)
    if not lines or lines[0].rstrip("\r\n") != "---":
        return {}, md_text
    # Start at 2: the regex required a newline between the fences, so a
    # closing fence directly after the opener is not a match.
    for index in range(2, len(lines)):
        if lines[index].rstrip("\r\n") != "---":
            continue
        fields = {}
        for line in lines[1:index]:
            key, sep, value = line.partition(":")
            if sep and _is_frontmatter_key(key):
                fields[key] = value.strip().strip("'\"")
        return fields, "".join(lines[index + 1:])
    return {}, md_text


def _is_frontmatter_key(key):
    """Equivalent of the former `\\w[\\w-]*` key pattern, without regex
    (SonarQube S8786 flagged the backtracking potential)."""
    return (bool(key) and (key[0].isalnum() or key[0] == "_")
            and all(ch.isalnum() or ch in "_-" for ch in key))


def _toml_basic(value):
    """Escape for a TOML basic (double-quoted, single-line) string."""
    value = value.replace("\\", "\\\\").replace('"', '\\"')
    return value.replace("\n", " ").replace("\r", "")


def render_toml(name, md_text):
    """Codex agent TOML from the canonical Claude-format definition."""
    fields, body = parse_frontmatter(md_text)
    description = _toml_basic(fields.get("description", ""))
    instructions = body.strip().replace("'''", "\\'\\'\\'")
    return (
        f'name = "{_toml_basic(fields.get("name", name))}"\n'
        f'description = "{description}"\n'
        f"developer_instructions = '''\n{instructions}\n'''\n"
    )


def _normalize_eol(data):
    """Line-ending-insensitive view for drift comparison.

    Sync writes LF, but git smudges a rendered copy to CRLF on any Windows
    `autocrlf` checkout — its blob is still LF and git treats the two as
    equivalent (`git add --renormalize` stages nothing). That is a checkout
    artifact, not a hand-edit, yet a byte-exact compare flagged all seven
    assets as drift on every Windows working tree (#38). Normalizing CRLF and
    lone CR to LF ignores that artifact; a genuine content edit still differs.
    """
    return data.replace(b"\r\n", b"\n").replace(b"\r", b"\n")


def _tree_manifest(root):
    manifest = {}
    for dirpath, _dirs, files in os.walk(root):
        for name in files:
            path = os.path.join(dirpath, name)
            with open(path, "rb") as fh:
                manifest[os.path.relpath(path, root)] = _normalize_eol(fh.read())
    return manifest


def _trees_equal(a, b):
    # Content comparison, deliberately not filecmp's stat heuristic: on a fresh
    # checkout mtimes are checkout-time accidents. Line endings are normalized
    # first (see _normalize_eol) so a CRLF checkout does not read as drift.
    return _tree_manifest(a) == _tree_manifest(b)


def _files_equal(path, content):
    if not os.path.exists(path):
        return False
    with open(path, "rb") as fh:
        return _normalize_eol(fh.read()) == _normalize_eol(content.encode("utf-8"))


def _sync_skill_target(target_rel, target_root, skills, check_only, drift):
    """Copy out-of-sync skills into one target and prune destination-only
    directories there."""
    for name, src in skills.items():
        dest = os.path.join(target_root, name)
        if not os.path.isdir(dest) or not _trees_equal(src, dest):
            drift.append(f"{target_rel}/{name}: out of sync")
            if not check_only:
                shutil.rmtree(dest, ignore_errors=True)
                shutil.copytree(src, dest)
    _prune_skill_dirs(target_rel, target_root, skills, check_only, drift)


def _prune_skill_dirs(target_rel, target_root, skills, check_only, drift):
    """Remove rendered skill directories whose canonical source is gone."""
    if not os.path.isdir(target_root):
        return
    for name in sorted(os.listdir(target_root)):
        if name in skills or not os.path.isdir(os.path.join(target_root, name)):
            continue
        suffix = "destination-only" if check_only else "destination-only, pruned"
        drift.append(f"{target_rel}/{name}: {suffix}")
        if not check_only:
            shutil.rmtree(os.path.join(target_root, name))


def _write_if_drifted(dest, dest_rel, content, check_only, drift):
    """Record and (outside check mode) write one rendered agent file."""
    if _files_equal(dest, content):
        return
    drift.append(f"{dest_rel}: out of sync")
    if not check_only:
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        with open(dest, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(content)


def _prune_agent_files(root, target_root, suffix, agents, check_only, drift):
    """Remove rendered agent files whose canonical source is gone."""
    if not os.path.isdir(target_root):
        return
    for name in sorted(os.listdir(target_root)):
        if not name.endswith(suffix) or name[: -len(suffix)] in agents:
            continue
        drift.append(f"{os.path.relpath(target_root, root)}/"
                     f"{name}: destination-only")
        if not check_only:
            os.remove(os.path.join(target_root, name))


def run(root, check_only):
    drift = []
    skills = canonical_skills()
    agents = canonical_agents()

    for target_rel in SKILL_TARGETS:
        _sync_skill_target(
            target_rel, os.path.join(root, target_rel), skills, check_only, drift)

    md_root = os.path.join(root, AGENT_MD_TARGET)
    toml_root = os.path.join(root, AGENT_TOML_TARGET)
    for name, src in agents.items():
        with open(src, encoding="utf-8") as fh:
            md_text = fh.read()
        _write_if_drifted(
            os.path.join(md_root, f"{name}.md"),
            f"{AGENT_MD_TARGET}/{name}.md", md_text, check_only, drift)
        _write_if_drifted(
            os.path.join(toml_root, f"{name}.toml"),
            f"{AGENT_TOML_TARGET}/{name}.toml",
            render_toml(name, md_text), check_only, drift)
    for target_root, suffix in ((md_root, ".md"), (toml_root, ".toml")):
        _prune_agent_files(root, target_root, suffix, agents, check_only, drift)

    return drift


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true",
                        help="report drift, write nothing, exit 1 on drift")
    parser.add_argument("--root", default=REPO_ROOT)
    args = parser.parse_args(argv)
    try:
        drift = run(os.path.abspath(args.root), args.check)
    except OSError as exc:
        print(f"sync: operational failure: {exc}", file=sys.stderr)
        return 3
    if args.check:
        if drift:
            print("sync --check: DRIFT")
            for item in drift:
                print(f"  - {item}")
            return 1
        print("sync --check: clean")
        return 0
    if drift:
        print("synced:")
        for item in drift:
            print(f"  - {item}")
    else:
        print("sync: already clean")
    return 0


if __name__ == "__main__":
    sys.exit(main())

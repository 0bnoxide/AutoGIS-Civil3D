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
import re
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
    for index in range(1, len(lines)):
        if lines[index].rstrip("\r\n") != "---":
            continue
        fields = {}
        for line in lines[1:index]:
            kv = re.match(r"^(\w[\w-]*):\s*(.*)$", line)
            if kv:
                fields[kv.group(1)] = kv.group(2).strip().strip("'\"")
        return fields, "".join(lines[index + 1:])
    return {}, md_text


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


def _tree_manifest(root):
    manifest = {}
    for dirpath, _dirs, files in os.walk(root):
        for name in files:
            path = os.path.join(dirpath, name)
            with open(path, "rb") as fh:
                manifest[os.path.relpath(path, root)] = fh.read()
    return manifest


def _trees_equal(a, b):
    # Byte comparison, deliberately not filecmp's stat heuristic: on a fresh
    # checkout mtimes are checkout-time accidents, and a size-equal CRLF/LF
    # difference must count as drift.
    return _tree_manifest(a) == _tree_manifest(b)


def _files_equal(path, content):
    if not os.path.exists(path):
        return False
    with open(path, "rb") as fh:
        return fh.read() == content.encode("utf-8")


def run(root, check_only):
    drift = []
    skills = canonical_skills()
    agents = canonical_agents()

    for target_rel in SKILL_TARGETS:
        target_root = os.path.join(root, target_rel)
        for name, src in skills.items():
            dest = os.path.join(target_root, name)
            if not os.path.isdir(dest) or not _trees_equal(src, dest):
                drift.append(f"{target_rel}/{name}: out of sync")
                if not check_only:
                    shutil.rmtree(dest, ignore_errors=True)
                    shutil.copytree(src, dest)
        if os.path.isdir(target_root):
            for name in sorted(os.listdir(target_root)):
                if name not in skills and os.path.isdir(
                        os.path.join(target_root, name)):
                    drift.append(f"{target_rel}/{name}: destination-only, "
                                 "pruned" if not check_only else
                                 f"{target_rel}/{name}: destination-only")
                    if not check_only:
                        shutil.rmtree(os.path.join(target_root, name))

    md_root = os.path.join(root, AGENT_MD_TARGET)
    toml_root = os.path.join(root, AGENT_TOML_TARGET)
    for name, src in agents.items():
        with open(src, encoding="utf-8") as fh:
            md_text = fh.read()
        md_dest = os.path.join(md_root, f"{name}.md")
        if not _files_equal(md_dest, md_text):
            drift.append(f"{AGENT_MD_TARGET}/{name}.md: out of sync")
            if not check_only:
                os.makedirs(md_root, exist_ok=True)
                with open(md_dest, "w", encoding="utf-8", newline="\n") as fh:
                    fh.write(md_text)
        toml_text = render_toml(name, md_text)
        toml_dest = os.path.join(toml_root, f"{name}.toml")
        if not _files_equal(toml_dest, toml_text):
            drift.append(f"{AGENT_TOML_TARGET}/{name}.toml: out of sync")
            if not check_only:
                os.makedirs(toml_root, exist_ok=True)
                with open(toml_dest, "w", encoding="utf-8", newline="\n") as fh:
                    fh.write(toml_text)
    for target_root, suffix in ((md_root, ".md"), (toml_root, ".toml")):
        if os.path.isdir(target_root):
            for name in sorted(os.listdir(target_root)):
                if name.endswith(suffix) and name[: -len(suffix)] not in agents:
                    drift.append(f"{os.path.relpath(target_root, root)}/"
                                 f"{name}: destination-only")
                    if not check_only:
                        os.remove(os.path.join(target_root, name))

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

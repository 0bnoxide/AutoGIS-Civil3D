"""Parity and determinism tests for the agent-asset sync."""

import os
import shutil
import sys
import tempfile
import unittest

ASSETS_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, ASSETS_DIR)

import sync  # noqa: E402


def snapshot(root):
    state = {}
    for rel in (*sync.SKILL_TARGETS, sync.AGENT_MD_TARGET,
                sync.AGENT_TOML_TARGET):
        base = os.path.join(root, rel)
        for dirpath, _dirs, files in os.walk(base):
            for name in files:
                path = os.path.join(dirpath, name)
                with open(path, "rb") as fh:
                    state[os.path.relpath(path, root)] = fh.read()
    return state


class TestSync(unittest.TestCase):
    def setUp(self):
        self.root = tempfile.mkdtemp(prefix="asset-sync-")
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)

    def test_sync_twice_is_byte_identical(self):
        sync.run(self.root, check_only=False)
        first = snapshot(self.root)
        self.assertTrue(first, "sync produced no files")
        drift = sync.run(self.root, check_only=False)
        self.assertEqual(drift, [])
        self.assertEqual(snapshot(self.root), first)

    def test_every_canonical_asset_reaches_both_harnesses(self):
        sync.run(self.root, check_only=False)
        for name in sync.canonical_skills():
            for target in sync.SKILL_TARGETS:
                self.assertTrue(
                    os.path.isfile(os.path.join(
                        self.root, target, name, "SKILL.md")),
                    f"{target}/{name}/SKILL.md missing")
        for name in sync.canonical_agents():
            self.assertTrue(os.path.isfile(os.path.join(
                self.root, sync.AGENT_MD_TARGET, f"{name}.md")))
            self.assertTrue(os.path.isfile(os.path.join(
                self.root, sync.AGENT_TOML_TARGET, f"{name}.toml")))

    def test_destination_only_asset_pruned(self):
        sync.run(self.root, check_only=False)
        stray = os.path.join(self.root, sync.SKILL_TARGETS[0], "stray-skill")
        os.makedirs(stray)
        with open(os.path.join(stray, "SKILL.md"), "w",
                  encoding="utf-8") as fh:
            fh.write("stray\n")
        sync.run(self.root, check_only=False)
        self.assertFalse(os.path.isdir(stray))

    def test_check_mode_reports_without_writing(self):
        sync.run(self.root, check_only=False)
        target = os.path.join(self.root, sync.AGENT_MD_TARGET,
                              "pr-reviewer.md")
        with open(target, "a", encoding="utf-8") as fh:
            fh.write("tampered\n")
        with open(target, "rb") as fh:
            tampered = fh.read()
        drift = sync.run(self.root, check_only=True)
        self.assertTrue(drift)
        with open(target, "rb") as fh:
            self.assertEqual(fh.read(), tampered, "--check must not write")

    def test_toml_render_carries_name_description_body(self):
        toml = sync.render_toml("pr-reviewer", (
            "---\nname: pr-reviewer\ndescription: A cold reviewer.\n---\n"
            "Body line one.\n"))
        self.assertIn('name = "pr-reviewer"', toml)
        self.assertIn('description = "A cold reviewer."', toml)
        self.assertIn("Body line one.", toml)


if __name__ == "__main__":
    unittest.main(verbosity=2)

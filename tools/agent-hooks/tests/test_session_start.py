"""session-start.sh pulls main before indexing and flags a stale index (#218).

Runs the real hook against a throwaway repository with a stub
codebase-memory-mcp on PATH, so the log line it writes is the thing tested.
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
HOOK = os.path.join(ROOT, "tools", "agent-hooks", "session-start.sh")

STUB = """#!/bin/bash
case "$2" in
  query_graph) cat "$STUB_ROWS" ;;
  index_status) echo '{"nodes":7,"status":"ready"}' ;;
esac
exit 0
"""


def find_bash():
    # Bare `bash` on Windows can be the WSL launcher; use Git Bash, located
    # the way session-start.ps1 does.
    if sys.platform != "win32":
        return shutil.which("bash")
    try:
        exec_path = subprocess.run(["git", "--exec-path"], capture_output=True,
                                   text=True, check=True).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        return None
    git_root = os.path.normpath(os.path.join(exec_path, "..", "..", ".."))
    for rel in (("bin", "bash.exe"), ("usr", "bin", "bash.exe")):
        candidate = os.path.join(git_root, *rel)
        if os.path.exists(candidate):
            return candidate
    return None


BASH = find_bash()


@unittest.skipUnless(BASH, "needs bash")
class TestSessionStart(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, self.tmp, ignore_errors=True)
        self.home = os.path.join(self.tmp, "home")
        stub_dir = os.path.join(self.tmp, "bin")
        os.makedirs(self.home)
        os.makedirs(stub_dir)
        stub = os.path.join(stub_dir, "codebase-memory-mcp")
        with open(stub, "w", newline="\n") as fh:
            fh.write(STUB)
        os.chmod(stub, 0o755)
        self.rows = os.path.join(self.tmp, "rows.json")
        self.env = dict(os.environ, HOME=self.home, STUB_ROWS=self.rows,
                        PATH=stub_dir + os.pathsep + os.environ["PATH"],
                        GIT_CONFIG_GLOBAL=os.devnull, GIT_CONFIG_NOSYSTEM="1")

        self.origin = os.path.join(self.tmp, "origin.git")
        self.primary = os.path.join(self.tmp, "primary")
        self.git("init", "-q", "--bare", "-b", "main", self.origin, cwd=self.tmp)
        self.git("clone", "-q", self.origin, self.primary, cwd=self.tmp)
        files = ["src/a.py", "src/b.py", "src/c.py", "README.md", "img/logo.png",
                 ".claude/hook.py"]
        for rel in files:
            self.write(self.primary, rel)
        self.commit(self.primary, "seed")
        self.git("push", "-q", "origin", "main")

    def git(self, *args, cwd=None):
        return subprocess.run(
            ["git", "-c", "user.name=t", "-c", "user.email=t@t", *args],
            cwd=cwd or self.primary, env=self.env, capture_output=True,
            text=True, check=True).stdout.strip()

    def write(self, base, rel):
        path = os.path.join(base, *rel.split("/"))
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w") as fh:
            fh.write(rel)

    def commit(self, repo, message):
        self.git("add", "-A", cwd=repo)
        self.git("commit", "-q", "-m", message, cwd=repo)

    def push_upstream_commit(self):
        other = os.path.join(self.tmp, "other")
        self.git("clone", "-q", self.origin, other, cwd=self.tmp)
        self.write(other, "src/d.py")
        self.commit(other, "upstream")
        self.git("push", "-q", "origin", "main", cwd=other)
        return self.git("rev-parse", "HEAD", cwd=other)

    def run_hook(self, indexed):
        with open(self.rows, "w") as fh:
            json.dump({"columns": ["f.file_path"],
                       "rows": [[p] for p in indexed], "total": len(indexed)}, fh)
        subprocess.run([BASH, HOOK], cwd=self.primary, env=self.env,
                       input="{}", capture_output=True, text=True, check=True)
        log = os.path.join(self.home, ".cache", "codebase-memory-mcp",
                           "last-index.log")
        with open(log) as fh:
            line = fh.read().splitlines()[-1]
        self.assertIn(" AutoGIS-Civil3D ", line)
        return line

    def test_pulls_main_before_indexing_despite_untracked_files(self):
        upstream = self.push_upstream_commit()
        self.write(self.primary, "scratch/untracked.txt")
        line = self.run_hook(["src/a.py", "src/b.py", "src/c.py", "src/d.py"])
        self.assertEqual(self.git("rev-parse", "HEAD"), upstream)
        self.assertIn("pull=ok", line)
        # README.md, img/ and dot-dirs are never indexed here: not counted.
        self.assertIn(" ok files=4/4 ", line)

    def test_dirty_tracked_tree_is_not_synced(self):
        self.push_upstream_commit()
        with open(os.path.join(self.primary, "src", "a.py"), "a") as fh:
            fh.write("edit")
        before = self.git("rev-parse", "HEAD")
        line = self.run_hook(["src/a.py", "src/b.py", "src/c.py"])
        self.assertEqual(self.git("rev-parse", "HEAD"), before)
        self.assertIn("pull=refused", line)

    def test_missing_files_log_stale_with_fix(self):
        line = self.run_hook(["src/a.py"])
        self.assertIn(" STALE 2 of 3 ", line)
        self.assertIn("delete_project", line)
        self.assertNotIn(" ok ", line)

    def test_wholly_missing_directory_is_stale(self):
        self.write(self.primary, "tests/t1.py")
        self.write(self.primary, "tests/t2.py")
        self.commit(self.primary, "tests")
        line = self.run_hook(["src/a.py", "src/b.py", "src/c.py"])
        self.assertIn(" STALE 2 of 5 ", line)

    def test_unreadable_graph_is_unchecked_not_stale(self):
        line = self.run_hook([])
        self.assertIn(" ok files=unchecked ", line)


if __name__ == "__main__":
    unittest.main()

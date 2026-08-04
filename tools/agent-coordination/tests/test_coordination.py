"""Phase 0 coordination tests: decision matrix, registry integrity, hooks.

Standard library unittest only. Every test builds disposable repositories
under a temp directory — the real primary worktree is never a target.
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
import threading
import unittest

MODULE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, MODULE_DIR)

import coordination  # noqa: E402


def run_git(args, cwd):
    return subprocess.run(["git", *args], cwd=cwd, capture_output=True,
                          text=True, check=False)


def make_repo(base, name="repo"):
    path = os.path.join(base, name)
    os.makedirs(path)
    run_git(["init", "-q", "-b", "main"], path)
    run_git(["config", "user.email", "t@t"], path)
    run_git(["config", "user.name", "t"], path)
    with open(os.path.join(path, "seed.txt"), "w", encoding="utf-8") as fh:
        fh.write("seed\n")
    run_git(["add", "."], path)
    run_git(["commit", "-q", "-m", "seed"], path)
    return path


class TempRepoCase(unittest.TestCase):
    def setUp(self):
        self.base = tempfile.mkdtemp(prefix="coord-test-")
        self.addCleanup(shutil.rmtree, self.base, ignore_errors=True)
        self.repo_path = make_repo(self.base)
        self.repo = coordination.discover(self.repo_path)
        self.assertIsNotNone(self.repo)


class TestMainRule(TempRepoCase):
    def test_edit_target_on_main_denied(self):
        target = os.path.join(self.repo_path, "seed.txt")
        reason = coordination.deny_reason_for_target(target, self.repo)
        self.assertIsNotNone(reason)
        self.assertIn("read-only", reason)

    def test_edit_target_on_branch_allowed(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        target = os.path.join(self.repo_path, "seed.txt")
        self.assertIsNone(coordination.deny_reason_for_target(target))

    def test_git_commit_on_main_denied(self):
        reason = coordination.deny_reason_for_git_argv(
            ["git", "commit", "-m", "x"], self.repo_path)
        self.assertIsNotNone(reason)

    def test_push_refspec_to_main_denied_from_any_branch(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        for argv in (["git", "push", "origin", "HEAD:main"],
                     ["git", "push", "origin", "feature:refs/heads/main"],
                     ["git", "push", "origin", "main"]):
            self.assertIsNotNone(
                coordination.deny_reason_for_git_argv(argv, self.repo_path),
                argv)

    def test_push_feature_branch_allowed(self):
        self.assertIsNone(coordination.deny_reason_for_git_argv(
            ["git", "push", "origin", "HEAD:feature"], self.repo_path))

    def test_global_options_do_not_hide_mutators(self):
        for argv in (["git", "-C", self.repo_path, "reset", "--hard"],
                     ["git", "-c", "user.name=x", "rebase", "feature"],
                     ["git", "--git-dir", ".git", "merge", "feature"]):
            self.assertIsNotNone(
                coordination.deny_reason_for_git_argv(argv, self.repo_path),
                argv)

    def test_force_push_shorthand_denied(self):
        self.assertIsNotNone(coordination.deny_reason_for_git_argv(
            ["git", "push", "origin", "+main"], self.repo_path))
        self.assertIsNotNone(coordination.deny_reason_for_git_argv(
            ["git", "push", "-f", "origin", "feature:main"], self.repo_path))

    def test_shell_redirect_onto_main_denied(self):
        reason = coordination.deny_reason_for_shell(
            "echo boom > seed.txt", self.repo_path, self.repo)
        self.assertIsNotNone(reason)

    def test_shell_segments_checked_independently(self):
        reason = coordination.deny_reason_for_shell(
            "ls && git commit -m x", self.repo_path, self.repo)
        self.assertIsNotNone(reason)

    def test_foreign_path_not_governed(self):
        outside = os.path.join(self.base, "elsewhere.txt")
        self.assertIsNone(coordination.deny_reason_for_target(outside, self.repo))


class TestStatelessGuarantee(TempRepoCase):
    """A destroyed registry must never turn a main write into an allow."""

    def corrupt(self, content):
        os.makedirs(os.path.dirname(self.repo.registry_path), exist_ok=True)
        with open(self.repo.registry_path, "w", encoding="utf-8") as fh:
            fh.write(content)

    def test_main_denied_with_corrupt_registry(self):
        for garbage in ("not json {", "[]", '{"wrong": true}'):
            self.corrupt(garbage)
            rc = coordination.cmd_check(self.repo, "s1", [])
            self.assertEqual(rc, coordination.DENY)

    def test_claim_blocked_with_corrupt_registry(self):
        self.corrupt("not json {")
        with self.assertRaises(coordination.RegistryError):
            coordination.claim(self.repo, "s1", "branch", "feature")


class TestClaims(TempRepoCase):
    def test_contested_claim_rejected_naming_winner(self):
        first = coordination.claim(self.repo, "s1", "branch", "feature")
        self.assertIn("claimed", first)
        second = coordination.claim(self.repo, "s2", "branch", "feature")
        self.assertIn("rejected", second)
        self.assertEqual(second["rejected"]["session"], "s1")

    def test_same_session_reclaim_allowed(self):
        coordination.claim(self.repo, "s1", "branch", "feature")
        again = coordination.claim(self.repo, "s1", "branch", "feature")
        self.assertIn("claimed", again)

    def test_race_exactly_one_winner(self):
        results = []
        def worker(session):
            results.append(coordination.claim(
                self.repo, session, "worktree", ".worktrees/claude+x"))
        threads = [threading.Thread(target=worker, args=(f"s{i}",))
                   for i in range(5)]
        for t in threads:
            t.start()
        for t in threads:
            t.join()
        winners = [r for r in results if "claimed" in r]
        self.assertEqual(len(winners), 1)

    def test_release_by_owner_and_contested_release(self):
        record = coordination.claim(
            self.repo, "s1", "branch", "feature")["claimed"]
        contested = coordination.release(self.repo, record["id"], session="s2")
        self.assertIn("contested", contested)
        released = coordination.release(self.repo, record["id"], session="s1")
        self.assertIn("released", released)

    def test_forced_release_requires_reason_and_audits(self):
        record = coordination.claim(
            self.repo, "s1", "branch", "feature")["claimed"]
        refused = coordination.release(self.repo, record["id"], force=True)
        self.assertIn("error", refused)
        forced = coordination.release(
            self.repo, record["id"], force=True, reason="owner: orphaned")
        self.assertIn("released", forced)
        data = json.load(open(self.repo.registry_path, encoding="utf-8"))
        self.assertEqual(len(data["audit"]), 1)

    def test_no_partial_registry_after_saves(self):
        for i in range(20):
            coordination.claim(self.repo, "s1", "file_glob", f"src/{i}/*")
        data = json.load(open(self.repo.registry_path, encoding="utf-8"))
        self.assertEqual(len(data["claims"]), 20)
        leftovers = [n for n in os.listdir(os.path.dirname(self.repo.registry_path))
                     if n.endswith(".tmp") or n.endswith(".lock")]
        self.assertEqual(leftovers, [])


class TestAdrAllocation(TempRepoCase):
    def setUp(self):
        super().setUp()
        adr_dir = os.path.join(self.repo_path, "docs", "adr")
        os.makedirs(adr_dir)
        with open(os.path.join(adr_dir, "0004-example.md"), "w",
                  encoding="utf-8") as fh:
            fh.write("# ADR-0004: Example\n")

    def test_allocates_above_existing_files(self):
        result = coordination.claim(self.repo, "s1", "adr", "")
        self.assertEqual(result["claimed"]["value"], "0005")

    def test_concurrent_allocations_distinct(self):
        results = []
        def worker(session):
            results.append(coordination.claim(self.repo, session, "adr", ""))
        threads = [threading.Thread(target=worker, args=(f"s{i}",))
                   for i in range(4)]
        for t in threads:
            t.start()
        for t in threads:
            t.join()
        numbers = sorted(r["claimed"]["value"] for r in results)
        self.assertEqual(numbers, ["0005", "0006", "0007", "0008"])

    def test_unused_allocation_gap_never_reissued(self):
        coordination.claim(self.repo, "s1", "adr", "")   # 0005, never a file
        nxt = coordination.claim(self.repo, "s2", "adr", "")
        self.assertEqual(nxt["claimed"]["value"], "0006")

    def test_adr_claims_cannot_be_released(self):
        record = coordination.claim(self.repo, "s1", "adr", "")["claimed"]
        result = coordination.release(self.repo, record["id"], session="s1")
        self.assertIn("error", result)


class TestRealGitHooks(TempRepoCase):
    """The hooks deny through real git in a disposable repository."""

    def install(self):
        hooks_src = os.path.join(os.path.dirname(MODULE_DIR), "..", ".githooks")
        hooks_src = os.path.abspath(hooks_src)
        dest = os.path.join(self.repo_path, ".githooks")
        shutil.copytree(hooks_src, dest)
        tools_dest = os.path.join(self.repo_path, "tools", "agent-coordination")
        os.makedirs(os.path.dirname(tools_dest), exist_ok=True)
        shutil.copytree(MODULE_DIR, tools_dest,
                        ignore=shutil.ignore_patterns("tests", "__pycache__"))
        run_git(["config", "core.hooksPath", ".githooks"], self.repo_path)

    def test_commit_on_main_denied_commit_on_branch_allowed(self):
        self.install()
        with open(os.path.join(self.repo_path, "new.txt"), "w",
                  encoding="utf-8") as fh:
            fh.write("x\n")
        run_git(["add", "."], self.repo_path)
        denied = run_git(["commit", "-m", "should fail"], self.repo_path)
        self.assertNotEqual(denied.returncode, 0)
        self.assertIn("read-only", denied.stderr + denied.stdout)
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        allowed = run_git(["commit", "-m", "ok"], self.repo_path)
        self.assertEqual(allowed.returncode, 0, allowed.stderr)

    def test_push_to_remote_main_denied(self):
        self.install()
        remote = os.path.join(self.base, "remote.git")
        run_git(["init", "-q", "--bare", "-b", "main"], self.base and self.base)
        # init --bare in self.base creates remote at cwd; redo precisely:
        shutil.rmtree(remote, ignore_errors=True)
        subprocess.run(["git", "init", "-q", "--bare", "-b", "main", remote],
                       check=True, capture_output=True)
        run_git(["remote", "add", "origin", remote], self.repo_path)
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        denied = run_git(["push", "origin", "HEAD:main"], self.repo_path)
        self.assertNotEqual(denied.returncode, 0)
        self.assertIn("pull request", denied.stderr + denied.stdout)
        allowed = run_git(["push", "origin", "HEAD:feature"], self.repo_path)
        self.assertEqual(allowed.returncode, 0, allowed.stderr)


class TestPreToolUseAdapter(TempRepoCase):
    def decide(self, payload):
        import io
        from contextlib import redirect_stdout
        buffer = io.StringIO()
        with redirect_stdout(buffer):
            rc = coordination.hook_pre_tool_use(json.dumps(payload))
        return rc, buffer.getvalue()

    def test_edit_on_main_emits_deny_json(self):
        rc, out = self.decide({
            "tool_name": "Edit",
            "tool_input": {"file_path": os.path.join(self.repo_path, "seed.txt")},
            "cwd": self.repo_path,
        })
        self.assertEqual(rc, coordination.ALLOW)  # exit 0; decision is JSON
        decision = json.loads(out)
        self.assertEqual(
            decision["hookSpecificOutput"]["permissionDecision"], "deny")

    def test_bash_push_to_main_emits_deny(self):
        rc, out = self.decide({
            "tool_name": "Bash",
            "tool_input": {"command": "git push origin HEAD:main"},
            "cwd": self.repo_path,
        })
        self.assertIn("deny", out)

    def test_benign_edit_on_branch_silent(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        rc, out = self.decide({
            "tool_name": "Edit",
            "tool_input": {"file_path": os.path.join(self.repo_path, "seed.txt")},
            "cwd": self.repo_path,
        })
        self.assertEqual(out, "")

    def test_malformed_payload_fails_open(self):
        rc = coordination.hook_pre_tool_use("this is not json")
        self.assertEqual(rc, coordination.ALLOW)


class TestSyncMain(TempRepoCase):
    def test_dirty_tree_refused_and_ff_succeeds(self):
        remote = os.path.join(self.base, "origin.git")
        subprocess.run(["git", "init", "-q", "--bare", "-b", "main", remote],
                       check=True, capture_output=True)
        run_git(["remote", "add", "origin", remote], self.repo_path)
        run_git(["push", "-q", "origin", "main"], self.repo_path)
        clone = os.path.join(self.base, "clone")
        subprocess.run(["git", "clone", "-q", remote, clone], check=True,
                       capture_output=True)
        run_git(["config", "user.email", "t@t"], clone)
        run_git(["config", "user.name", "t"], clone)
        with open(os.path.join(clone, "adv.txt"), "w", encoding="utf-8") as fh:
            fh.write("adv\n")
        run_git(["add", "."], clone)
        run_git(["commit", "-q", "-m", "advance"], clone)
        run_git(["push", "-q", "origin", "main"], clone)

        with open(os.path.join(self.repo_path, "dirty.txt"), "w",
                  encoding="utf-8") as fh:
            fh.write("dirty\n")
        run_git(["add", "dirty.txt"], self.repo_path)
        self.assertEqual(coordination.cmd_sync_main(self.repo),
                         coordination.DENY)
        run_git(["reset", "-q", "HEAD", "dirty.txt"], self.repo_path)
        os.remove(os.path.join(self.repo_path, "dirty.txt"))
        self.assertEqual(coordination.cmd_sync_main(self.repo),
                         coordination.ALLOW)


if __name__ == "__main__":
    unittest.main(verbosity=2)

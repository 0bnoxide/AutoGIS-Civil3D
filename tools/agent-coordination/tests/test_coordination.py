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
from unittest import mock

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

    def test_new_directory_target_under_main_denied(self):
        target = os.path.join(self.repo_path, "does", "not", "exist.txt")
        reason = coordination.deny_reason_for_target(target, self.repo)
        self.assertIsNotNone(reason)

    def test_force_push_shorthand_denied(self):
        self.assertIsNotNone(coordination.deny_reason_for_git_argv(
            ["git", "push", "origin", "+main"], self.repo_path))
        self.assertIsNotNone(coordination.deny_reason_for_git_argv(
            ["git", "push", "-f", "origin", "feature:main"], self.repo_path))

    def test_shell_redirect_onto_main_denied(self):
        reason = coordination.deny_reason_for_shell(
            "echo boom > seed.txt", self.repo_path, self.repo)
        self.assertIsNotNone(reason)

    def test_unexpanded_var_redirect_skipped(self):
        # Not evaluable without expansion: fail open, git hooks backstop.
        for cmd in ('echo x > "$TEMP/y.md"', "echo x > $TMPDIR/y",
                    "echo x > `mktemp`"):
            self.assertIsNone(coordination.deny_reason_for_shell(
                cmd, self.repo_path, self.repo), cmd)

    def test_quoted_literal_redirect_still_denied(self):
        for cmd in ('echo x > "seed.txt"', "echo x > 'seed.txt'"):
            self.assertIsNotNone(coordination.deny_reason_for_shell(
                cmd, self.repo_path, self.repo), cmd)

    def test_tilde_redirect_resolves_outside_repo(self):
        self.assertIsNone(coordination.deny_reason_for_shell(
            "echo x > ~/coord-test-elsewhere.txt", self.repo_path, self.repo))

    def test_shell_segments_checked_independently(self):
        reason = coordination.deny_reason_for_shell(
            "ls && git commit -m x", self.repo_path, self.repo)
        self.assertIsNotNone(reason)

    def test_cd_tracked_across_segments(self):
        other = make_repo(self.base, "other-main").replace(os.sep, "/")
        reason = coordination.deny_reason_for_shell(
            f"cd {other} && git reset --hard", self.base, None)
        self.assertIsNotNone(reason)

    def test_cd_inside_pipeline_does_not_move_later_segments(self):
        safe = os.path.join(self.base, "safe").replace(os.sep, "/")
        os.makedirs(safe, exist_ok=True)
        reason = coordination.deny_reason_for_shell(
            f"true | cd {safe} && git reset --hard", self.repo_path, self.repo)
        self.assertIsNotNone(reason)

    def test_worktree_and_gitdir_options_resolve_target_tree(self):
        for form in ([f"--work-tree={self.repo_path}"],
                     ["--work-tree", self.repo_path],
                     [f"--git-dir={os.path.join(self.repo_path, '.git')}"]):
            argv = ["git", *form, "reset", "--hard"]
            self.assertIsNotNone(
                coordination.deny_reason_for_git_argv(argv, self.base), argv)

    def test_restore_clean_and_checkout_pathspec_denied_on_main(self):
        for argv in (["git", "restore", "seed.txt"],
                     ["git", "clean", "-fd"],
                     ["git", "checkout", "--", "seed.txt"],
                     ["git", "checkout", "seed.txt"]):
            self.assertIsNotNone(
                coordination.deny_reason_for_git_argv(argv, self.repo_path),
                argv)
        for argv in (["git", "checkout", "-b", "feature2"],
                     ["git", "checkout", "some-branch"],
                     ["git", "switch", "-c", "feature3"]):
            self.assertIsNone(
                coordination.deny_reason_for_git_argv(argv, self.repo_path),
                argv)

    def test_chained_dash_c_accumulates_and_work_tree_wins(self):
        nested = os.path.join(self.repo_path, "sub")
        os.makedirs(nested, exist_ok=True)
        argv = ["git", "-C", os.path.basename(self.repo_path), "-C", "sub",
                "reset", "--hard"]
        self.assertIsNotNone(coordination.deny_reason_for_git_argv(
            argv, os.path.dirname(self.repo_path)), argv)
        argv = ["git", f"--git-dir={os.path.join(self.base, 'x', '.git')}",
                f"--work-tree={self.repo_path}", "reset", "--hard"]
        self.assertIsNotNone(
            coordination.deny_reason_for_git_argv(argv, self.base), argv)

    def test_tee_in_pipeline_stage_denied(self):
        reason = coordination.deny_reason_for_shell(
            "echo boom | tee seed.txt", self.repo_path, self.repo)
        self.assertIsNotNone(reason)

    def test_rm_on_main_denied(self):
        reason = coordination.deny_reason_for_shell(
            "rm -f seed.txt", self.repo_path, self.repo)
        self.assertIsNotNone(reason)

    def test_cp_destination_on_main_denied(self):
        for cmd in ("cp other.txt seed.txt", "mv other.txt seed.txt"):
            reason = coordination.deny_reason_for_shell(
                cmd, self.repo_path, self.repo)
            self.assertIsNotNone(reason, cmd)

    def test_mv_source_off_main_denied(self):
        # mv deletes its source with no commit; only the adapter sees it.
        outside = os.path.join(self.base, "elsewhere.txt").replace(os.sep, "/")
        reason = coordination.deny_reason_for_shell(
            f"mv seed.txt {outside}", self.repo_path, self.repo)
        self.assertIsNotNone(reason)

    def test_target_directory_flag_resolves_destination(self):
        for flag in ("-t", "--target-directory"):
            argv = ["mv", flag, "/dest", "a", "b"]
            self.assertEqual(coordination._copy_move_operands(argv),
                             ("/dest", ["a", "b"]), argv)
        self.assertEqual(
            coordination._copy_move_operands(
                ["cp", "--target-directory=/dest", "a"]),
            ("/dest", ["a"]))
        self.assertEqual(coordination._copy_move_operands(["mv", "a", "b"]),
                         ("b", ["a"]))
        for argv in (["cp", "-rt", "/dest", "a"], ["cp", "-rt/dest", "a"]):
            self.assertEqual(coordination._copy_move_operands(argv),
                             ("/dest", ["a"]), argv)

    def test_checkout_of_deleted_tracked_file_denied(self):
        os.remove(os.path.join(self.repo_path, "seed.txt"))
        reason = coordination.deny_reason_for_git_argv(
            ["git", "checkout", "seed.txt"], self.repo_path)
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

    def test_overlapping_file_glob_rejected(self):
        coordination.claim(self.repo, "s1", "file_glob", "src/*")
        overlap = coordination.claim(self.repo, "s2", "file_glob", "src/x/*")
        self.assertIn("rejected", overlap)
        disjoint = coordination.claim(self.repo, "s2", "file_glob", "docs/*")
        self.assertIn("claimed", disjoint)

    def test_adapter_fails_closed_on_corrupt_registry_with_session(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        self.repo = coordination.discover(self.repo_path)
        os.makedirs(os.path.dirname(self.repo.registry_path), exist_ok=True)
        with open(self.repo.registry_path, "w", encoding="utf-8") as fh:
            fh.write("not json {")
        import io
        from contextlib import redirect_stdout
        with mock.patch.dict(
                os.environ, {"AGENT_SESSION_ID": "s2"}):
            buffer = io.StringIO()
            with redirect_stdout(buffer):
                coordination.hook_pre_tool_use(json.dumps({
                    "tool_name": "Edit",
                    "tool_input": {"file_path": os.path.join(
                        self.repo_path, "seed.txt")},
                    "cwd": self.repo_path,
                }))
            self.assertIn("deny", buffer.getvalue())

    def test_check_requires_targets_inside_own_scope(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        self.repo = coordination.discover(self.repo_path)
        coordination.claim(self.repo, "s1", "branch", "feature")
        coordination.claim(self.repo, "s1", "file_glob", "src/*")
        inside = os.path.join(self.repo_path, "src", "a.cs")
        outside = os.path.join(self.repo_path, "docs", "x.md")
        self.assertEqual(
            coordination.cmd_check(self.repo, "s1", [inside]),
            coordination.ALLOW)
        self.assertEqual(
            coordination.cmd_check(self.repo, "s1", [outside]),
            coordination.DENY)

    def test_adapter_enforces_callers_own_scope(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        self.repo = coordination.discover(self.repo_path)
        coordination.claim(self.repo, "s1", "branch", "feature")
        coordination.claim(self.repo, "s1", "file_glob", "src/*")
        import io
        from contextlib import redirect_stdout
        with mock.patch.dict(
                os.environ, {"AGENT_SESSION_ID": "s1"}):
            buffer = io.StringIO()
            with redirect_stdout(buffer):
                coordination.hook_pre_tool_use(json.dumps({
                    "tool_name": "Write",
                    "tool_input": {"file_path": os.path.join(
                        self.repo_path, "docs", "x.md")},
                    "cwd": self.repo_path,
                }))
            self.assertIn("outside your claimed file scope", buffer.getvalue())
            buffer = io.StringIO()
            with redirect_stdout(buffer):
                coordination.hook_pre_tool_use(json.dumps({
                    "tool_name": "Write",
                    "tool_input": {"file_path": os.path.join(
                        self.repo_path, "src", "a.cs")},
                    "cwd": self.repo_path,
                }))
            self.assertEqual(buffer.getvalue(), "")

    def test_doctor_survives_broken_sync_script(self):
        stub_dir = os.path.join(self.repo_path, "tools", "agent-assets")
        os.makedirs(stub_dir, exist_ok=True)
        with open(os.path.join(stub_dir, "sync.py"), "w",
                  encoding="utf-8") as fh:
            fh.write("import sys; sys.exit(2)\n")
        import io
        from contextlib import redirect_stdout
        buffer = io.StringIO()
        with redirect_stdout(buffer):
            rc = coordination.cmd_doctor(self.repo)
        self.assertEqual(rc, coordination.ALLOW)
        self.assertIn("failed to run (advisory)", buffer.getvalue())

    def test_doctor_reports_asset_drift(self):
        stub_dir = os.path.join(self.repo_path, "tools", "agent-assets")
        os.makedirs(stub_dir, exist_ok=True)
        with open(os.path.join(stub_dir, "sync.py"), "w",
                  encoding="utf-8") as fh:
            fh.write("import sys; print('sync --check: DRIFT'); sys.exit(1)\n")
        import io
        from contextlib import redirect_stdout
        buffer = io.StringIO()
        with redirect_stdout(buffer):
            coordination.cmd_doctor(self.repo)
        self.assertIn("agent-asset drift", buffer.getvalue())

    def test_sibling_prefixes_do_not_conflict(self):
        coordination.claim(self.repo, "s1", "file_glob", "src/*")
        for ok in ("srclib/*", "src2", "docs/srcnotes/*"):
            result = coordination.claim(self.repo, "s2", "file_glob", ok)
            self.assertIn("claimed", result, ok)
        coordination.claim(self.repo, "s1", "file_glob", "a/main")
        sibling = coordination.claim(self.repo, "s2", "file_glob", "a/main2")
        self.assertIn("claimed", sibling)

    def test_arbitrary_glob_patterns_rejected_at_claim(self):
        for bad in ("src/*.cs", "src/test*", "**/x", "a?b"):
            result = coordination.claim(self.repo, "s1", "file_glob", bad)
            self.assertIn("error", result, bad)

    def test_adapter_denies_shell_redirect_into_claimed_glob(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        self.repo = coordination.discover(self.repo_path)
        coordination.claim(self.repo, "s1", "file_glob", "src/*")
        import io
        from contextlib import redirect_stdout
        with mock.patch.dict(
                os.environ, {"AGENT_SESSION_ID": "s2"}):
            buffer = io.StringIO()
            with redirect_stdout(buffer):
                coordination.hook_pre_tool_use(json.dumps({
                    "tool_name": "Bash",
                    "tool_input": {"command": "echo boom > src/code.cs"},
                    "cwd": self.repo_path,
                }))
            self.assertIn("deny", buffer.getvalue())

    def test_check_denies_target_in_another_sessions_glob(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        self.repo = coordination.discover(self.repo_path)  # branch changed
        coordination.claim(self.repo, "s1", "branch", "feature")
        coordination.claim(self.repo, "s1", "file_glob", "src/*")
        target = os.path.join(self.repo_path, "src", "code.cs")
        rc = coordination.cmd_check(self.repo, "s2", [target])
        self.assertEqual(rc, coordination.DENY)
        rc = coordination.cmd_check(self.repo, "s1", [target])
        self.assertEqual(rc, coordination.ALLOW)

    def test_check_denies_unclaimed_branch(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        self.repo = coordination.discover(self.repo_path)
        rc = coordination.cmd_check(self.repo, "s1", [])
        self.assertEqual(rc, coordination.DENY)
        coordination.claim(self.repo, "s1", "branch", "feature")
        rc = coordination.cmd_check(self.repo, "s1", [])
        self.assertEqual(rc, coordination.ALLOW)

    def test_release_by_owner_and_contested_release(self):
        record = coordination.claim(
            self.repo, "s1", "branch", "feature")["claimed"]
        contested = coordination.release(self.repo, record["id"], session="s2")
        self.assertIn("contested", contested)
        released = coordination.release(self.repo, record["id"], session="s1")
        self.assertIn("released", released)

    def test_release_without_session_refused(self):
        record = coordination.claim(
            self.repo, "s1", "branch", "feature")["claimed"]
        result = coordination.release(self.repo, record["id"])
        self.assertIn("error", result)

    def test_adapter_denies_edit_in_another_sessions_glob(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        self.repo = coordination.discover(self.repo_path)
        coordination.claim(self.repo, "s1", "file_glob", "src/*")
        import io
        from contextlib import redirect_stdout
        with mock.patch.dict(
                os.environ, {"AGENT_SESSION_ID": "s2"}):
            buffer = io.StringIO()
            with redirect_stdout(buffer):
                coordination.hook_pre_tool_use(json.dumps({
                    "tool_name": "Edit",
                    "tool_input": {"file_path": os.path.join(
                        self.repo_path, "src", "a.cs")},
                    "cwd": self.repo_path,
                }))
            self.assertIn("deny", buffer.getvalue())

    def test_glob_matches_from_linked_worktree(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        self.repo = coordination.discover(self.repo_path)
        coordination.claim(self.repo, "s1", "file_glob", "src/*")
        wt = os.path.join(self.repo_path, ".worktrees", "claude+wt")
        run_git(["worktree", "add", "-q", wt, "-b", "wt-branch"],
                self.repo_path)
        target = os.path.join(wt, "src", "a.cs")
        conflict = coordination.glob_conflict(
            coordination.list_claims(self.repo), "s2", [target])
        self.assertIsNotNone(conflict)

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

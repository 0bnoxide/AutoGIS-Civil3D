"""Phase 0 coordination tests: decision matrix, registry integrity, hooks.

Standard library unittest only. Every test builds disposable repositories
under a temp directory — the real primary worktree is never a target.
"""

import json
import io
import os
import shutil
import subprocess
import sys
import tempfile
import threading
import unittest
from contextlib import redirect_stderr, redirect_stdout
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


class TestCodexHookTrust(TempRepoCase):
    def write_hook_trust_evidence(self, content):
        evidence_dir = os.path.join(
            self.repo_path, "docs", "verification")
        os.makedirs(evidence_dir, exist_ok=True)
        with open(os.path.join(evidence_dir, "codex-project-hook-trust.md"),
                  "w", encoding="utf-8") as fh:
            fh.write(content)

    def doctor_output(self):
        buffer = io.StringIO()
        with redirect_stdout(buffer):
            rc = coordination.cmd_doctor(self.repo)
        self.assertEqual(rc, coordination.ALLOW)
        return buffer.getvalue()

    def test_doctor_reports_valid_hook_trust_evidence_as_verified(self):
        self.write_hook_trust_evidence(
            "Verification date: 2026-08-05\n"
            "Hooks inspection: passed\n"
            "Activation probe: passed")

        self.assertIn("Codex project-hook trust: verified (2026-08-05)",
                      self.doctor_output())

    def test_doctor_reports_missing_hook_trust_evidence_as_unverified(self):
        self.assertIn("Codex project-hook trust: unverified",
                      self.doctor_output())

    def test_doctor_reports_invalid_hook_trust_date_as_unverified(self):
        self.write_hook_trust_evidence("Verification date: invalid")

        self.assertIn("Codex project-hook trust: unverified",
                      self.doctor_output())

    def test_doctor_reports_incomplete_hook_trust_evidence_as_unverified(self):
        self.write_hook_trust_evidence(
            "Verification date: 2026-08-05\n"
            "Hooks inspection: passed")

        self.assertIn("Codex project-hook trust: unverified",
                      self.doctor_output())


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

    def test_git_worktree_mutators_denied_on_main(self):
        # rm/mv/stash/apply mutate the tree with no commit, so no
        # pre-commit backstop (#42).
        for argv in (["git", "rm", "seed.txt"],
                     ["git", "mv", "seed.txt", "other.txt"],
                     ["git", "stash"],
                     ["git", "apply", "p.patch"]):
            self.assertIsNotNone(
                coordination.deny_reason_for_git_argv(argv, self.repo_path),
                argv)

    def test_powershell_write_cmdlets_denied_on_main(self):
        # PowerShell write forms reach the same rule as POSIX ones (#41).
        for cmd in ("Set-Content -Path seed.txt -Value pwned",
                    "Remove-Item seed.txt",
                    "'x' | Out-File seed.txt",
                    "New-Item -ItemType File -Force seed.txt",
                    "Copy-Item other.txt seed.txt",
                    "Move-Item seed.txt ../elsewhere.txt",
                    "Rename-Item seed.txt pwned.md",
                    "'x' | Tee-Object seed.txt",
                    "rd seed.txt"):
            self.assertIsNotNone(coordination.deny_reason_for_shell(
                cmd, self.repo_path, self.repo, ps=True), cmd)

    def test_powershell_sc_alias_denied_on_main(self):
        # `sc` is Set-Content under Windows PowerShell 5.1; dropping it
        # would reopen the #41 gap there.
        for cmd in ("sc README.md pwned", "sc -Path seed.txt -Value x"):
            self.assertIsNotNone(coordination.deny_reason_for_shell(
                cmd, self.repo_path, self.repo, ps=True), cmd)

    def test_powershell_provider_paths_allowed_on_main(self):
        # Env:/Variable:/Alias: targets are provider items, not files.
        for cmd in ("Set-Item Env:FOO bar", "Clear-Item Variable:x",
                    "Set-Content -Path Env:FOO -Value bar"):
            self.assertIsNone(coordination.deny_reason_for_shell(
                cmd, self.repo_path, self.repo, ps=True), cmd)

    def test_powershell_backslash_path_denied(self):
        # A Windows-separator absolute path must not be eaten by the
        # POSIX tokenizer (cold-review P1).
        target = os.path.join(self.repo_path, "seed.txt").replace("/", "\\")
        self.assertIsNotNone(coordination.deny_reason_for_shell(
            f"Set-Content -Path {target} -Value pwned",
            self.repo_path, self.repo, ps=True))

    def test_powershell_colon_binding_denied(self):
        # `-Path:file` carries its value inline (cold-review P1).
        self.assertIsNotNone(coordination.deny_reason_for_shell(
            "Set-Content -Path:seed.txt -Value pwned",
            self.repo_path, self.repo, ps=True))

    def test_powershell_cmdlets_ignored_for_bash_payloads(self):
        # `sc`, `copy`, `del`... are legitimate command names under other
        # shells; the cmdlet layer applies to PowerShell payloads only.
        for cmd in ("sc query eventlog", "del something", "ni hello"):
            self.assertIsNone(coordination.deny_reason_for_shell(
                cmd, self.repo_path, self.repo), cmd)

    def test_git_apply_build_fake_ancestor_denied(self):
        # --build-fake-ancestor writes a file even under --check.
        self.assertIsNotNone(coordination.deny_reason_for_git_argv(
            ["git", "apply", "--check", "--build-fake-ancestor", "out",
             "p.patch"], self.repo_path))

    def test_git_read_only_forms_allowed_on_main(self):
        for argv in (["git", "stash", "list"],
                     ["git", "stash", "show"],
                     ["git", "apply", "--check", "p.patch"],
                     ["git", "apply", "--stat", "p.patch"],
                     ["git", "rm", "--dry-run", "seed.txt"],
                     ["git", "rm", "-n", "seed.txt"]):
            self.assertIsNone(
                coordination.deny_reason_for_git_argv(argv, self.repo_path),
                argv)

    def test_git_read_only_lookalikes_still_denied_on_main(self):
        # A stash message naming "list" is still a push; --apply forces
        # an apply despite --check-style flags.
        for argv in (["git", "stash", "push", "-m", "list"],
                     ["git", "apply", "--stat", "--apply", "p.patch"]):
            self.assertIsNotNone(
                coordination.deny_reason_for_git_argv(argv, self.repo_path),
                argv)

    def test_powershell_unmodeled_switch_does_not_swallow_path(self):
        # -NoNewline is not modeled; the unknown-parameter default must
        # fail toward deny, not consume the path token.
        self.assertIsNotNone(coordination.deny_reason_for_shell(
            "Out-File -NoNewline seed.txt", self.repo_path, self.repo,
            ps=True))

    def test_powershell_copy_from_main_allowed(self):
        # Copy-class cmdlets write only their destination; reading a file
        # off main is not a mutation.
        self.assertIsNone(coordination.deny_reason_for_shell(
            "Copy-Item seed.txt ~/coord-test-elsewhere.txt",
            self.repo_path, self.repo, ps=True))

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

    def test_quoted_message_text_yields_no_targets(self):
        # Words, newlines, and <angle>+ markup inside a quoted -m message
        # must never surface as segments or write targets.
        cmd = ('git add real.py && git commit -m "fix: accept '
               '.claude/worktrees/<slug> and that\n'
               'location > and other words; rm -rf nothing\n'
               'end."')
        self.assertEqual(
            coordination.shell_write_targets(cmd, self.repo_path), [])
        events = list(coordination._shell_events(cmd, self.repo_path))
        self.assertEqual([e[1][0] for e in events if e[0] == "git"],
                         ["git", "git"])

    def test_heredoc_body_yields_no_targets(self):
        cmd = ("gh pr create --body-file - <<'EOF'\n"
               "- `x`: alongside `.worktrees/<agent>+<slug>` stuff\n"
               "echo boom > seed.txt\n"
               "EOF")
        self.assertEqual(
            coordination.shell_write_targets(cmd, self.repo_path), [])

    def test_herestring_does_not_mask_later_commands(self):
        # <<< is a here-string, not a heredoc: it must not swallow the
        # rest of the command as a phantom heredoc body.
        targets = coordination.shell_write_targets(
            'grep x <<<"data"\necho hi > protected.txt', self.repo_path)
        self.assertEqual([os.path.basename(t) for t in targets],
                         ["protected.txt"])

    def test_escaped_quote_outside_string_pins_behavior(self):
        # \" outside a string opens _QUOTE_RE's double-quote branch,
        # diverging from shell semantics; pin that the redirect after the
        # closing quote is still seen (advisory, PR #26 review P3).
        targets = coordination.shell_write_targets(
            'echo \\"x\\" > out.txt', self.repo_path)
        self.assertEqual([os.path.basename(t) for t in targets], ["out.txt"])

    def test_redirect_after_quoted_arg_still_detected(self):
        targets = coordination.shell_write_targets(
            'echo "a > b" > out.txt', self.repo_path)
        self.assertEqual([os.path.basename(t) for t in targets], ["out.txt"])

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

    def test_git_working_tree_plumbing_denied_on_main(self):
        # #45: index/worktree writers with no commit backstop, and a forced
        # switch that discards the main working tree.
        for argv in (["git", "read-tree", "-u", "--reset", "HEAD"],
                     ["git", "checkout-index", "-a", "-f"],
                     ["git", "sparse-checkout", "set", "foo"],
                     ["git", "pull"],
                     ["git", "switch", "--discard-changes", "main"],
                     ["git", "switch", "-f", "main"],
                     # bundled short flags must not hide the force (cold review)
                     ["git", "switch", "-fq", "main"],
                     ["git", "switch", "-qf", "main"]):
            self.assertIsNotNone(
                coordination.deny_reason_for_git_argv(argv, self.repo_path),
                argv)

    def test_git_ref_movers_targeting_main_denied_from_any_tree(self):
        # #45: ref movers shift the shared main ref regardless of the cwd's
        # branch, so they deny even called from a non-repo directory.
        for argv in (["git", "update-ref", "refs/heads/main", "HEAD~1"],
                     ["git", "update-ref", "-d", "refs/heads/main"],
                     ["git", "branch", "-f", "main", "HEAD~1"],
                     ["git", "branch", "-D", "main"],
                     ["git", "branch", "-M", "main"],
                     ["git", "symbolic-ref", "HEAD", "refs/heads/main"],
                     # bundled short flags hide the destructive letter, and
                     # update-ref --stdin carries the ref out of argv entirely
                     # (cold review P1/P2):
                     ["git", "branch", "-Df", "main"],
                     ["git", "branch", "-fD", "main"],
                     ["git", "branch", "-Mf", "main"],
                     ["git", "update-ref", "--stdin"]):
            self.assertIsNotNone(
                coordination.deny_reason_for_git_argv(argv, self.base), argv)

    def test_git_safe_ref_and_switch_forms_allowed(self):
        for argv in (["git", "switch", "feature"],
                     ["git", "switch", "-c", "newfeature"],
                     ["git", "switch", "-q", "feature"],  # quiet, no force
                     ["git", "branch"],
                     ["git", "branch", "newfeature"],
                     ["git", "branch", "-D", "oldfeature"],
                     ["git", "branch", "-av"],  # list all/verbose bundle
                     ["git", "sparse-checkout", "list"],
                     ["git", "update-ref", "refs/heads/feature", "HEAD"],
                     ["git", "symbolic-ref", "HEAD"]):
            self.assertIsNone(
                coordination.deny_reason_for_git_argv(argv, self.repo_path),
                argv)

    def test_prefix_wrappers_do_not_hide_mutators(self):
        # #46: sudo/env/nice/timeout/xargs/command run the real command in
        # their trailing argv; the argv[0]-keyed checks must see through them.
        for cmd in ("sudo git reset --hard",
                    "env X=1 git reset --hard",
                    "nice git reset --hard",
                    "nice -n 5 git reset --hard",
                    "timeout 5 git reset --hard",
                    "xargs git reset --hard",
                    "command git reset --hard",
                    "sudo rm seed.txt",
                    "sudo bash -c 'rm seed.txt'"):
            self.assertIsNotNone(coordination.deny_reason_for_shell(
                cmd, self.repo_path, self.repo), cmd)

    def test_interpreter_c_strings_are_reparsed(self):
        # #46: bash -c / eval / sh -c wrap the real command in a quoted arg
        # that _mask_literals blanks; recurse into it.
        for cmd in ('bash -c "git reset --hard"',
                    "sh -c 'rm seed.txt'",
                    'eval "git reset --hard"',
                    # combined short-flag clusters ending in c (cold review)
                    'bash -lc "git reset --hard"',
                    "sh -ec 'rm seed.txt'",
                    'zsh -xc "git reset --hard"'):
            self.assertIsNotNone(coordination.deny_reason_for_shell(
                cmd, self.repo_path, self.repo), cmd)

    def test_powershell_interpreter_wrappers_reparsed(self):
        # #46, PowerShell adapter: -Command, iex, and the call-operator block.
        for cmd in ('pwsh -Command "Remove-Item seed.txt"',
                    'iex "Remove-Item seed.txt"',
                    "& { Remove-Item seed.txt }",
                    # fused braces / no surrounding space (cold review)
                    "&{Remove-Item seed.txt}",
                    "& {Remove-Item seed.txt}",
                    ". { Remove-Item seed.txt }"):
            self.assertIsNotNone(coordination.deny_reason_for_shell(
                cmd, self.repo_path, self.repo, ps=True), cmd)

    def test_wrapper_unwrapping_does_not_over_deny(self):
        # Benign wrapped commands, and the documented residual (a language
        # interpreter cannot be reparsed), must stay allowed.
        allowed = [("sudo ls", False),
                   ('bash -c "git log"', False),
                   ("timeout 5 echo hi", False),
                   ("python -c \"open('seed.txt','w')\"", False),  # residual
                   ("& { Get-Content seed.txt }", True)]
        for cmd, ps in allowed:
            self.assertIsNone(coordination.deny_reason_for_shell(
                cmd, self.repo_path, self.repo, ps=ps), cmd)

    def test_git_dry_run_and_stash_object_forms_allowed_on_main(self):
        # #45: these touch no working tree or index, so the mutator gate
        # over-denied them; keep them allowed on main.
        for argv in (["git", "clean", "-n"],
                     ["git", "clean", "--dry-run"],
                     ["git", "mv", "-n", "a", "b"],
                     ["git", "stash", "create"],
                     ["git", "stash", "store", "deadbeef"]):
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

    def test_doctor_worktree_naming(self):
        import io
        from contextlib import redirect_stdout
        for wt_rel, branch, ok in (
            (os.path.join(".worktrees", "claude+good"), "wt-good", True),
            (os.path.join(".claude", "worktrees", "native-slug"),
             "wt-native", True),
            (os.path.join("stray", "spot"), "wt-stray", False),
        ):
            run_git(["worktree", "add", "-q",
                     os.path.join(self.repo_path, wt_rel), "-b", branch],
                    self.repo_path)
            buffer = io.StringIO()
            with redirect_stdout(buffer):
                coordination.cmd_doctor(self.repo)
            flagged = f"naming convention: {wt_rel.replace(os.sep, '/')}" \
                in buffer.getvalue().replace("\\", "/")
            self.assertEqual(flagged, not ok, wt_rel)
            run_git(["worktree", "remove", "--force",
                     os.path.join(self.repo_path, wt_rel)], self.repo_path)

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

    def test_claim_cli_rejected_glob_exits_misuse(self):
        # #39: the claim CLI branch must surface claim()'s {"error": ...} as a
        # MISUSE exit with the message, not crash with KeyError on "claimed".
        buffer = io.StringIO()
        with mock.patch.object(coordination, "discover", return_value=self.repo):
            with redirect_stderr(buffer):
                rc = coordination.main(
                    ["claim", "--session", "s1",
                     "--kind", "file_glob", "--value", "docs/**"])
        self.assertEqual(rc, coordination.MISUSE)
        self.assertIn("literal path", buffer.getvalue())

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

    def test_lock_retries_through_windows_delete_pending(self):
        # Windows reports a delete-pending lock file as PermissionError, not
        # FileExistsError. Contention must retry, not surface a traceback.
        real_open = os.open
        denials = [PermissionError(13, "Permission denied")]

        def flaky_open(path, flags, *args):
            if str(path).endswith(coordination.LOCK_SUFFIX) and denials:
                raise denials.pop()
            return real_open(path, flags, *args)

        with mock.patch.object(os, "open", flaky_open):
            record = coordination.claim(
                self.repo, "s1", "branch", "feature")["claimed"]
        self.assertEqual(record["value"], "feature")
        self.assertEqual(denials, [])

    def test_lock_reports_a_persistent_permission_error(self):
        # A denial that outlives the deadline is an unwritable state
        # directory, not contention: say so instead of blaming a holder.
        def always_denied(path, flags, *args):
            raise PermissionError(13, "Permission denied")

        with mock.patch.object(os, "open", always_denied):
            with self.assertRaises(coordination.RegistryError) as caught:
                with coordination._Lock(self.repo.registry_path, timeout=0.0):
                    pass
        self.assertIn("Permission denied", str(caught.exception))
        self.assertIn("not creatable", str(caught.exception))

    def test_lock_does_not_retry_a_failure_after_it_holds_the_lock(self):
        # A write failure means this process already holds the lock:
        # retrying would make it contend with its own lock file.
        def failing_write(fd, data):
            raise PermissionError(13, "Permission denied")

        with mock.patch.object(os, "write", failing_write):
            with self.assertRaises(coordination.RegistryError) as caught:
                with coordination._Lock(self.repo.registry_path, timeout=5.0):
                    pass
        # RegistryError is not an OSError, and main() catches only the
        # former: a bare OSError here would crash the CLI (#54 review).
        self.assertIsInstance(caught.exception.__cause__, PermissionError)
        # ...and it must not leave the lock file behind for nobody to release.
        self.assertFalse(
            os.path.exists(self.repo.registry_path + coordination.LOCK_SUFFIX))


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

    def test_main_routes_pre_tool_use_from_foreign_process_cwd(self):
        # #47: when the harness runs the hook process from outside any repo,
        # main()'s process-cwd discover() is None. pre-tool-use governs the
        # PAYLOAD cwd, so it must still be dispatched (not fail open) — a write
        # into the main checkout must deny. The git hooks always run in-repo,
        # so only pre-tool-use needs this.
        import io
        from contextlib import redirect_stdout
        payload = json.dumps({
            "tool_name": "Write",
            "tool_input": {"file_path": os.path.join(self.repo_path, "x.txt")},
            "cwd": self.repo_path,
        })
        foreign = tempfile.mkdtemp(prefix="coord-foreign-")
        self.addCleanup(shutil.rmtree, foreign, ignore_errors=True)
        old = os.getcwd()
        os.chdir(foreign)
        self.addCleanup(os.chdir, old)
        buffer = io.StringIO()
        with mock.patch.object(sys, "stdin", io.StringIO(payload)):
            with redirect_stdout(buffer):
                coordination.main(["hook", "pre-tool-use"])
        self.assertIn("deny", buffer.getvalue())


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

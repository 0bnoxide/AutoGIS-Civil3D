"""Phase 0 coordination tests: decision matrix, registry integrity, hooks.

Standard library unittest only. Every test builds disposable repositories
under a temp directory — the real primary worktree is never a target.
"""

import datetime as _dt
import hashlib
import json
import io
import os
import shlex
import shutil
import socket
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

    def doctor_output(self):
        buffer = io.StringIO()
        with redirect_stdout(buffer):
            rc = coordination.cmd_doctor(self.repo)
        self.assertEqual(rc, coordination.ALLOW)
        return buffer.getvalue()


class TestCodexHookTrust(TempRepoCase):
    def write_hook_trust_evidence(self, content, root=None):
        evidence_dir = os.path.join(
            root or self.repo_path, "docs", "verification")
        os.makedirs(evidence_dir, exist_ok=True)
        with open(os.path.join(evidence_dir, "codex-project-hook-trust.md"),
                  "w", encoding="utf-8") as fh:
            fh.write(content)

    def write_hooks_config(self, root, content):
        os.makedirs(os.path.join(root, ".codex"), exist_ok=True)
        with open(os.path.join(root, ".codex", "hooks.json"), "wb") as fh:
            fh.write(content)
        return hashlib.sha256(content).hexdigest()

    def write_complete_evidence(self, digest, root=None):
        self.write_hook_trust_evidence(
            "Verification date: 2026-08-05\n"
            "Hooks inspection: passed\n"
            "Activation probe: passed\n"
            f"Hooks config SHA-256: {digest}", root)

    def test_doctor_reports_valid_hook_trust_evidence_as_verified(self):
        self.write_complete_evidence(
            self.write_hooks_config(self.repo_path, b"{}\n"))

        self.assertIn("Codex project-hook trust: verified (2026-08-05)",
                      self.doctor_output())

    def test_doctor_reports_changed_hooks_config_as_stale(self):
        self.write_complete_evidence(hashlib.sha256(b"old").hexdigest())
        self.write_hooks_config(self.repo_path, b"{}\n")

        output = self.doctor_output()
        self.assertNotIn("Codex project-hook trust: verified", output)
        self.assertIn("Codex project-hook trust: stale", output)
        # Trust is per hook definition (#212): no blanket re-trust advice.
        self.assertIn("review the changed hook definitions", output)
        self.assertNotIn("re-trust", output)

    def test_doctor_writes_utf8_when_piped_under_a_legacy_encoding(self):
        # #195: a Windows pipe defaults stdout to cp1252.
        self.write_complete_evidence(hashlib.sha256(b"old").hexdigest())
        self.write_hooks_config(self.repo_path, b"{}\n")
        env = dict(os.environ, PYTHONIOENCODING="cp1252")
        result = subprocess.run(
            [sys.executable, os.path.join(MODULE_DIR, "coordination.py"),
             "doctor"], cwd=self.repo_path, env=env, capture_output=True,
            check=False)
        self.assertEqual(result.returncode, coordination.ALLOW, result.stderr)
        self.assertIn("Codex project-hook trust: stale \u2014",
                      result.stdout.decode("utf-8"))

    def test_doctor_reports_record_without_config_hash_as_unverified(self):
        self.write_hooks_config(self.repo_path, b"{}\n")
        self.write_hook_trust_evidence(
            "Verification date: 2026-08-05\n"
            "Hooks inspection: passed\n"
            "Activation probe: passed")

        self.assertIn("Codex project-hook trust: unverified",
                      self.doctor_output())

    def test_doctor_reports_missing_hooks_config_as_unverified(self):
        self.write_complete_evidence(hashlib.sha256(b"{}\n").hexdigest())

        self.assertIn("Codex project-hook trust: unverified",
                      self.doctor_output())

    def test_doctor_hashes_primary_checkout_hooks_config(self):
        digest = self.write_hooks_config(self.repo_path, b"{}\n")
        run_git(["add", "."], self.repo_path)
        run_git(["commit", "-q", "-m", "hooks"], self.repo_path)
        wt = os.path.join(self.base, "wt")
        run_git(["worktree", "add", "-q", wt, "-b", "wt-branch"],
                self.repo_path)
        self.write_hooks_config(wt, b'{"changed": true}\n')
        self.write_complete_evidence(digest, root=wt)
        self.repo = coordination.discover(wt)

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

    def test_pathed_git_executables_do_not_hide_mutators(self):
        for executable in ("/usr/bin/git", "C:\\Program Files\\Git\\bin\\git.exe"):
            self.assertIsNotNone(
                coordination.deny_reason_for_git_argv(
                    [executable, "reset", "--hard"], self.repo_path),
                executable)
        self.assertIsNone(coordination.deny_reason_for_git_argv(
            ["/opt/git/git-helper", "reset", "--hard"], self.repo_path))

    def test_windows_trailing_dot_space_git_does_not_hide_mutators(self):
        # #76: Windows path resolution strips trailing dots and spaces, so
        # `git.`, `git ` (trailing), and `git.exe.` all execute as git.exe,
        # while the final path component here is a distinct token. Fail
        # toward deny, like the pathed forms above.
        for executable in ("git.", "git ", "git.exe.",
                           "C:\\Program Files\\Git\\bin\\git.exe."):
            self.assertIsNotNone(
                coordination.deny_reason_for_git_argv(
                    [executable, "reset", "--hard"], self.repo_path),
                executable)
        # End-to-end via the PowerShell adapter -- the path the issue names.
        self.assertIsNotNone(coordination.deny_reason_for_shell(
            "git. reset --hard", self.repo_path, self.repo, ps=True))

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

    def test_cd_into_unexpanded_var_skips_relative_targets(self):
        # #56: `cd "$D"` sets an unresolvable effective cwd; a later plain
        # relative target (no `$` of its own) must not be measured as a bogus
        # repo-relative path and false-denied as out of file scope.
        self.assertEqual(coordination.shell_write_targets(
            'cd "$D"\nprintf x > Bad.cs', self.repo_path), [])

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

    def test_directory_stack_commands_move_the_shell(self):
        # #183: pushd/popd and PowerShell's location cmdlets move the shell
        # like cd; popd returns to where pushd left.
        main = self.repo_path.replace(os.sep, "/")
        safe = os.path.join(self.base, "safe").replace(os.sep, "/")
        os.makedirs(safe, exist_ok=True)
        denied = (
            (f"pushd {main} && git reset --hard", {}),
            (f"pushd {main} & git reset --hard", {"cmd": True}),
            (f"pushd /d {main} & git reset --hard", {"cmd": True}),
            (f"Push-Location {main}; git reset --hard", {"ps": True}),
            (f"Set-Location -Path {main}; Remove-Item seed.txt", {"ps": True}),
            (f"sl {main}; git reset --hard", {"ps": True}),
            (f"CD {main}; git reset --hard", {"ps": True}),
            (f"pushd {safe} && popd && git reset --hard", {}),
            (f"pushd {safe} && pushd {main} && pushd && pushd && "
             "git reset --hard", {}),
            (f"pushd {safe}; pushd /tmp; dirs -c; cd {main}; popd; "
             "git reset --hard", {}),
            ("command -v pushd /tmp; git reset --hard", {}),
            (f"if cd {main}; then git reset --hard; fi", {}),
            (f"{{ cd {main}; }}; git reset --hard", {}),
            (f"echo $(cd {main}; git reset --hard)", {}),
            (f"case x in x) cd {main};; esac; git reset --hard", {}),
            (f"cd {safe} {main}; git reset --hard", {}),
            (f"& {{ Set-Location {main} }}; git reset --hard", {"ps": True}),
            (f"if ($true) {{ cd {main} }}; git reset --hard", {"ps": True}),
            (f"if exist x cd {main} & git reset --hard", {"cmd": True}),
            (f"command -p -- pushd {main} && git reset --hard", {}),
            (f"command -p pushd {main} && git reset --hard", {}),
            (f"X=1 pushd {main}; git reset --hard", {}),
            ("builtin -p pushd /tmp; git reset --hard", {}),
            ("dirs -c > seed.txt", {}),
            (f"& Set-Location {main}; git reset --hard", {"ps": True}),
            (f". Set-Location {main}; git reset --hard", {"ps": True}),
            (f"eval 'cd {main}'; git reset --hard", {}),
            (f"iex 'cd {main}'; git reset --hard", {"ps": True}),
            (f"Set-Alias c Set-Location; c {main}; git reset --hard",
             {"ps": True}),
            (f"$ExecutionContext.SessionState.Path.SetLocation('{main}'); "
             "git reset --hard", {"ps": True}),
            (f"cd {main}; cd {safe}; cd ~-; git reset --hard", {}),
            (f"@cd {main} & git reset --hard", {"cmd": True}),
            (f"(cd {main}) & git reset --hard", {"cmd": True}),
            (f"pushd {safe}; cd /tmp; pushd >/dev/null; git reset --hard", {}),
            (f"cd {safe} > /dev/null 2>&1; pushd {main} 2>$null; "
             "git reset --hard", {"ps": True}),
            (f"cd {safe} >nul & cd {main} 2>nul & git reset --hard",
             {"cmd": True}),
            ("cd /tmp > seed.txt", {}),
            (f"Microsoft.PowerShell.Management\\Set-Location {main}; "
             "git reset --hard", {"ps": True}),
            (f"Set-Location FileSystem::{main}; git reset --hard",
             {"ps": True}),
            (f"Set-Location {main[:-1]}*; git reset --hard", {"ps": True}),
        )
        for command, kw in denied:
            with self.subTest(command=command):
                self.assertIsNotNone(coordination.deny_reason_for_shell(
                    command, safe if main in command else self.repo_path,
                    None, **kw))
        # Forms the parser does not model fail closed (#183 review): pushd -n
        # keeps the cwd, -StackName selects another stack, cd - returns to
        # OLDPWD. Common parameters are deliberately unmodeled here.
        for command, kw in (
                (f"pushd -n {safe} && git reset --hard", {}),
                (f"pushd {safe} && pushd +1 && git reset --hard", {}),
                (f"cd {safe} && cd - && git reset --hard", {}),
                (f"pushd /d {safe} & git reset --hard", {"cmd": True}),
                (f"Push-Location {safe}; if ($true) {{ Pop-Location }}; "
                 "git reset --hard", {"ps": True}),
                (f"if ($true) {{ Push-Location {safe} }}; Pop-Location; "
                 "git reset --hard", {"ps": True}),
                (f"Push-Location {safe} -StackName A; Push-Location /x "
                 "-StackName B; Pop-Location -StackName A; git reset --hard",
                 {"ps": True})):
            with self.subTest(command=command):
                self.assertIsNotNone(coordination.deny_reason_for_shell(
                    command, self.repo_path, self.repo, **kw))
        self.assertIsNotNone(coordination.deny_reason_for_shell(
            f"Set-Location -ErrorAction Stop -Path {main}; git reset --hard",
            safe, None, ps=True))
        # Outside any repository an unmodeled form could still enter one, and
        # a PowerShell location cmdlet in a pipeline takes a piped path.
        home = os.path.dirname(main)
        with mock.patch.dict(os.environ, {"HOME": home, "USERPROFILE": home,
                                          "MAINREPO": main}):
            for command, kw in (
                    (f"Push-Location {main}; Push-Location; git reset --hard",
                     {"ps": True}),
                    (f"command pushd {main} && git reset --hard", {}),
                    (f"builtin cd {main} && git reset --hard", {}),
                    (f"pushd ~/{os.path.basename(main)} && git reset --hard",
                     {}),
                    ("pushd $MAINREPO && git reset --hard", {}),
                    ("cd && cd " + os.path.basename(main)
                     + " && git reset --hard", {}),
                    ("Set-Location; Set-Location " + os.path.basename(main)
                     + "; git reset --hard", {"ps": True})):
                with self.subTest(command=command):
                    self.assertIsNotNone(coordination.deny_reason_for_shell(
                        command, safe, None, **kw))
        for command in (
                f"Push-Location {main} -StackName A; git reset --hard",
                f"'{main}' | Set-Location; git reset --hard"):
            with self.subTest(command=command):
                self.assertIsNotNone(coordination.deny_reason_for_shell(
                    command, safe, None, ps=True))
        # A wrapper that starts a process cannot move this shell.
        self.assertIsNotNone(coordination.deny_reason_for_shell(
            f"env pushd {safe}; git reset --hard", main, None))
        for command, kw in (
                (f"pushd {main} && popd && git reset --hard", {}),
                (f"Push-Location {main}; Pop-Location; git reset --hard",
                 {"ps": True})):
            with self.subTest(command=command):
                self.assertIsNone(coordination.deny_reason_for_shell(
                    command, safe, None, **kw))

    def test_powershell_location_parameters_require_complete_binding(self):
        safe = os.path.join(self.base, "safe directory").replace(os.sep, "/")
        os.makedirs(safe)
        with mock.patch.dict(os.environ, {"HOME": safe, "USERPROFILE": safe}):
            for location in (
                    "Set-Location -Encoding utf8",
                    "Set-Location -Path",
                    "Set-Location -Path:",
                    "Set-Location -Path -PassThru",
                    f"Set-Location '{safe}' -Path '{safe}'",
                    f"Set-Location -Path '{safe}' -LiteralPath '{safe}'",
                    f"Set-Location '{safe}' '{safe}'",
                    f"Set-Location '{safe}' -PassThru -PassThru",
                    f"Set-Location '{safe}' -PassThru:$true",
                    f"Pop-Location -Path:'{safe}'",
                    f"Push-Location '{safe}' -StackName other",
                    "Set-Location @paths"):
                command = location + "; git reset --hard"
                with self.subTest(command=command):
                    self.assertIsNotNone(coordination.deny_reason_for_shell(
                        command, self.repo_path, self.repo, ps=True))
                    self.assertIn(
                        ("opaque", self.repo_path,
                         coordination._UNMODELED_LOCATION),
                        list(coordination._shell_events(
                            command, self.repo_path, ps=True)))
            for location in (
                    f"Set-Location '{safe}'",
                    f"Set-Location -Path:'{safe}' -PassThru",
                    f"Set-Location -PassThru -LiteralPath '{safe}'",
                    f"Push-Location -LiteralPath:'{safe}' -PassThru",
                    "Set-Location -PassThru",
                    "cd",
                    f"Set-Location '{safe}'; Push-Location -PassThru; "
                    "Pop-Location -PassThru"):
                command = location + "; git reset --hard"
                with self.subTest(command=command):
                    self.assertIsNone(coordination.deny_reason_for_shell(
                        command, self.repo_path, self.repo, ps=True))

    def test_location_pipeline_stage_invocations_are_denied(self):
        main = self.repo_path.replace(os.sep, "/")
        safe = os.path.join(self.base, "safe").replace(os.sep, "/")
        os.makedirs(safe)
        for name in ("Microsoft.PowerShell.Management\\Set-Location",
                     "Microsoft.PowerShell.Management\\Push-Location",
                     "Microsoft.PowerShell.Management\\POP-LOCATION",
                     "Set-Location", "cd"):
            for invocation in (name, "& " + name, ". " + name,
                               "& '" + name + "'", '. "' + name + '"',
                               "$label = & '" + name + "'",
                               "&'" + name + "'", "$label=&'" + name + "'",
                               ".'" + name + "'", "$label=.'" + name + "'"):
                for pipeline in (
                        f"{invocation} | Write-Output",
                        f"'{main}' | {invocation}",
                        f"'{main}' | {invocation} | Write-Output"):
                    command = pipeline + "; git reset --hard"
                    with self.subTest(command=command):
                        # No repo hint: an opaque location can enter any repo.
                        self.assertIsNotNone(coordination.deny_reason_for_shell(
                            command, safe, None, ps=True))
                        self.assertIn(
                            ("opaque", safe, coordination._UNMODELED_LOCATION),
                            list(coordination._shell_events(
                                command, safe, ps=True)))
        self.assertIsNone(coordination.deny_reason_for_shell(
            "Write-Output 'Microsoft.PowerShell.Management\\Set-Location' "
            "| Write-Output; git reset --hard", safe, None, ps=True))

    def test_quoted_location_names_are_data(self):
        safe = os.path.join(self.base, "safe").replace(os.sep, "/")
        os.makedirs(safe)
        for name in ("cd", "Set-Location",
                     "Microsoft.PowerShell.Management\\Set-Location"):
            for quote in ("'", '"'):
                for template in ("{}", "$label = {}", "$label={}",
                                 "{} | Write-Output", "$label = {} | Write-Output",
                                 "$label={} | Write-Output", "& {{ {} }}"):
                    command = template.format(f"{quote}{name}{quote}")
                    with self.subTest(command=command), mock.patch.dict(
                            os.environ, {"HOME": safe, "USERPROFILE": safe}):
                        self.assertIsNone(coordination.deny_reason_for_shell(
                            command, self.repo_path, self.repo, ps=True))
                        self.assertIsNone(coordination.deny_reason_for_shell(
                            command + "; git reset --hard", safe, None,
                            ps=True))
                        self.assertIsNotNone(coordination.deny_reason_for_shell(
                            command + "; git reset --hard", self.repo_path,
                            self.repo, ps=True))

    def test_assigned_literal_location_calls_move_the_session(self):
        main = self.repo_path.replace(os.sep, "/")
        safe = os.path.join(self.base, "safe").replace(os.sep, "/")
        os.makedirs(safe)
        for operator in ("& ", "&", ". ", "."):
            for prefix in ("", "$label = ", "$label="):
                call = f"{prefix}{operator}'Set-Location'"
                with self.subTest(call=call):
                    self.assertIsNotNone(coordination.deny_reason_for_shell(
                        f"{call} '{main}'; git reset --hard", safe, None,
                        ps=True))
                    self.assertIsNone(coordination.deny_reason_for_shell(
                        f"{call} '{safe}'; git reset --hard", self.repo_path,
                        self.repo, ps=True))
        self.assertIsNotNone(coordination.deny_reason_for_shell(
            f".Set-Location '{safe}'; git reset --hard", self.repo_path,
            self.repo, ps=True))

    def test_cd_inside_pipeline_does_not_move_later_segments(self):
        safe = os.path.join(self.base, "safe").replace(os.sep, "/")
        os.makedirs(safe, exist_ok=True)
        reason = coordination.deny_reason_for_shell(
            f"true | cd {safe} && git reset --hard", self.repo_path, self.repo)
        self.assertIsNotNone(reason)

    def test_single_ampersand_separates_statements_on_main(self):
        # #190: `a & b` backgrounds a and then runs b (Bash, PowerShell 7).
        for cmd, ps in (("echo x & git reset --hard", False),
                        ("echo x&git reset --hard", False),
                        ("Write-Output x & git reset --hard", True),
                        ("Write-Output x & Remove-Item seed.txt", True)):
            self.assertIsNotNone(coordination.deny_reason_for_shell(
                cmd, self.repo_path, self.repo, ps=ps), cmd)

    def test_powershell_script_block_bodies_are_checked(self):
        # #210: commands inside `{ ... }` reach the main rule.
        for cmd in ("if ($true) { git reset --hard }",
                    "if($true){git reset --hard}",
                    "try { git status } catch { git reset --hard }",
                    "foreach ($f in 1) { Remove-Item seed.txt }",
                    "if ($true) { Set-Content seed.txt x }",
                    "if ($true) {\n  git status\n  git reset --hard\n}",
                    "if ($a) { if ($b) { git reset --hard } }",
                    "Get-ChildItem | ForEach-Object { git reset --hard }",
                    # A block bound to a parameter is coerced to its text.
                    "Remove-Item -Path {seed.txt}",
                    # A quote inside a comment opens no string (review).
                    '<# " #>; Remove-Item seed.txt; Write-Output "x"',
                    "# '\ngit reset --hard; Write-Output 'x'",
                    # `<#` inside a token opens no comment (review).
                    "Write-Output Foo<#x; Remove-Item seed.txt; #>",
                    "Write-Output foo`;#bar; Remove-Item seed.txt",
                    'Write-Output "x"# "\nRemove-Item seed.txt; Write-Output "y"',
                    "Write-Output 'x'<# ' #>; Remove-Item seed.txt; 'y'",
                    "Write-Output foo` <#x; Remove-Item seed.txt #>",
                    # A comment is blanked only where it is certain, so a
                    # `#` PowerShell may read as text never hides code.
                    'Write-Output foo `\n# "\nRemove-Item seed.txt; '
                    'Write-Output "y"',
                    "Write-Output foo `\n# x; Remove-Item seed.txt",
                    "Write-Output foo` #x; Remove-Item seed.txt",
                    "Write-Output ${a #}; Remove-Item seed.txt",
                    "Write-Output ${a\n#}; Remove-Item seed.txt",
                    "cmd.exe --% /c echo #x | Remove-Item seed.txt",
                    # `--%` ends at a newline or pipe.
                    "findstr.exe --% x\nRemove-Item seed.txt",
                    "cmd.exe foo`;--% bar; Remove-Item seed.txt",
                    "findstr.exe --% x | ForEach-Object { git reset --hard }"):
            self.assertIsNotNone(coordination.deny_reason_for_shell(
                cmd, self.repo_path, self.repo, ps=True), cmd)
        for cmd in ("if ($true) { git status }",
                    "Write-Output '{ git reset --hard }'",
                    "Get-ChildItem | ForEach-Object { $_.Name }",
                    "Write-Output ${env:TEMP}",
                    # Comments are data, even when they hold a block.
                    "# if ($true) { git reset --hard }",
                    "<# { git reset --hard } #>",
                    "Write-Output x # { git reset --hard }",
                    "Write-Output x # git reset --hard",
                    "Write-Output 'x'; # Remove-Item seed.txt",
                    "<# Remove-Item seed.txt\ngit reset --hard #>\ngit status",
                    "\t# Remove-Item seed.txt\ngit status",
                    # A `#` inside a string opens no comment.
                    "Write-Output '#'; git status",
                    # After `--%` the line is literal native-command text.
                    "findstr.exe --% { Remove-Item seed.txt }",
                    "findstr.exe --% x; git reset --hard"):
            self.assertIsNone(coordination.deny_reason_for_shell(
                cmd, self.repo_path, self.repo, ps=True), cmd)

    def test_powershell_block_cd_stays_inside_the_block(self):
        # A block may not run: its `cd` must not move later statements.
        safe = os.path.join(self.base, "safe").replace(os.sep, "/")
        os.makedirs(safe, exist_ok=True)
        main = self.repo_path.replace(os.sep, "/")
        self.assertIsNotNone(coordination.deny_reason_for_shell(
            f"if ($false) {{ cd {safe} }}; git reset --hard",
            self.repo_path, self.repo, ps=True))
        self.assertIsNotNone(coordination.deny_reason_for_shell(
            f"cd {safe}; if ($true) {{ cd {main}; git reset --hard }}",
            self.repo_path, self.repo, ps=True))

    def test_powershell_block_cd_may_move_later_statements(self):
        # Review of #210: a block that runs moves the location for later
        # statements, so they are checked in both possible locations.
        safe = os.path.join(self.base, "safe").replace(os.sep, "/")
        os.makedirs(safe, exist_ok=True)
        main = self.repo_path.replace(os.sep, "/")
        for cmd in (f"if ($true) {{ cd {main} }}; git reset --hard",
                    f"try {{ cd {main} }} finally {{ git reset --hard }}",
                    f"if ($a) {{ cd {main} }} else {{ }}; git reset --hard",
                    f"if ($a) {{ if ($b) {{ cd {main} }} }}; git reset --hard",
                    f"if ($true) {{ cd {self.base} }}; cd repo; "
                    "git reset --hard"):
            self.assertIsNotNone(coordination.deny_reason_for_shell(
                cmd, safe, self.repo, ps=True), cmd)
        for cmd in (f"if ($true) {{ cd {main} }}; cd {safe}; git reset --hard",
                    # Only one branch of an if/else chain runs.
                    f"if ($true) {{ cd {main} }} else {{ git reset --hard }}",
                    f"if ($a) {{ cd {main} }} elseif ($b) {{ git reset --hard }}",
                    # A background job's location never reaches the parent.
                    f"& {{ cd {main} }} & git reset --hard"):
            self.assertIsNone(coordination.deny_reason_for_shell(
                cmd, safe, self.repo, ps=True), cmd)

    def test_powershell_repeated_block_is_checked_from_where_it_moved(self):
        # Review of #210: a loop body runs again from where it moved to.
        main = self.repo_path.replace(os.sep, "/")
        safe = os.path.join(self.base, "safe").replace(os.sep, "/")
        self.assertIsNotNone(coordination.deny_reason_for_shell(
            "foreach ($x in 1,2) { cd sub; git reset --hard }", safe,
            self.repo, ps=True))
        events = list(coordination._shell_events(
            f"foreach ($x in 1,2) {{ cd {main}; git status }}", safe, ps=True))
        self.assertNotIn("opaque", [e[0] for e in events])
        self.assertIn(main, [e[2] for e in events if e[0] == "git"])

    def test_powershell_backtick_escaped_brace_is_not_a_block(self):
        self.assertIsNone(coordination.deny_reason_for_shell(
            "Write-Output `{ Remove-Item seed.txt", self.repo_path, self.repo,
            ps=True))
        self.assertIsNotNone(coordination.deny_reason_for_shell(
            "Write-Output ``{ Remove-Item seed.txt }", self.repo_path,
            self.repo, ps=True))
    def test_bash_pipe_ampersand_stage_is_checked(self):
        # #208: the stage after `|&` must not start with `&`.
        for cmd in ("git status |& git reset --hard",
                    "git status|&git reset --hard"):
            self.assertIsNotNone(coordination.deny_reason_for_shell(
                cmd, self.repo_path, self.repo), cmd)
        self.assertIsNone(coordination.deny_reason_for_shell(
            "git status |& cat", self.repo_path, self.repo))

    def test_bash_grouping_and_negation_are_checked(self):
        # #209: `(`, `{` and `!` are not the command name.
        for cmd in ("( git reset --hard )", "(git reset --hard)",
                    "{ git reset --hard; }", "! git reset --hard",
                    "! ( git reset --hard )", "( echo x > seed.txt )"):
            self.assertIsNotNone(coordination.deny_reason_for_shell(
                cmd, self.repo_path, self.repo), cmd)
        self.assertIsNone(coordination.deny_reason_for_shell(
            "(git status)", self.repo_path, self.repo))

    def test_grouped_cd_does_not_move_later_statements(self):
        # Peeling `(`/`{` must not let a grouped `cd` move later statements:
        # subshells, piped or backgrounded groups, and closers followed by
        # redirects or pipes all run the reset in this checkout.
        safe = os.path.join(self.base, "safe").replace(os.sep, "/")
        os.makedirs(safe, exist_ok=True)
        for cmd in (f"(cd {safe}); git reset --hard",
                    f"( cd {safe} && true ); git reset --hard",
                    f"( (cd {safe}) ); git reset --hard",
                    f"(cd {safe} && true) >/dev/null; git reset --hard",
                    f"(cd {safe} && true) | cat; git reset --hard",
                    f"{{ cd {safe}; true; }} | cat; git reset --hard",
                    f"{{ cd {safe}; true; }} & git reset --hard",
                    "(git reset --hard) >/dev/null",
                    "cat | { git reset --hard; }"):
            self.assertIsNotNone(coordination.deny_reason_for_shell(
                cmd, self.repo_path, self.repo), cmd)
        # Inside the subshell the cd does apply (#224 review).
        main = self.repo_path.replace(os.sep, "/")
        self.assertIsNotNone(coordination.deny_reason_for_shell(
            f"(cd {main} && git reset --hard); true", safe, None))
        self.assertIsNone(coordination.deny_reason_for_shell(
            f"(cd {safe} && git reset --hard)", self.repo_path, self.repo))

    def test_backgrounded_cd_does_not_move_later_segments(self):
        # `cd x &` runs in a background subshell/job: the parent stays put.
        safe = os.path.join(self.base, "safe").replace(os.sep, "/")
        os.makedirs(safe, exist_ok=True)
        # `&` binds looser than `&&`/`||`, so the whole list is the job.
        for cmd in (f"cd {safe} & git reset --hard",
                    f"cd {safe} && true & git reset --hard",
                    f"cd {safe} & git reset --hard &"):
            for ps in (False, True):
                self.assertIsNotNone(coordination.deny_reason_for_shell(
                    cmd, self.repo_path, self.repo, ps=ps), (cmd, ps))

    def test_backgrounded_cd_moves_the_rest_of_its_own_list(self):
        # The job runs its later segments after its own `cd`.
        safe = os.path.join(self.base, "safe").replace(os.sep, "/")
        os.makedirs(safe, exist_ok=True)
        main = self.repo_path.replace(os.sep, "/")
        for ps in (False, True):
            self.assertIsNotNone(coordination.deny_reason_for_shell(
                f"cd {main} && git reset --hard &", safe, None, ps=ps), ps)
            self.assertIsNone(coordination.deny_reason_for_shell(
                f"cd {main} && true & git reset --hard", safe, None,
                ps=ps), ps)

    def test_ampersand_lookalikes_are_not_separators(self):
        # Redirect forms, a trailing &, quoted text and the PowerShell call
        # operator must neither over-deny nor lose the command they carry.
        allowed = [("git status 2>&1", False), ("git log >&2", False),
                   ("git status |& cat", False), ("git status &", False),
                   ("git status && git log", False),
                   ('echo "a & git reset --hard"', False),
                   ("Write-Output 'a & git reset --hard'", True),
                   ("& git status", True),
                   ("Get-Content seed.txt | & { process { $_ } }", True)]
        for cmd, ps in allowed:
            self.assertIsNone(coordination.deny_reason_for_shell(
                cmd, self.repo_path, self.repo, ps=ps), cmd)
        out = os.path.join(self.repo_path, "out.log")
        for cmd in ("ls &> out.log", "ls &>> out.log", "ls > out.log 2>&1"):
            self.assertEqual(coordination.shell_write_targets(
                cmd, self.repo_path), [out], cmd)
        self.assertIsNotNone(coordination.deny_reason_for_shell(
            "& git reset --hard", self.repo_path, self.repo, ps=True))

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

    def test_excessively_nested_interpreter_is_denied_on_main(self):
        command = "git reset --hard"
        for _ in range(coordination._MAX_UNWRAP_DEPTH + 1):
            command = f"bash -c {shlex.quote(command)}"
        self.assertIsNotNone(coordination.deny_reason_for_shell(
            command, self.repo_path, self.repo))

    def test_excessively_nested_prefix_wrappers_are_denied_on_main(self):
        command = "git reset --hard"
        for _ in range(coordination._MAX_UNWRAP_DEPTH + 1):
            command = f"sudo {command}"
        self.assertIsNotNone(coordination.deny_reason_for_shell(
            command, self.repo_path, self.repo))

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

    def test_coreutils_trailing_dot_space_targets(self):
        cases = (
            ("tee", ("seed.txt",), ["seed.txt"]),
            ("sed", ("-i", "seed.txt"), ["seed.txt"]),
            ("dd", ("of=seed.txt",), ["seed.txt"]),
            ("truncate", ("seed.txt",), ["seed.txt"]),
            ("rm", ("seed.txt",), ["seed.txt"]),
            ("unlink", ("seed.txt",), ["seed.txt"]),
            ("shred", ("seed.txt",), ["seed.txt"]),
            ("cp", ("other.txt", "seed.txt"), ["seed.txt"]),
            ("mv", ("other.txt", "seed.txt"),
             ["seed.txt", "other.txt"]),
            ("install", ("other.txt", "seed.txt"), ["seed.txt"]),
        )
        for command, operands, expected in cases:
            for suffix in (".", " "):
                with self.subTest(command=command, suffix=suffix):
                    self.assertEqual(
                        coordination._argv_write_targets(
                            [command + suffix, *operands], ps=False),
                        expected)

    def test_coreutils_suffix_normalization_preserves_boundaries(self):
        for command in ("RM.", "RM ", "/usr/bin/rm.",
                        r"C:\Program Files\Git\usr\bin\rm."):
            with self.subTest(command=command):
                self.assertEqual(
                    coordination._argv_write_targets(
                        [command, "seed.txt"], ps=False),
                    [])

        powershell_cases = (
            (["DEL", "seed.txt"], ["seed.txt"]),
            (["COPY", "other.txt", "seed.txt"], ["seed.txt"]),
            (["del.", "seed.txt"], []),
            (["del ", "seed.txt"], []),
            (["copy.", "other.txt", "seed.txt"], []),
            (["copy ", "other.txt", "seed.txt"], []),
        )
        for argv, expected in powershell_cases:
            with self.subTest(argv=argv):
                self.assertEqual(
                    coordination._argv_write_targets(argv, ps=True),
                    expected)

    def test_coreutils_suffix_forms_denied_on_main(self):
        for command in ("tee. seed.txt", "rm. seed.txt",
                        "cp. other.txt seed.txt"):
            with self.subTest(command=command):
                self.assertIsNotNone(coordination.deny_reason_for_shell(
                    command, self.repo_path, self.repo))

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

    def test_second_supervisor_rejected_whatever_its_value(self):
        first = coordination.claim(self.repo, "s1", "supervisor", "claude")
        self.assertIn("claimed", first)
        second = coordination.claim(self.repo, "s2", "supervisor", "codex")
        self.assertIn("rejected", second)
        self.assertEqual(second["rejected"]["session"], "s1")

    def test_supervisor_reclaim_by_same_session_allowed(self):
        coordination.claim(self.repo, "s1", "supervisor", "claude")
        again = coordination.claim(self.repo, "s1", "supervisor", "claude")
        self.assertIn("claimed", again)

    def test_supervisor_claim_leaves_other_kinds_alone(self):
        coordination.claim(self.repo, "s1", "supervisor", "claude")
        other = coordination.claim(self.repo, "s2", "branch", "feature")
        self.assertIn("claimed", other)

    def test_supervisor_claimable_after_release(self):
        held = coordination.claim(self.repo, "s1", "supervisor", "claude")
        coordination.release(self.repo, held["claimed"]["id"], session="s1")
        taken = coordination.claim(self.repo, "s2", "supervisor", "codex")
        self.assertIn("claimed", taken)

    def test_claim_cli_second_supervisor_denied_naming_holder(self):
        coordination.claim(self.repo, "s1", "supervisor", "claude")
        buffer = io.StringIO()
        with mock.patch.object(coordination, "discover", return_value=self.repo):
            with redirect_stderr(buffer):
                rc = coordination.main(
                    ["claim", "--session", "s2",
                     "--kind", "supervisor", "--value", "codex"])
        self.assertEqual(rc, coordination.DENY)
        self.assertIn("claimed by session s1", buffer.getvalue())

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
        with open(self.repo.registry_path, encoding="utf-8") as fh:
            data = json.load(fh)
        self.assertEqual(len(data["audit"]), 1)

    def test_no_partial_registry_after_saves(self):
        for i in range(20):
            coordination.claim(self.repo, "s1", "file_glob", f"src/{i}/*")
        with open(self.repo.registry_path, encoding="utf-8") as fh:
            data = json.load(fh)
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

    def test_lock_deadline_trusts_exception_over_racy_probe(self):
        # #57: a final contention failure (FileExistsError) must be reported as
        # contention even when the lock file no longer exists at the
        # post-deadline probe — the holder removed it in the race window.
        def contended_open(path, flags, *args):
            raise FileExistsError(17, "File exists")

        # Force the racy probe to disagree with the exception: exists()==False
        # would have said "not creatable" under the old probe-based logic.
        with mock.patch.object(os, "open", contended_open), \
                mock.patch.object(os.path, "exists", return_value=False):
            with self.assertRaises(coordination.RegistryError) as caught:
                with coordination._Lock(self.repo.registry_path, timeout=0.0):
                    pass
        self.assertIn("lock held", str(caught.exception))

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

    def write_index(self, rows):
        with open(os.path.join(self.repo_path, "docs", "adr", "README.md"),
                  "w", encoding="utf-8") as fh:
            fh.write("# ADRs\n\n| ADR | Title | State |\n|---|---|---|\n"
                     + rows + "\nAllocation policy updated in 2026.\n")

    def test_fresh_registry_skips_permanently_consumed_index_number(self):
        self.write_index("| 0005 | Consumed by allocation probe | — |\n")
        result = coordination.claim(self.repo, "s1", "adr", "")
        self.assertEqual(result["claimed"]["value"], "0006")

    def test_index_links_also_raise_the_allocation_floor(self):
        self.write_index("| [0008](0008-example.md) | Example | Accepted |\n")
        result = coordination.claim(self.repo, "s1", "adr", "")
        self.assertEqual(result["claimed"]["value"], "0009")

    def test_optional_markdown_outer_pipes_preserve_consumed_numbers(self):
        for row in ("0007 | Consumed | — |\n",
                    "0007 | Consumed | —\n",
                    "| 0007 | Consumed | —\n"):
            with self.subTest(row=row):
                self.write_index(row)
                self.assertEqual(coordination._adr_index_floor(self.repo), 7)
        result = coordination.claim(self.repo, "s1", "adr", "")
        self.assertEqual(result["claimed"]["value"], "0008")

    def test_table_header_without_outer_pipes_is_supported(self):
        path = os.path.join(self.repo_path, "docs", "adr", "README.md")
        with open(path, "w", encoding="utf-8") as fh:
            fh.write("ADR | Title | State\n---|---|---\n"
                     "0007 | Consumed | —\n")
        self.assertIn("ADR registry/index mismatch", self.doctor_output())
        result = coordination.claim(self.repo, "s1", "adr", "")
        self.assertEqual(result["claimed"]["value"], "0008")

    def test_registry_above_index_is_preserved(self):
        for _ in range(3):
            coordination.claim(self.repo, "s1", "adr", "")
        self.write_index("| 0005 | Consumed by allocation probe | — |\n")
        result = coordination.claim(self.repo, "s1", "adr", "")
        self.assertEqual(result["claimed"]["value"], "0008")

    def test_malformed_index_fails_closed_without_allocating(self):
        for rows in ("| 000X | Consumed | — |\n",
                     "| [0005 | Broken link | Accepted |\n"):
            with self.subTest(rows=rows):
                self.write_index(rows)
                with self.assertRaises(coordination.RegistryError):
                    coordination.claim(self.repo, "s1", "adr", "")
                self.assertEqual(coordination.list_claims(self.repo), [])

    def test_doctor_reports_index_ahead_of_registry(self):
        self.write_index("| 0005 | Consumed by allocation probe | — |\n")
        self.assertIn("ADR registry/index mismatch", self.doctor_output())
        coordination.claim(self.repo, "s1", "adr", "")
        self.assertNotIn("ADR registry/index mismatch", self.doctor_output())

    def test_doctor_reports_malformed_index_without_crashing(self):
        self.write_index("| unknown | Consumed | — |\n")
        self.assertIn("invalid ADR index", self.doctor_output())

    def test_invalid_utf8_index_fails_closed(self):
        path = os.path.join(self.repo_path, "docs", "adr", "README.md")
        with open(path, "wb") as fh:
            fh.write(b"\xff")
        with self.assertRaises(coordination.RegistryError):
            coordination.claim(self.repo, "s1", "adr", "")
        self.assertIn("invalid ADR index", self.doctor_output())

    def test_unreadable_index_fails_closed(self):
        os.mkdir(os.path.join(self.repo_path, "docs", "adr", "README.md"))
        with self.assertRaises(coordination.RegistryError):
            coordination.claim(self.repo, "s1", "adr", "")
        self.assertIn("invalid ADR index", self.doctor_output())

    def test_index_without_table_fails_closed(self):
        path = os.path.join(self.repo_path, "docs", "adr", "README.md")
        with open(path, "w", encoding="utf-8") as fh:
            fh.write("# ADRs\n")
        with self.assertRaises(coordination.RegistryError):
            coordination.claim(self.repo, "s1", "adr", "")

    def test_exhausted_number_space_fails_closed(self):
        self.write_index("| 9999 | Consumed | — |\n")
        with self.assertRaisesRegex(coordination.RegistryError, "exhausted"):
            coordination.claim(self.repo, "s1", "adr", "")

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

    def test_ambiguous_codex_commands_check_both_shells_on_main(self):
        for command in (
                r'echo "a\"b"; git reset --hard; echo "c"',
                r'Write-Output "x\"; Set-Content seed.txt boom; Write-Output "y"'):
            with self.subTest(command=command), mock.patch.object(
                    sys, "platform", "win32"), mock.patch.dict(os.environ, {
                        "CODEX_THREAD_ID": "codex-thread", "CLAUDECODE": "",
                    }):
                rc, out = self.decide({
                    "tool_name": "Bash", "tool_input": {"command": command},
                    "cwd": self.repo_path,
                })
                self.assertIn("main is read-only", out)

    def test_ambiguous_codex_commands_check_both_shells_for_claims(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        coordination.claim(self.repo, "s1", "branch", "feature")
        coordination.claim(self.repo, "s2", "file_glob", "src/*")
        for command in (
                r'echo "a\"b"; rm src/code.cs; echo "c"',
                r'Write-Output "x\"; Remove-Item src/code.cs; Write-Output "y"'):
            with self.subTest(command=command), mock.patch.object(
                    sys, "platform", "win32"), mock.patch.dict(os.environ, {
                        "CODEX_THREAD_ID": "codex-thread", "CLAUDECODE": "",
                        "AGENT_SESSION_ID": "different-environment-session",
                    }):
                rc, out = self.decide({
                    "tool_name": "Bash", "tool_input": {"command": command},
                    "cwd": self.repo_path, "session_id": "s1",
                })
                self.assertIn("claimed by session s2", out)

    def test_single_ampersand_keeps_claim_writes_visible(self):
        # #190: the statement after a single & must reach the claim layer.
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        coordination.claim(self.repo, "s1", "branch", "feature")
        coordination.claim(self.repo, "s2", "file_glob", "src/*")
        for tool, command in (
                ("Bash", "echo x & rm src/code.cs"),
                ("PowerShell", "Write-Output x & Remove-Item src/code.cs")):
            with self.subTest(command=command):
                rc, out = self.decide({
                    "tool_name": tool, "tool_input": {"command": command},
                    "cwd": self.repo_path, "session_id": "s1",
                })
                self.assertIn("claimed by session s2", out)

    def test_powershell_script_block_writes_reach_claim_layer(self):
        # #210: the claim layer shares the same parser.
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        coordination.claim(self.repo, "s1", "branch", "feature")
        coordination.claim(self.repo, "s2", "file_glob", "src/*")
        rc, out = self.decide({
            "tool_name": "PowerShell",
            "tool_input": {"command": "if ($true) { Remove-Item src/code.cs }"},
            "cwd": self.repo_path, "session_id": "s1",
        })
        self.assertIn("claimed by session s2", out)
    def test_bash_grouped_and_pipe_ampersand_writes_reach_claim_layer(self):
        # #208, #209: the claim layer shares the same parser.
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        coordination.claim(self.repo, "s1", "branch", "feature")
        coordination.claim(self.repo, "s2", "file_glob", "src/*")
        for command in ("echo x |& rm src/code.cs", "( rm src/code.cs )",
                        "{ rm src/code.cs; }", "! rm src/code.cs"):
            with self.subTest(command=command):
                rc, out = self.decide({
                    "tool_name": "Bash", "tool_input": {"command": command},
                    "cwd": self.repo_path, "session_id": "s1",
                })
                self.assertIn("claimed by session s2", out)

    def test_directory_delete_or_move_reaches_descendant_claims(self):
        # #184: removing `src` removes everything claimed under `src/*`.
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        coordination.claim(self.repo, "s1", "branch", "feature")
        coordination.claim(self.repo, "s2", "file_glob", "src/lib/*")

        def decide(tool, command, shell=""):
            return self.decide({
                "tool_name": tool,
                "tool_input": {"command": command, "shell": shell},
                "cwd": self.repo_path, "session_id": "s1",
            })[1]

        for tool, shell, command in (
                ("Bash", "", "rm -rf src"),
                ("Bash", "", "rm -r ."),
                ("Bash", "", "rmdir src/lib"),
                ("Bash", "", "/bin/rm -rf src"),
                ("Bash", "", "/usr/bin/mv src elsewhere"),
                ("Bash", "", "mv src elsewhere"),
                ("Bash", "", 'cmd /c "rd /s /q src"'),
                ("Bash", "cmd.exe", "rmdir /s /q src"),
                ("Bash", "cmd.exe", r"del /s /q src"),
                ("Bash", "cmd.exe", "move src elsewhere"),
                ("PowerShell", "", "Remove-Item -Recurse src"),
                ("PowerShell", "", "rd src -Recurse"),
                ("PowerShell", "", "Move-Item src elsewhere"),
                ("Bash", "", "rm -rf -- src"),
                ("Bash", "", "mv -- src elsewhere"),
                ("Bash", "", "rm.exe -rf src"),
                ("Bash", "", "/usr/bin/mv.exe src elsewhere"),
                ("PowerShell", "", "Rename-Item -Path lib -NewName src"),
                ("PowerShell", "", "Rename-Item lib -NewName:src"),
                ("Bash", "", "RM.EXE -rf src"),
                ("Bash", "", "/usr/bin/MV.EXE src elsewhere")):
            with self.subTest(shell=shell or tool, command=command):
                self.assertIn("contains file_glob 'src/lib/*' claimed by "
                              "session s2", decide(tool, command, shell))
        for command in ("rm -rf docs", "rm -rf src/other", "rm -rf srclib",
                        "cp notes.txt ."):
            with self.subTest(command=command):
                self.assertEqual(decide("Bash", command), "")
        # rmdir is an ordinary write too, so the caller's own scope applies.
        coordination.claim(self.repo, "s1", "file_glob", "docs/*")
        self.assertIn("outside your claimed file scope",
                      decide("Bash", "rmdir srclib"))

    def test_subtree_and_overlap_checks_fold_case_on_windows(self):
        # #233: on a case-insensitive checkout a claim keeps its spelling
        # (`Src/Lib/*`) while the normalized target is `src`.
        coordination.claim(self.repo, "s2", "file_glob", "Src/Lib/*")
        claims = coordination.list_claims(self.repo)
        target = os.path.join(self.repo_path, "src")
        self.assertIsNone(coordination.glob_conflict(
            claims, "s1", [], subtrees=[target]))  # POSIX: distinct paths
        self.assertFalse(coordination._globs_overlap("Src/*", "src/a.cs"))
        with mock.patch("os.path.normcase", lambda p: p.lower()):
            self.assertIn("contains file_glob 'Src/Lib/*'",
                          coordination.glob_conflict(
                              claims, "s1", [], subtrees=[target]))
            self.assertTrue(coordination._globs_overlap("Src/*", "src/a.cs"))
            self.assertIsNone(coordination.glob_conflict(
                claims, "s1", [], subtrees=[target + "lib"]))

    def test_move_into_existing_directory_checks_the_moved_path(self):
        # Review of #184: `mv src docs` writes `docs/src` when docs exists.
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        coordination.claim(self.repo, "s1", "branch", "feature")
        coordination.claim(self.repo, "s2", "file_glob", "docs/lib/*")
        os.makedirs(os.path.join(self.repo_path, "docs", "lib"))

        def decide(command, shell=""):
            return self.decide({
                "tool_name": "Bash",
                "tool_input": {"command": command, "shell": shell},
                "cwd": self.repo_path, "session_id": "s1",
            })[1]

        for command, shell in (("mv src docs", ""), ("mv src docs/", ""),
                               ("mv a b docs", ""), ("move src docs", "cmd.exe")):
            with self.subTest(command=command):
                self.assertEqual(decide(command, shell), "")
        for command, shell in (("mv lib docs", ""), ("mv src docs/lib", ""),
                               ("move src docs\\lib", "cmd.exe")):
            with self.subTest(command=command):
                self.assertIn("claimed by session s2", decide(command, shell))
        # -T replaces an existing directory instead of moving into it.
        for command in ("mv -T src docs", "mv -fT src docs",
                        "mv --no-target-directory src docs"):
            with self.subTest(command=command):
                self.assertIn("contains file_glob 'docs/lib/*'", decide(command))
        # A wildcard operand is checked as what it expands to.
        for command in ("rm -rf docs/*", "rm -rf d*/l?b", "mv docs/* /tmp",
                        "rm -rf {docs,dummy}", "rm -rf docs/{x,l{a,i}b}",
                        "rm -rf {d..e}ocs", "rm -rf x{y/../{foo,docs}"):
            with self.subTest(command=command):
                self.assertIn("docs/lib", decide(command))
        self.assertEqual(decide("rm -rf docs/*.md"), "")
        # rmdir -p also removes the ancestors; cmd ren names a sibling.
        self.assertEqual(decide("rmdir docs/empty"), "")
        for command, shell in (("rmdir -p docs/empty", ""),
                               ("rmdir -vp docs/empty", ""),
                               ("rmdir --parents docs/empty", ""),
                               ("ren docs\\old lib", "cmd.exe"),
                               ("rename docs\\old lib", "cmd.exe")):
            with self.subTest(command=command):
                self.assertIn("docs/lib", decide(command, shell))
        # A multi-match destination glob moves all but the last match.
        os.makedirs(os.path.join(self.repo_path, "dummy"))
        self.assertIn("docs/lib", decide("mv src d*"))
        shutil.rmtree(os.path.join(self.repo_path, "dummy"))
        with mock.patch.dict(os.environ, {"HOME": self.repo_path,
                                         "USERPROFILE": self.repo_path}):
            self.assertIn("docs/lib", decide("rm -rf ~/docs/*"))
        # A wildcard source moved into a directory lands under its basename.
        os.makedirs(os.path.join(self.repo_path, "stuff", "lib"))
        os.makedirs(os.path.join(self.repo_path, "target"))
        coordination.claim(self.repo, "s2", "file_glob", "target/lib/*")
        self.assertIn("target/lib", decide("mv stuff/* target"))
        self.assertIn("target/lib", decide("mv stuff/lib t*"))
        self.assertEqual(decide("mv stuff target"), "")
        # A positional Rename-Item name lands beside the source.
        self.assertIn("docs/lib", self.decide({
            "tool_name": "PowerShell",
            "tool_input": {"command": "Rename-Item docs/x lib"},
            "cwd": self.repo_path, "session_id": "s1"})[1])
        shutil.rmtree(os.path.join(self.repo_path, "stuff"))
        shutil.rmtree(os.path.join(self.repo_path, "target"))
        # Unlinking a symlink leaves the directory it points to alone.
        if hasattr(os, "symlink"):
            try:
                os.symlink("docs", os.path.join(self.repo_path, "link"))
            except OSError:
                pass
            else:
                self.assertEqual(decide("rm link"), "")
                self.assertEqual(decide("mv link other"), "")
                self.assertIn("contains file_glob 'docs/lib/*'",
                              decide("rm -r link/"))
                os.remove(os.path.join(self.repo_path, "link"))
        shutil.rmtree(os.path.join(self.repo_path, "docs"))
        self.assertIn("contains file_glob 'docs/lib/*'", decide("mv src docs"))

    def test_bash_line_continuations_join_before_parsing(self):
        # Review of #178: Bash drops an unquoted backslash-newline.
        for command in ("git \\\nreset --hard",
                        "echo a\\\\\ngit reset --hard",
                        "cat <<'EOF'\nx\\\nEOF\ngit reset --hard"):
            with self.subTest(command=command):
                self.assertIsNotNone(coordination.deny_reason_for_shell(
                    command, self.repo_path, self.repo), command)
        for command in ("echo 'a\\\n' && git status",
                        'echo "foo\\\n; git reset --hard"'):
            with self.subTest(command=command):
                self.assertIsNone(coordination.deny_reason_for_shell(
                    command, self.repo_path, self.repo), command)
        # Escaped separators are literal; an escaped backslash is not.
        self.assertIsNone(coordination.deny_reason_for_shell(
            r"echo a \; git reset --hard", self.repo_path, self.repo))
        self.assertIsNotNone(coordination.deny_reason_for_shell(
            r"echo a \\; git reset --hard", self.repo_path, self.repo))

    def test_bash_redirect_backslash_escape_reaches_claimed_destination(self):
        # #178: Bash reads `s\\rc` as `src`; PowerShell keeps `\\` a separator.
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        coordination.claim(self.repo, "s1", "branch", "feature")
        coordination.claim(self.repo, "s2", "file_glob", "src/*")
        for command in (r"printf x > s\rc/code.cs", r"printf x >s\rc/code.cs 2>&1",
                        "printf x > s\\\nrc/code.cs",
                        r"printf x > src/\;code.cs"):
            with self.subTest(command=command):
                rc, out = self.decide({
                    "tool_name": "Bash", "tool_input": {"command": command},
                    "cwd": self.repo_path, "session_id": "s1",
                })
                self.assertIn("claimed by session s2", out)
        self.assertEqual(
            coordination.shell_write_targets(r"printf x > a\ b.txt", "/r"),
            [os.path.join("/r", "a b.txt")])
        self.assertEqual(coordination.shell_write_targets(
            r"Write-Output x > s\rc\code.cs", "/r", ps=True),
            [os.path.join("/r", "s/rc/code.cs")])

    def test_ambiguous_data_is_conservative_but_explicit_shell_data_allowed(self):
        # Each string is harmless data in its actual shell; the other parser
        # sees a prohibited command, so ambiguous input deliberately denies.
        for shell, command in (
                ("/bin/bash", r'echo "a\"; git reset --hard; y"'),
                ("pwsh", r'Write-Output "a`"; git reset --hard; y"')):
            with self.subTest(shell=shell), mock.patch.object(
                    sys, "platform", "win32"), mock.patch.dict(os.environ, {
                        "CODEX_THREAD_ID": "codex-thread", "CLAUDECODE": "",
                    }):
                rc, out = self.decide({
                    "tool_name": "Bash", "tool_input": {
                        "command": command, "shell": shell,
                    }, "cwd": self.repo_path,
                })
                self.assertEqual(out, "")
                rc, out = self.decide({
                    "tool_name": "Bash", "tool_input": {"command": command},
                    "cwd": self.repo_path,
                })
                self.assertIn("main is read-only", out)

    def test_powershell_literal_backslash_quote_keeps_main_writes_visible(self):
        for command in (
                r'Write-Output "x\"; Set-Content seed.txt boom; Write-Output "y"',
                r'Write-Output "x\"; git reset --hard; Write-Output "y"'):
            for tool, shell in (("PowerShell", ""), ("Bash", "pwsh")):
                with self.subTest(command=command, tool=tool):
                    rc, out = self.decide({
                        "tool_name": tool, "tool_input": {
                            "command": command, "shell": shell,
                        }, "cwd": self.repo_path,
                    })
                    self.assertIn("main is read-only", out)

    def test_powershell_literal_backslash_quote_keeps_claim_writes_visible(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        coordination.claim(self.repo, "s1", "branch", "feature")
        coordination.claim(self.repo, "s2", "file_glob", "src/*")
        command = r'Write-Output "x\"; Remove-Item src/code.cs; Write-Output "y"'
        rc, out = self.decide({
            "tool_name": "PowerShell", "tool_input": {"command": command},
            "cwd": self.repo_path, "session_id": "s1",
        })
        self.assertIn("claimed by session s2", out)

    def test_powershell_quote_escapes_keep_statement_text_literal(self):
        for command in (
                r'Write-Output "x`"; Remove-Item seed.txt; y"',
                r"Write-Output 'x''; Remove-Item seed.txt; y'",
                r'Write-Output "x""; Remove-Item seed.txt; y"'):
            with self.subTest(command=command):
                self.assertEqual(coordination.shell_write_targets(
                    command, self.repo_path, ps=True), [])
                self.assertIsNone(coordination.deny_reason_for_shell(
                    command, self.base, None, ps=True))
                rc, out = self.decide({
                    "tool_name": "PowerShell", "tool_input": {"command": command},
                    "cwd": self.repo_path,
                })
                self.assertEqual(out, "")

    # #179/#187: PowerShell data versus statement boundaries. Each write
    # case hides `Set-Content seed.txt` behind a quoting form; each data
    # case carries a redirect or write only inside a string.
    PS_HIDDEN_WRITES = (
        "$p = @'\n\"\n'@; Set-Content seed.txt boom; Write-Output \"z\"",
        "$p = @'\n'\n'@; Set-Content seed.txt boom; Write-Output 'z'",
        "Write-Output \u2018x'; Set-Content seed.txt boom; Write-Output 'y'",
        "Write-Output \u201cx\"; Set-Content seed.txt boom; Write-Output \"y\"",
        "Write-Output `\"; Set-Content seed.txt boom; Write-Output \"y\"",
        "Write-Output `'; Set-Content seed.txt boom; Write-Output 'y'",
        "Set-Content \u2018seed.txt\u2019 boom",
        "Write-Output x@'\n'; Set-Content seed.txt boom; Write-Output y'q\n'@z",
        "Write-Output x=@'\n'; Set-Content seed.txt boom; Write-Output y'q\n'@z",
        "$p = @'\r'\r'@; Set-Content seed.txt boom; Write-Output 'z'",
        "$p=@'\n'\n'@; Set-Content seed.txt boom",
        "Write-Output a\rSet-Content seed.txt boom",
        "Write-Output $p=@'\n'; Set-Content seed.txt boom; Write-Output y'q\n'@z",
        "a && $p=@'\n'; Set-Content seed.txt boom; Write-Output y'q\n'@z",
        "${p}=@'\n'\n'@; Set-Content seed.txt boom; Write-Output 'z'",
        "${env:a b}=@'\n'\n'@; Set-Content seed.txt boom; Write-Output 'z'",
        "$p??=@'\n'\n'@; Set-Content seed.txt boom; Write-Output 'z'",
        "[int[]]$p.'a'=@'\n'\n'@; Set-Content seed.txt boom; Write-Output 'z'",
        "$p=@()+@'\n'\n'@; Set-Content seed.txt boom; Write-Output 'z'",
        "$p=1,@\"\n\"\n\"@; Set-Content seed.txt boom; Write-Output \"z\"",
        "# @'\n# '\nSet-Content seed.txt boom\n'@' | Out-Null",
        "<# @' #>\nSet-Content seed.txt boom\n'@' | Out-Null",
        "Write-Output a # it's\nSet-Content seed.txt boom; Write-Output 'z'",
        "$x=# @'\n# '\nSet-Content seed.txt boom\n'@' | Out-Null",
        "Write-Output x`\n#tag; Set-Content seed.txt boom",
        "Write-Output 'a'# '\nSet-Content seed.txt boom\nWrite-Output 'z'",
        "$x.y# '\nSet-Content seed.txt boom\nWrite-Output 'z'",
        "$x = Set-Content seed.txt boom",
        "[string]$x=Set-Content seed.txt boom",
        "$x=`\nSet-Content seed.txt boom",
        "Write-Output a\n`\nSet-Content seed.txt boom",
        "Write-Output x`\r\nSet-Content seed.txt boom",
    )
    PS_DATA_ONLY = (
        "$p = @'\nprint(targets('echo x > seed.txt'))\n'@\n$p | python -",
        "$p = @\"\nx > seed.txt\n\"@\n$p | python -",
        "$p=@'\r\nx > seed.txt\r\n'@\r\n$p | Out-Null",
        "Write-Output \u2018a\u2019\u2019; Remove-Item seed.txt; b\u2019",
        "a; $q = $p=@'\nx > seed.txt\n'@",
        "{ [string]$p.a[0] += @'\nx > seed.txt\n'@ }",
        "Write-Output x`; Remove-Item seed.txt",
        "Write-Output `@'\n; Remove-Item seed.txt\n'",
        "Write-Output x`|Remove-Item seed.txt`\nboom",
    )

    def ps_command_names(self, source):
        """Command names in PowerShell's own AST, or None without pwsh."""
        pwsh = shutil.which("pwsh")
        if not pwsh:
            return None
        script = ("$e = $null; [System.Management.Automation.Language.Parser]"
                  "::ParseInput($env:PS_SRC, [ref]$null, [ref]$e).FindAll("
                  "{ $args[0] -is [System.Management.Automation.Language."
                  "CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() }")
        out = subprocess.run(
            [pwsh, "-NoProfile", "-NonInteractive", "-Command", script],
            env={**os.environ, "PS_SRC": source}, capture_output=True,
            text=True, encoding="utf-8", check=True).stdout
        return out.split()

    def test_powershell_quoting_forms_keep_writes_visible(self):
        for command in self.PS_HIDDEN_WRITES:
            with self.subTest(command=command):
                names = self.ps_command_names(command)
                if names is not None:
                    self.assertIn("Set-Content", names)
                rc, out = self.decide({
                    "tool_name": "PowerShell", "tool_input": {"command": command},
                    "cwd": self.repo_path,
                })
                # An opener outside the modeled here-string starts fails
                # closed instead (#226 review).
                self.assertRegex(out, "main is read-only|here-string opener")

    def test_powershell_quoting_forms_keep_claim_writes_visible(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        coordination.claim(self.repo, "s1", "branch", "feature")
        coordination.claim(self.repo, "s2", "file_glob", "src/*")
        for command in self.PS_HIDDEN_WRITES:
            command = command.replace("seed.txt", "src/code.cs")
            with self.subTest(command=command):
                rc, out = self.decide({
                    "tool_name": "PowerShell", "tool_input": {"command": command},
                    "cwd": self.repo_path, "session_id": "s1",
                })
                self.assertRegex(
                    out, "claimed by session s2|here-string opener")

    def test_powershell_string_data_is_not_a_write(self):
        for command in self.PS_DATA_ONLY:
            with self.subTest(command=command):
                names = self.ps_command_names(command)
                if names is not None:
                    self.assertNotIn("Remove-Item", names)
                self.assertEqual(coordination.shell_write_targets(
                    command, self.repo_path, ps=True), [])
                self.assertIsNone(coordination.deny_reason_for_shell(
                    command, self.base, None, ps=True))
        # An actual redirect after the here-string is still enforced.
        rc, out = self.decide({
            "tool_name": "PowerShell", "tool_input": {
                "command": "$p = @'\nx > other.txt\n'@\n$p > seed.txt",
            }, "cwd": self.repo_path,
        })
        self.assertIn("main is read-only", out)

    def test_bash_and_cmd_quote_rules_are_preserved(self):
        rc, out = self.decide({
            "tool_name": "Bash", "tool_input": {
                "command": r'printf "x\"; rm seed.txt; y"', "shell": "/bin/bash",
            }, "cwd": self.repo_path,
        })
        self.assertEqual(out, "")
        rc, out = self.decide({
            "tool_name": "Bash", "tool_input": {
                "command": r'echo "x\" & del seed.txt & echo "y"', "shell": "cmd.exe",
            }, "cwd": self.repo_path,
        })
        self.assertIn("main is read-only", out)

    def test_cmd_caret_escapes_are_literal(self):
        # #185: `^&` and `^>` are text in cmd; unescaped operators still deny.
        # The outer cmd consumes one caret layer before a child cmd reparses.
        for command, denied in (("cmd /c echo ^& git reset --hard", True),
                                ("cmd /c echo ^^^& git reset --hard", False),
                                ("echo a ^& git reset --hard", False),
                                ("echo a ^> seed.txt", False),
                                ("echo a & git reset --hard", True),
                                ("echo a ^^& git reset --hard", True),
                                ('echo "a ^" & git reset --hard', True)):
            # Both outer shells pass a single-quoted script through intact.
            for text, shell in ((f"cmd /c '{command}'", "pwsh"),
                                (f"cmd /c '{command}'", "/bin/bash"),
                                (command, "cmd.exe")):
                with self.subTest(command=command, shell=shell):
                    rc, out = self.decide({
                        "tool_name": "Bash", "tool_input": {
                            "command": text, "shell": shell,
                        }, "cwd": self.repo_path,
                    })
                    if denied:
                        self.assertIn("main is read-only", out)
                    else:
                        self.assertEqual(out, "")

    def test_cmd_caret_escaped_paths_reach_claims(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        coordination.claim(self.repo, "s1", "branch", "feature")
        coordination.claim(self.repo, "s2", "file_glob", "src/*")
        for command in (r"del s^rc\code.cs", r"echo x > s^rc\code.cs"):
            with self.subTest(command=command):
                rc, out = self.decide({
                    "tool_name": "Bash", "tool_input": {
                        "command": command, "shell": "cmd.exe",
                    }, "cwd": self.repo_path, "session_id": "s1",
                })
                self.assertIn("claimed by session s2", out)

    def test_cmd_scripts_preserve_windows_git_paths(self):
        other = make_repo(self.base, "other-main")
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        commands = (
            (r'cmd /c "C:\Git\cmd\git.exe reset --hard"', other),
            (f'cmd /c "git -C {other} reset --hard"', self.repo_path),
            (f'cmd /c "git -C {other} checkout -- ."', self.repo_path),
            (f'cmd /c "cd /d {other} & git reset --hard"', self.repo_path),
            (f'''cmd /c "echo 'literal & git -C {other} reset --hard & echo 'rest"''',
             self.repo_path),
        )
        for tool, shell in (("PowerShell", "pwsh"), ("Bash", "/bin/bash")):
            for command, cwd in commands:
                with self.subTest(tool=tool, command=command):
                    rc, out = self.decide({
                        "tool_name": tool, "tool_input": {
                            "command": command, "shell": shell,
                        }, "cwd": cwd,
                    })
                    self.assertIn("main is read-only", out)

    def test_pushd_reaches_foreign_claims(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        coordination.claim(self.repo, "s1", "branch", "feature")
        coordination.claim(self.repo, "s2", "file_glob", "src/*")
        for tool, command in (
                ("Bash", "pushd src && rm code.cs"),
                ("PowerShell", "Push-Location src; Remove-Item code.cs")):
            with self.subTest(command=command):
                rc, out = self.decide({
                    "tool_name": tool, "tool_input": {"command": command},
                    "cwd": self.repo_path, "session_id": "s1",
                })
                self.assertIn("claimed by session s2", out)

    def test_nested_interpreter_forms_reach_main_rule(self):
        # #182: unquoted cmd /c tails, path- or .exe-supplied interpreters,
        # Git Bash's //c, and multi-argument pwsh -Command.
        for command in (
                "cmd /c git reset --hard",
                'cmd //c "git reset --hard"',
                "C:/Windows/System32/cmd.exe /c git reset --hard",
                'powershell.exe -Command "git reset --hard"',
                "pwsh.exe -c git reset --hard",
                '/usr/bin/bash -c "git reset --hard"',
                'bash.exe -c "git reset --hard"',
                '/usr/bin/env bash -c "git reset --hard"'):
            for tool, shell in (("Bash", "/bin/bash"), ("PowerShell", "pwsh")):
                with self.subTest(tool=tool, command=command):
                    rc, out = self.decide({
                        "tool_name": tool, "tool_input": {
                            "command": command, "shell": shell,
                        }, "cwd": self.repo_path,
                    })
                    self.assertIn("main is read-only", out)

    def test_nested_interpreter_forms_reach_claims(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        coordination.claim(self.repo, "s1", "branch", "feature")
        coordination.claim(self.repo, "s2", "file_glob", "src/*")
        for command in (
                r"C:\Windows\System32\cmd.exe /c del /q src\code.cs",
                r"cmd //c del /q src\code.cs",
                "pwsh.exe -Command Remove-Item src/code.cs",
                '/bin/sh -c "rm src/code.cs"'):
            with self.subTest(command=command):
                rc, out = self.decide({
                    "tool_name": "PowerShell", "tool_input": {
                        "command": command,
                    }, "cwd": self.repo_path, "session_id": "s1",
                })
                self.assertIn("claimed by session s2", out)
        # A quoted cmd argument stays one argument when the tail is re-joined.
        self.assertEqual(coordination.shell_write_targets(
            'cmd /c echo "a & del src/code.cs"', self.repo_path), [])

    def test_cmd_write_aliases_reach_claims_without_powershell_aliases(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        coordination.claim(self.repo, "s1", "branch", "feature")
        coordination.claim(self.repo, "s2", "file_glob", "src/*")
        for outer, shell in (("cmd /c ", "pwsh"), ("", "cmd.exe")):
            for command in (r'del /q src\code.cs', r'copy own.txt src\code.cs',
                            r'move src\code.cs own.txt'):
                with self.subTest(outer=outer, command=command):
                    rc, out = self.decide({
                        "tool_name": "Bash", "tool_input": {
                            "command": outer + '"' + command + '"' if outer else command,
                            "shell": shell,
                        }, "cwd": self.repo_path, "session_id": "s1",
                    })
                    self.assertIn("claimed by session s2", out)
            for command in ("sc query", r'copy src\code.cs own.txt',
                            r"Set-Content src\code.cs value"):
                with self.subTest(outer=outer, command=command):
                    rc, out = self.decide({
                        "tool_name": "Bash", "tool_input": {
                            "command": outer + '"' + command + '"' if outer else command,
                            "shell": shell,
                        }, "cwd": self.repo_path, "session_id": "s1",
                    })
                    self.assertEqual(out, "")

    def test_non_string_shell_metadata_keeps_existing_protection(self):
        for shell in (None, ["pwsh"], 5):
            with self.subTest(shell=shell):
                rc, out = self.decide({
                    "tool_name": "Bash", "tool_input": {
                        "command": "git reset --hard", "shell": shell,
                    }, "cwd": self.repo_path,
                })
                self.assertIn("main is read-only", out)
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        coordination.claim(self.repo, "s1", "branch", "feature")
        coordination.claim(self.repo, "s2", "file_glob", "src/*")
        for shell in (None, ["pwsh"], 5):
            with self.subTest(shell=shell):
                rc, out = self.decide({
                    "tool_name": "Bash", "tool_input": {
                        "command": "rm src/code.cs", "shell": shell,
                    }, "cwd": self.repo_path, "session_id": "s1",
                })
                self.assertIn("claimed by session s2", out)

    def test_codex_windows_bash_powershell_writes_on_main_denied(self):
        for command in ("Set-Content seed.txt boom",
                        "'boom' | Out-File seed.txt",
                        r"Set-Content .\seed.txt boom",
                        r"'boom' > .\seed.txt"):
            with self.subTest(command=command), mock.patch.object(
                    sys, "platform", "win32"), mock.patch.dict(os.environ, {
                        "CODEX_THREAD_ID": "codex-thread", "CLAUDECODE": "",
                    }):
                rc, out = self.decide({
                    "tool_name": "Bash", "tool_input": {"command": command},
                    "cwd": self.repo_path,
                })
                self.assertIn("main is read-only", out)

    def test_codex_windows_powershell_writes_reach_claim_layer(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        coordination.claim(self.repo, "s1", "branch", "feature")
        coordination.claim(self.repo, "s2", "file_glob", "src/*")
        for command in ("Set-Content src/code.cs boom",
                        "'boom' | Out-File src/code.cs",
                        r"'boom' > src\code.cs"):
            with self.subTest(command=command), mock.patch.object(
                    sys, "platform", "win32"), mock.patch.dict(os.environ, {
                        "CODEX_THREAD_ID": "codex-thread", "CLAUDECODE": "",
                    }):
                rc, out = self.decide({
                    "tool_name": "Bash", "tool_input": {"command": command},
                    "session_id": "s1", "cwd": self.repo_path,
                })
                self.assertIn("claimed by session s2", out)

    def test_codex_windows_nested_bash_keeps_raw_script(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        coordination.claim(self.repo, "s1", "branch", "feature")
        coordination.claim(self.repo, "s2", "file_glob", "src/*")
        command = r"bash -c 'rm s\rc/code.cs'"
        # Bash removes the backslash escape: the real target is src/code.cs.
        self.assertEqual(coordination.shell_write_targets(command, self.repo_path),
                         [os.path.join(self.repo_path, "src/code.cs")])
        with mock.patch.object(sys, "platform", "win32"), mock.patch.dict(
                os.environ, {"CODEX_THREAD_ID": "codex-thread", "CLAUDECODE": ""}):
            rc, out = self.decide({
                "tool_name": "Bash", "tool_input": {"command": command},
                "session_id": "s1", "cwd": self.repo_path,
            })
            self.assertIn("claimed by session s2", out)
            rc, out = self.decide({
                "tool_name": "Bash", "tool_input": {
                    "command": "Set-Content own.txt boom",
                }, "session_id": "s1", "cwd": self.repo_path,
            })
            self.assertEqual(out, "")
            rc, out = self.decide({
                "tool_name": "Bash", "tool_input": {
                    "command": r"rm src\code.cs", "shell": "bash.exe",
                }, "session_id": "s1", "cwd": self.repo_path,
            })
            self.assertEqual(out, "")

    def test_explicit_shell_overrides_codex_bash_label(self):
        for shell in ("powershell", "pwsh.exe",
                      r"C:\Program Files\PowerShell\7\pwsh.exe"):
            with self.subTest(shell=shell):
                rc, out = self.decide({
                    "tool_name": "Bash", "tool_input": {
                        "command": "Set-Content seed.txt boom", "shell": shell,
                    }, "cwd": self.repo_path,
                })
                self.assertIn("main is read-only", out)
        with mock.patch.object(sys, "platform", "win32"), mock.patch.dict(
                os.environ, {"CODEX_THREAD_ID": "codex-thread", "CLAUDECODE": ""}):
            rc, out = self.decide({
                "tool_name": "Bash", "tool_input": {
                    "command": "printf x > /dev/null", "shell": "/bin/bash",
                }, "cwd": self.repo_path,
            })
            self.assertEqual(out, "")
            rc, out = self.decide({
                "tool_name": "Bash", "tool_input": {
                    "command": "printf x > se\\ed.txt", "shell": "/bin/bash",
                }, "cwd": self.repo_path,
            })
            self.assertIn("main is read-only", out)

    def test_claude_bash_on_windows_keeps_bash_semantics(self):
        with mock.patch.object(sys, "platform", "win32"), mock.patch.dict(
                os.environ, {"CODEX_THREAD_ID": "inherited-codex-thread",
                             "CLAUDECODE": "1"}):
            rc, out = self.decide({
                "tool_name": "Bash", "tool_input": {"command": "sc query"},
                "cwd": self.repo_path,
            })
            self.assertEqual(out, "")
            rc, out = self.decide({
                "tool_name": "Bash", "tool_input": {"command": "printf x > se\\ed.txt"},
                "cwd": self.repo_path,
            })
            self.assertIn("main is read-only", out)

    def test_benign_edit_on_branch_silent(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        # This case exercises the no-session path, independent of the caller.
        with mock.patch.dict(os.environ, {"AGENT_SESSION_ID": ""}):
            rc, out = self.decide({
                "tool_name": "Edit",
                "tool_input": {"file_path": os.path.join(self.repo_path, "seed.txt")},
                "cwd": self.repo_path,
            })
        self.assertEqual(out, "")

    def apply_patch(self, *headers, session="s1"):
        # Codex sends file edits as tool "apply_patch" with the raw patch
        # text in tool_input.command (#153).
        patch = "*** Begin Patch\n" + "".join(
            f"{h}\n+x\n" for h in headers) + "*** End Patch\n"
        with mock.patch.dict(os.environ, {"AGENT_SESSION_ID": ""}):
            return self.decide({
                "session_id": session,
                "tool_name": "apply_patch",
                "tool_input": {"command": patch},
                "cwd": self.repo_path,
            })

    def test_apply_patch_on_main_emits_deny(self):
        rc, out = self.apply_patch("*** Add File: probe.txt")
        self.assertIn("deny", out)

    def test_apply_patch_on_unclaimed_branch_denied(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        rc, out = self.apply_patch("*** Add File: probe.txt")
        self.assertIn("no claim for branch 'feature'", out)

    def test_apply_patch_checks_every_file_in_the_patch(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        coordination.claim(self.repo, "s1", "branch", "feature")
        coordination.claim(self.repo, "s1", "file_glob", "src/*")
        rc, out = self.apply_patch(
            "*** Update File: src/a.cs", "*** Delete File: docs/x.md")
        self.assertIn("outside your claimed file scope", out)
        rc, out = self.apply_patch(
            "*** Update File: src/a.cs\n*** Move to: docs/x.md")
        self.assertIn("outside your claimed file scope", out)

    def test_freeform_apply_patch_gets_the_same_guards(self):
        # #181: a plain-string tool_input must not skip main/claim guards.
        patch = "*** Begin Patch\n*** Add File: probe.txt\n+x\n*** End Patch\n"

        def decide(tool_input):
            with mock.patch.dict(os.environ, {"AGENT_SESSION_ID": ""}):
                return self.decide({"session_id": "s1", "tool_name": "apply_patch",
                                    "tool_input": tool_input,
                                    "cwd": self.repo_path})[1]

        self.assertIn("main is read-only", decide(patch))
        self.assertEqual(decide(patch), decide({"command": patch}))
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        self.assertIn("no claim for branch 'feature'", decide(patch))
        coordination.claim(self.repo, "s1", "branch", "feature")
        self.assertEqual(decide(patch), "")

    def test_apply_patch_on_claimed_branch_silent(self):
        run_git(["checkout", "-q", "-b", "feature"], self.repo_path)
        coordination.claim(self.repo, "s1", "branch", "feature")
        rc, out = self.apply_patch("*** Add File: probe.txt")
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
        # Left untracked, it no longer blocks the fast-forward (#218).
        self.assertEqual(coordination.cmd_sync_main(self.repo),
                         coordination.ALLOW)
        self.assertTrue(os.path.exists(os.path.join(self.repo_path, "adv.txt")))


class TestDoctorLiveness(TempRepoCase):
    @staticmethod
    def dead_pid():
        proc = subprocess.Popen([sys.executable, "-c", "pass"])
        proc.wait()
        return proc.pid

    def write_lock(self, content):
        lock = self.repo.registry_path + coordination.LOCK_SUFFIX
        os.makedirs(os.path.dirname(lock), exist_ok=True)
        with open(lock, "w", encoding="utf-8") as fh:
            fh.write(content)

    def write_claims(self, *overrides):
        base = {"id": "abc123def456", "session": "sess-1", "harness": "",
                "pid": os.getpid(), "host": socket.gethostname(),
                "kind": "branch", "value": "feature-x",
                "created_utc": coordination._now()}
        data = coordination._empty_registry()
        data["claims"] = [{**base, **record} for record in overrides]
        os.makedirs(os.path.dirname(self.repo.registry_path), exist_ok=True)
        coordination._save(self.repo.registry_path, data)

    def write_claim(self, **overrides):
        self.write_claims(overrides)

    def test_pid_alive_true_for_own_process(self):
        snapshot = coordination._pid_snapshot()
        self.assertIs(coordination._pid_alive(os.getpid(), snapshot), True)

    def test_pid_alive_unknown_for_garbage_pid(self):
        self.assertIsNone(coordination._pid_alive("garbage", None))
        self.assertIsNone(coordination._pid_alive(-4, set()))
        self.assertIsNone(coordination._pid_alive(None, {1, 2}))

    def test_same_host_dead_pid_claim_reported_orphaned(self):
        self.write_claim(pid=self.dead_pid())
        out = self.doctor_output()
        self.assertIn("orphaned claim abc123def456", out)
        self.assertIn("release --force --id abc123def456", out)

    def test_distinct_pid_spread_falls_back_to_age_only(self):
        old = (_dt.datetime.now(_dt.timezone.utc)
               - _dt.timedelta(hours=48)).strftime("%Y-%m-%dT%H:%M:%SZ")
        self.write_claims(
            {"id": "spread000001", "pid": 101, "created_utc": old},
            {"id": "spread000002", "pid": "202", "created_utc": old,
             "kind": "worktree", "value": ".worktrees/codex+issue-91"},
        )
        with mock.patch.object(coordination, "_pid_alive",
                               return_value=False):
            out = self.doctor_output()
        self.assertNotIn("orphaned claim spread", out)
        self.assertIn("stale-suspect claim spread000001", out)
        self.assertIn("stale-suspect claim spread000002", out)

    def test_pid_spread_rejects_repeated_dead_pid(self):
        self.write_claims(
            {"id": "repeat000001", "pid": 101},
            {"id": "repeat000002", "pid": "101", "kind": "worktree",
             "value": ".worktrees/codex+issue-91"},
        )
        with mock.patch.object(coordination, "_pid_alive",
                               return_value=False):
            out = self.doctor_output()
        self.assertIn("orphaned claim repeat000001", out)
        self.assertIn("orphaned claim repeat000002", out)

    def test_pid_spread_is_scoped_to_session(self):
        self.write_claims(
            {"id": "sess10000001", "pid": 101, "session": "sess-1"},
            {"id": "sess20000002", "pid": 202, "session": "sess-2"},
        )
        with mock.patch.object(coordination, "_pid_alive",
                               return_value=False):
            out = self.doctor_output()
        self.assertIn("orphaned claim sess10000001", out)
        self.assertIn("orphaned claim sess20000002", out)

    def test_pid_spread_is_scoped_to_host(self):
        self.write_claims(
            {"id": "local0000001", "pid": 101},
            {"id": "foreign00001", "pid": 202, "host": "elsewhere"},
        )
        with mock.patch.object(coordination, "_pid_alive",
                               return_value=False):
            out = self.doctor_output()
        self.assertIn("orphaned claim local0000001", out)
        self.assertNotIn("orphaned claim foreign00001", out)

    def test_pid_spread_ignores_unusable_pid(self):
        self.write_claims(
            {"id": "valid0000001", "pid": 101},
            {"id": "zero00000002", "pid": 0, "kind": "worktree",
             "value": ".worktrees/codex+issue-91"},
        )
        with mock.patch.object(coordination, "_pid_alive",
                               return_value=False):
            out = self.doctor_output()
        self.assertIn("orphaned claim valid0000001", out)

    def test_pid_spread_ignores_adr_pid(self):
        self.write_claims(
            {"id": "valid0000001", "pid": 101},
            {"id": "adr000000002", "pid": 202, "kind": "adr",
             "value": "0005"},
        )
        with mock.patch.object(coordination, "_pid_alive",
                               return_value=False):
            out = self.doctor_output()
        self.assertIn("orphaned claim valid0000001", out)
        self.assertNotIn("orphaned claim adr000000002", out)

    def test_pid_spread_ignores_unusable_session(self):
        self.write_claim(session=["bad"], pid=101)
        with mock.patch.object(coordination, "_pid_alive",
                               return_value=False):
            out = self.doctor_output()
        self.assertIn("orphaned claim abc123def456", out)

    def test_pid_spread_ignores_nonfinite_pid(self):
        old = (_dt.datetime.now(_dt.timezone.utc)
               - _dt.timedelta(hours=48)).strftime("%Y-%m-%dT%H:%M:%SZ")
        self.write_claim(pid=float("inf"), created_utc=old)
        out = self.doctor_output()
        self.assertNotIn("orphaned claim abc123def456", out)
        self.assertIn("stale-suspect claim abc123def456", out)

    def test_live_claim_not_reported_orphaned(self):
        self.write_claim()  # own pid, own host
        self.assertNotIn("orphaned claim", self.doctor_output())

    def test_foreign_host_dead_pid_not_reported_orphaned(self):
        self.write_claim(pid=self.dead_pid(), host="elsewhere")
        self.assertNotIn("orphaned claim", self.doctor_output())

    def test_adr_claim_exempt_from_orphan_check(self):
        self.write_claim(kind="adr", value="0099", pid=self.dead_pid())
        self.assertNotIn("orphaned claim", self.doctor_output())

    def test_probe_unknown_falls_back_to_age_only(self):
        old = (_dt.datetime.now(_dt.timezone.utc)
               - _dt.timedelta(hours=48)).strftime("%Y-%m-%dT%H:%M:%SZ")
        self.write_claim(pid=self.dead_pid(), created_utc=old)
        with mock.patch.object(coordination, "_pid_alive",
                               return_value=None):
            out = self.doctor_output()
        self.assertNotIn("orphaned claim", out)
        self.assertIn("stale-suspect claim", out)

    def test_malformed_claim_timestamp_is_a_finding(self):
        self.write_claim(created_utc="not-a-timestamp")
        output = self.doctor_output()
        self.assertIn("unparseable created_utc", output)

    def test_claim_without_kind_is_a_finding(self):
        self.write_claim()
        data = coordination._load(self.repo.registry_path)
        del data["claims"][0]["kind"]
        coordination._save(self.repo.registry_path, data)
        output = self.doctor_output()
        self.assertIn("missing kind", output)

    def test_cli_claim_not_reported_orphaned(self):
        proc = subprocess.run(
            [sys.executable, coordination.__file__, "claim",
             "--session", "cli-sess", "--kind", "branch",
             "--value", "feature-cli"],
            capture_output=True, text=True, cwd=self.repo_path, timeout=60)
        self.assertEqual(proc.returncode, 0, proc.stderr)
        self.assertEqual(len(coordination.list_claims(self.repo)), 1)
        self.assertNotIn("orphaned claim", self.doctor_output())

    def test_lock_with_dead_local_holder_reported_removable(self):
        pid = self.dead_pid()
        self.write_lock(f"{pid}@{socket.gethostname()} 2026-08-07T00:00:00Z")
        self.assertIn(
            f"holder pid {pid} is dead on this host; safe to remove",
            self.doctor_output())

    def test_lock_with_live_local_holder_reported_alive(self):
        self.write_lock(
            f"{os.getpid()}@{socket.gethostname()} 2026-08-07T00:00:00Z")
        self.assertIn(f"holder pid {os.getpid()} appears alive; hands off",
                      self.doctor_output())

    def test_lock_with_garbage_content_keeps_neutral_wording(self):
        self.write_lock("not a holder line")
        self.assertIn("verify the holder is alive before removing manually",
                      self.doctor_output())

    def test_lock_on_foreign_host_keeps_neutral_wording(self):
        self.write_lock(f"{self.dead_pid()}@elsewhere 2026-08-07T00:00:00Z")
        self.assertIn("verify the holder is alive before removing manually",
                      self.doctor_output())


class TestSessionPid(unittest.TestCase):
    def test_walk_skips_transients_to_app_ancestor(self):
        table = {10: (20, "python"), 20: (30, "bash"),
                 30: (40, "node"), 40: (0, "explorer")}
        self.assertEqual(coordination._first_app_ancestor(10, table), 30)

    def test_walk_all_transient_chain_returns_none(self):
        table = {10: (20, "pythonw"), 20: (0, "pwsh")}
        self.assertIsNone(coordination._first_app_ancestor(10, table))

    def test_walk_start_pid_missing_returns_none(self):
        self.assertIsNone(
            coordination._first_app_ancestor(99, {1: (0, "node")}))

    def test_walk_cyclic_table_returns_none(self):
        table = {10: (20, "python"), 20: (10, "cmd")}
        self.assertIsNone(coordination._first_app_ancestor(10, table))

    def test_session_pid_live_resolves_alive_non_cli_pid(self):
        pid = coordination._session_pid()
        if pid is None:
            self.skipTest("no resolvable app ancestor on this host")
        self.assertNotEqual(pid, os.getpid())
        self.assertIs(
            coordination._pid_alive(pid, coordination._pid_snapshot()),
            True)


if __name__ == "__main__":
    unittest.main(verbosity=2)

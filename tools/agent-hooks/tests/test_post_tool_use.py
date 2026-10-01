"""Self-check for the PostToolUse feedback hook.

The command runner is injected, so no test, dotnet, or gh runs for real. File
layouts that the logic inspects (a tool's tests/ dir, a src project and its
convention-mapped .Tests project) are built in a tempdir.
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest

TOOL_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, TOOL_DIR)

import post_tool_use  # noqa: E402
from post_tool_use import handle, _is_git_push, _pushed_branch  # noqa: E402


def stub(*responses):
    """responses: (predicate, (rc, out)) pairs. Records every argv seen."""
    calls = []

    def run(argv):
        calls.append(argv)
        for predicate, result in responses:
            if predicate(argv):
                return result
        return (0, "")

    run.calls = calls
    return run


def is_unittest(argv):
    return argv[:3] == ["python", "-m", "unittest"]


def is_dotnet(argv):
    return bool(argv) and argv[0] == "dotnet"


def is_gh_pr(argv):
    return argv[:3] == ["gh", "pr", "view"]


class PostEditPythonTests(unittest.TestCase):
    def setUp(self):
        self.root = tempfile.mkdtemp(prefix="post-tool-")
        os.makedirs(os.path.join(self.root, "tools", "mytool", "tests"))
        open(os.path.join(self.root, "tools", "mytool", "mytool.py"), "w").close()

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def _edit(self, rel):
        return {"tool_name": "Edit",
                "tool_input": {"file_path": os.path.join(self.root, rel)}}

    def test_failing_suite_is_surfaced(self):
        run = stub((is_unittest, (1, "F\nFAILED (failures=1)")))
        ctx = handle(self._edit("tools/mytool/mytool.py"), self.root, {}, run)
        self.assertIsNotNone(ctx)
        self.assertIn("tools/mytool", ctx)
        self.assertIn("FAILED", ctx)

    def test_passing_suite_is_silent(self):
        run = stub((is_unittest, (0, "OK")))
        ctx = handle(self._edit("tools/mytool/mytool.py"), self.root, {}, run)
        self.assertIsNone(ctx)

    def test_tool_without_tests_dir_never_runs(self):
        os.makedirs(os.path.join(self.root, "tools", "notests"))
        open(os.path.join(self.root, "tools", "notests", "x.py"), "w").close()
        run = stub((is_unittest, (1, "FAILED")))
        ctx = handle(self._edit("tools/notests/x.py"), self.root, {}, run)
        self.assertIsNone(ctx)
        self.assertEqual(run.calls, [])

    def test_non_python_edit_never_runs(self):
        run = stub()
        ctx = handle(self._edit("tools/mytool/readme.md"), self.root, {}, run)
        self.assertIsNone(ctx)
        self.assertEqual(run.calls, [])

    def _patch(self, command, response=None, cwd=None):
        return {"tool_name": "apply_patch", "tool_input": command,
                "tool_response": {"exit_code": 0} if response is None else response,
                "cwd": cwd or self.root}

    def test_patch_checks_add_update_delete_and_both_move_targets(self):
        for name in ("added", "deleted", "moved", "destination"):
            os.makedirs(os.path.join(self.root, "tools", name, "tests"))
        patch = "\n".join((
            "*** Begin Patch", "*** Add File: tools/added/new.py", "+pass",
            "*** Update File: tools/mytool/mytool.py", "@@", "-old", "+new",
            "*** Delete File: tools/deleted/old.py",
            "*** Update File: tools/moved/old.py",
            "*** Move to: tools/destination/new.py", "@@", "-old", "+new",
            "*** End Patch"))
        run = stub((is_unittest, (1, "FAILED")))
        ctx = handle(self._patch({"command": patch}), self.root, {}, run)
        self.assertIsNotNone(ctx)
        self.assertEqual(len(run.calls), 5)
        for name in ("added", "mytool", "deleted", "moved", "destination"):
            self.assertIn(f"tools/{name}", ctx)

    def test_freeform_patch_resolves_paths_against_payload_cwd_and_runs_suite_once(self):
        patch = ("*** Begin Patch\n*** Update File: mytool.py\n@@\n-old\n+new\n"
                 "*** Add File: other.py\n+pass\n*** End Patch")
        run = stub((is_unittest, (1, "FAILED")))
        response = "Success. Updated the following files:\nM mytool.py\nA other.py"
        ctx = handle(self._patch(patch, response,
                                 os.path.join(self.root, "tools", "mytool")),
                     self.root, {}, run)
        self.assertIsNotNone(ctx)
        self.assertEqual(len(run.calls), 1)

    def test_patch_passing_suite_is_silent(self):
        run = stub((is_unittest, (0, "OK")))
        patch = "*** Begin Patch\n*** Delete File: tools/mytool/mytool.py\n*** End Patch"
        self.assertIsNone(handle(self._patch({"command": patch}), self.root, {}, run))
        self.assertEqual(len(run.calls), 1)

    def test_failed_or_unconfirmed_patch_never_runs_tests(self):
        patch = "*** Begin Patch\n*** Delete File: tools/mytool/mytool.py\n*** End Patch"
        responses = ({"exit_code": 1}, {"exit_code": 0, "is_error": True},
                     {"exit_code": 0, "isError": True}, {"interrupted": True},
                     "Failed to apply patch", {}, [], 0, {"exit_code": False},
                     {"exitCode": None})
        for response in responses:
            with self.subTest(response=response):
                run = stub()
                self.assertIsNone(handle(self._patch(patch, response),
                                         self.root, {}, run))
                self.assertEqual(run.calls, [])

    def test_patch_accepts_structured_exit_codes_and_success_output(self):
        patch = "*** Begin Patch\n*** Delete File: tools/mytool/mytool.py\n*** End Patch"
        success = "Success. Updated the following files:\nD tools/mytool/mytool.py"
        for response in ({"exit_code": 0}, {"exitCode": 0}, {"returncode": 0},
                         {"output": success}, {"stdout": success}):
            with self.subTest(response=response):
                run = stub((is_unittest, (1, "FAILED")))
                self.assertIsNotNone(handle(self._patch(patch, response),
                                            self.root, {}, run))
                self.assertEqual(len(run.calls), 1)

    def test_malformed_or_outside_patch_is_silent(self):
        for command in ({}, {"command": []}, [], 0, "not a patch",
                        "*** Begin Patch\n*** Delete File: ../../outside.py\n*** End Patch"):
            with self.subTest(command=command):
                run = stub()
                self.assertIsNone(handle(self._patch(command), self.root, {}, run))
                self.assertEqual(run.calls, [])


class PostEditDotnetTests(unittest.TestCase):
    def setUp(self):
        self.root = tempfile.mkdtemp(prefix="post-tool-")
        os.makedirs(os.path.join(self.root, "src", "Foo"))
        open(os.path.join(self.root, "src", "Foo", "Foo.csproj"), "w").close()
        open(os.path.join(self.root, "src", "Foo", "Thing.cs"), "w").close()
        os.makedirs(os.path.join(self.root, "tests", "Foo.Tests"))
        open(os.path.join(self.root, "tests", "Foo.Tests", "Foo.Tests.csproj"), "w").close()

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def _edit(self, rel):
        return {"tool_name": "Write",
                "tool_input": {"file_path": os.path.join(self.root, rel)}}

    def test_cs_edit_is_off_without_the_marker(self):
        run = stub((is_dotnet, (1, "Failed!")))
        ctx = handle(self._edit("src/Foo/Thing.cs"), self.root, {}, run)
        self.assertIsNone(ctx)
        self.assertEqual(run.calls, [])

    def test_cs_edit_with_marker_maps_to_test_project_and_surfaces_failure(self):
        run = stub((is_dotnet, (1, "X\nFailed!  - Failed: 1")))
        env = {"AUTOGIS_HOOK_DOTNET": "1"}
        ctx = handle(self._edit("src/Foo/Thing.cs"), self.root, env, run)
        self.assertIsNotNone(ctx)
        self.assertIn("Foo.Tests", ctx)
        self.assertIn("Failed", ctx)
        self.assertTrue(any(
            argv[0] == "dotnet"
            and "tests/Foo.Tests/Foo.Tests.csproj" in argv
            for argv in run.calls))

    def test_cs_edit_with_marker_passing_is_silent(self):
        run = stub((is_dotnet, (0, "Passed!")))
        env = {"AUTOGIS_HOOK_DOTNET": "1"}
        ctx = handle(self._edit("src/Foo/Thing.cs"), self.root, env, run)
        self.assertIsNone(ctx)

    def test_patch_cs_feedback_remains_marker_gated(self):
        payload = {"tool_name": "apply_patch", "cwd": self.root,
                   "tool_input": {"command": "*** Begin Patch\n"
                                  "*** Update File: src/Foo/Thing.cs\n@@\n"
                                  "-old\n+new\n*** End Patch"},
                   "tool_response": {"exit_code": 0}}
        run = stub((is_dotnet, (1, "Failed!")))
        self.assertIsNone(handle(payload, self.root, {}, run))
        self.assertEqual(run.calls, [])
        ctx = handle(payload, self.root, {"AUTOGIS_HOOK_DOTNET": "1"}, run)
        self.assertIn("Foo.Tests", ctx)
        self.assertEqual(len(run.calls), 1)


class PostPushTests(unittest.TestCase):
    def _bash(self, command, tool_response=None):
        payload = {"tool_name": "Bash", "tool_input": {"command": command}}
        if tool_response is not None:
            payload["tool_response"] = tool_response
        return payload

    def test_git_push_surfaces_pr_url(self):
        run = stub((is_gh_pr, (0, "https://github.com/o/r/pull/9\n")))
        ctx = handle(self._bash("git push -u origin my-branch"), "/root", {}, run)
        self.assertIsNotNone(ctx)
        self.assertIn("pull/9", ctx)

    def test_non_push_command_never_calls_gh(self):
        run = stub((is_gh_pr, (0, "url")))
        ctx = handle(self._bash("git status"), "/root", {}, run)
        self.assertIsNone(ctx)
        self.assertEqual(run.calls, [])

    def test_push_before_a_pr_exists_is_silent(self):
        run = stub((is_gh_pr, (1, "")))
        ctx = handle(self._bash("git push"), "/root", {}, run)
        self.assertIsNone(ctx)

    def test_push_is_the_subcommand_not_any_argument(self):
        self.assertFalse(_is_git_push("echo remember to git push later | cat"))
        self.assertFalse(_is_git_push("git commit -m push"))
        self.assertFalse(_is_git_push("git log --grep push"))
        self.assertFalse(_is_git_push("gitk push"))
        self.assertTrue(_is_git_push("git push"))
        self.assertTrue(_is_git_push("git status && git push"))
        self.assertTrue(_is_git_push("git -C repo push origin main"))
        self.assertTrue(_is_git_push("git -c user.name=x push"))

    def test_failed_push_is_not_reported(self):
        run = stub((is_gh_pr, (0, "https://github.com/o/r/pull/9")))
        rejected = "! [rejected]  main -> main (non-fast-forward)\nerror: failed to push"
        self.assertIsNone(
            handle(self._bash("git push", tool_response=rejected), "/root", {}, run))
        self.assertIsNone(
            handle(self._bash("git push", tool_response={"exit_code": 1}), "/root", {}, run))
        self.assertEqual(run.calls, [])

    def test_targets_the_pushed_branch_not_the_current(self):
        run = stub((is_gh_pr, (0, "https://github.com/o/r/pull/12")))
        handle(self._bash("git push origin HEAD:feature"), "/root", {}, run)
        self.assertTrue(any(argv[:3] == ["gh", "pr", "view"] and "feature" in argv
                            for argv in run.calls))

    def test_dash_C_cross_repo_declines(self):
        run = stub((is_gh_pr, (0, "https://github.com/o/r/pull/9")))
        ctx = handle(self._bash("git -C ../other push"), "/root", {}, run)
        self.assertIsNone(ctx)
        self.assertEqual(run.calls, [])

    def test_pushed_branch_parsing(self):
        self.assertEqual(_pushed_branch("git push"), (None, False))
        self.assertEqual(_pushed_branch("git push origin mybr"), ("mybr", False))
        self.assertEqual(_pushed_branch("git push origin HEAD:feature"), ("feature", False))
        self.assertEqual(_pushed_branch("git push -o ci.skip origin feat"), ("feat", False))
        self.assertEqual(_pushed_branch("git -C d push origin x")[1], True)


class RobustnessTests(unittest.TestCase):
    def test_broken_coordination_preserves_edit_and_patch_failure_feedback(self):
        with tempfile.TemporaryDirectory(prefix="post-tool-broken-") as root:
            hook_dir = os.path.join(root, "tools", "agent-hooks")
            coord_dir = os.path.join(root, "tools", "agent-coordination")
            os.makedirs(hook_dir)
            os.makedirs(os.path.join(coord_dir, "tests"))
            shutil.copyfile(os.path.join(TOOL_DIR, "post_tool_use.py"),
                            os.path.join(hook_dir, "post_tool_use.py"))
            script = """
import os, sys
sys.path.insert(0, sys.argv[1])
import post_tool_use
root = sys.argv[2]
payload = {"tool_name": "Edit", "tool_input": {"file_path":
    os.path.join(root, "tools", "agent-coordination", "coordination.py")}}
context = post_tool_use.handle(payload, root, {}, lambda argv: (1, "FAILED"))
assert "Tests for tools/agent-coordination FAILED" in context, context
post_tool_use._git_toplevel = lambda cwd: root
def run(argv, cwd):
    assert argv == ["python", "-m", "unittest", "discover", "-s",
                    "tools/agent-coordination/tests"], argv
    assert cwd == root, cwd
    return 1, "FAILED"
post_tool_use._real_run = run
sys.exit(post_tool_use.main())
"""
            patch = {"tool_name": "apply_patch", "cwd": root,
                     "tool_input": "*** Begin Patch\n"
                                   "*** Update File: tools/agent-coordination/coordination.py\n"
                                   "@@\n-old\n+broken\n"
                                   "*** End Patch",
                     "tool_response": {"exit_code": 0}}
            for source in ("def broken(:\n", "undefined_name\n",
                           "raise ImportError('broken')\n", "pass\n",
                           "raise SystemExit(7)\n"):
                with self.subTest(source=source):
                    with open(os.path.join(coord_dir, "coordination.py"), "w") as fh:
                        fh.write(source)
                    result = subprocess.run([sys.executable, "-c", script, hook_dir, root],
                                            input=json.dumps(patch), capture_output=True,
                                            text=True, check=False)
                    self.assertEqual(result.returncode, 0, result.stderr)
                    self.assertTrue(result.stdout)
                    output = json.loads(result.stdout)["hookSpecificOutput"]
                    self.assertEqual(output["hookEventName"], "PostToolUse")
                    self.assertIn("Tests for tools/agent-coordination FAILED",
                                  output["additionalContext"])
                    self.assertEqual(result.stderr, "")

    def test_unknown_tool_is_silent(self):
        run = stub()
        self.assertIsNone(handle({"tool_name": "Read", "tool_input": {}},
                                 "/root", {}, run))

    def test_missing_fields_do_not_raise(self):
        run = stub()
        self.assertIsNone(handle({}, "/root", {}, run))
        self.assertIsNone(handle({"tool_name": "Edit"}, "/root", {}, run))
        self.assertIsNone(handle({"tool_name": "Bash", "tool_input": {}},
                                 "/root", {}, run))


if __name__ == "__main__":
    unittest.main()

"""Self-check for the PostToolUse feedback hook.

The command runner is injected, so no test, dotnet, or gh runs for real. File
layouts that the logic inspects (a tool's tests/ dir, a src project and its
convention-mapped .Tests project) are built in a tempdir.
"""

import os
import shutil
import sys
import tempfile
import unittest

TOOL_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, TOOL_DIR)

import post_tool_use  # noqa: E402
from post_tool_use import handle, _is_git_push  # noqa: E402


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


class PostPushTests(unittest.TestCase):
    def _bash(self, command):
        return {"tool_name": "Bash", "tool_input": {"command": command}}

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

    def test_push_mentioned_in_an_echo_is_not_a_push(self):
        self.assertFalse(_is_git_push("echo remember to git push later | cat"))
        self.assertTrue(_is_git_push("git status && git push"))
        self.assertTrue(_is_git_push("git -C repo push origin main"))
        self.assertFalse(_is_git_push("gitk push"))


class RobustnessTests(unittest.TestCase):
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

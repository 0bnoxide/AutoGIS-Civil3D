"""Codex hook commands must survive whichever shell Codex launches them from.

Codex on Windows runs `commandWindows` through the session shell, which may be
PowerShell or cmd. A `$name` inside the double-quoted `-Command` string is
expanded by an outer PowerShell before the inner one sees it, so the hook
exits 1 with a parse error and Codex reports it as `Failed` (#153).
"""

import json
import os
import shutil
import subprocess
import sys
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
HOOKS = os.path.join(ROOT, ".codex", "hooks.json")


def windows_commands():
    with open(HOOKS, encoding="utf-8") as fh:
        config = json.load(fh)
    for event, groups in config["hooks"].items():
        for group in groups:
            for hook in group["hooks"]:
                yield event, hook["commandWindows"]


class TestCodexHooksConfig(unittest.TestCase):
    def test_windows_commands_have_no_shell_variables(self):
        for event, command in windows_commands():
            with self.subTest(event=event):
                self.assertNotIn("$", command)

    @unittest.skipUnless(sys.platform == "win32" and shutil.which("powershell"),
                         "needs Windows PowerShell")
    def test_pre_tool_use_runs_under_cmd_and_powershell(self):
        command = dict(windows_commands())["PreToolUse"]
        # cmd gets a raw command line, as Codex builds it (`/C "<command>"`).
        for outer in (f'cmd.exe /C "{command}"',
                      ["powershell", "-NoProfile", "-Command", command]):
            with self.subTest(shell=str(outer)[:12]):
                result = subprocess.run(
                    outer, input="{}", capture_output=True, text=True,
                    encoding="utf-8", cwd=ROOT, check=False)
                self.assertEqual(result.returncode, 0, result.stderr)


if __name__ == "__main__":
    unittest.main()

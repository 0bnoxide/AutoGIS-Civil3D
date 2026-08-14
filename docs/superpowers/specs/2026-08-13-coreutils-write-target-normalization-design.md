# Coreutils Write-Target Normalization — Design

**Status:** Approved 2026-08-13 (owner). Governs the fix for
[issue #81](https://github.com/0bnoxide/AutoGIS-Civil3D/issues/81) as a
maintenance hardening of the accepted repository-coordination foundation.

## Problem

`_argv_write_targets` in `tools/agent-coordination/coordination.py` selects
its POSIX/coreutils parser by exact comparison with raw `argv[0]`. The
matchers for `tee`, `sed -i`, `dd of=`, `truncate`, `rm`, `unlink`, `shred`,
`cp`, `mv`, and `install` therefore ignore command tokens with trailing dots
or spaces. Windows executable resolution can strip those suffixes before
launch, creating the same theoretical parser gap previously fixed for the
Git executable in issue #76.

When target extraction returns no path, the PreToolUse adapter cannot apply
the read-only-`main` rule. Deletion commands are the sharpest edge because
they have no commit-hook backstop.

## Decision

Normalize only the coreutils command token inside `_argv_write_targets`:

```python
cmd = argv[0].rstrip(". ")
```

Use `cmd` for the existing POSIX/coreutils comparisons and for the `mv`
source-removal branch. Keep PowerShell cmdlet matching based on the original
raw token:

```python
low = argv[0].lower()
```

The normalization is unconditional rather than OS-gated, matching the
deny-direction policy accepted for issue #76. A literal POSIX executable
whose name ends in a dot or space may be conservatively classified as its
unsuffixed command. That false-deny direction is preferable to allowing a
possible write or deletion on `main`.

## Boundary and invariants

The change stays inside `_argv_write_targets` and preserves these behaviors:

- Only trailing `.` and space characters are removed.
- Case sensitivity of coreutils command matching is unchanged.
- Path components are not stripped; pathed coreutils remain outside the
  matcher exactly as they are today.
- PowerShell cmdlet and alias matching remains case-insensitive and uses the
  raw token, so suffixes such as `del.` and `copy.` do not become aliases.
- Operand parsing and target ordering are unchanged.
- Redirect parsing, wrapper unwrapping, Git mutation checks, and claim logic
  are untouched.

Do not reuse `_git_executable_name`: that helper also strips path components
and lowercases the token, which would widen this fix beyond issue #81. Do not
introduce a new helper or dependency for a single local normalization.

## Test design

Add focused tests to
`tools/agent-coordination/tests/test_coordination.py`.

### Extraction matrix

A table-driven helper-level test exercises both `.` and trailing-space
suffixes for every affected command family:

| Command | Operands | Expected targets |
|---|---|---|
| `tee` | `seed.txt` | `seed.txt` |
| `sed` | `-i seed.txt` | `seed.txt` |
| `dd` | `of=seed.txt` | `seed.txt` |
| `truncate` | `seed.txt` | `seed.txt` |
| `rm` | `seed.txt` | `seed.txt` |
| `unlink` | `seed.txt` | `seed.txt` |
| `shred` | `seed.txt` | `seed.txt` |
| `cp` | `other.txt seed.txt` | `seed.txt` |
| `mv` | `other.txt seed.txt` | `seed.txt`, then `other.txt` |
| `install` | `other.txt seed.txt` | `seed.txt` |

Nested `subTest` contexts identify the command and suffix on failure.

### Preserved boundaries

Focused controls prove that the fix does not broaden adjacent semantics:

- An uppercase coreutils token remains unmatched.
- `/usr/bin/rm.` remains unmatched because path components are retained.
- Ordinary uppercase PowerShell aliases remain matched through the existing
  case-insensitive path.
- `del.` and `copy.` remain unmatched as PowerShell aliases.

### End-to-end denial

One command from each consequence class must reach the read-only-`main`
decision through `deny_reason_for_shell`:

- write: `tee. seed.txt`;
- delete: `rm. seed.txt`; and
- copy: `cp. other.txt seed.txt`.

The extraction matrix covers every matcher; these three cases prove the
extracted paths flow through the adapter's final denial decision.

## Verification

Use a red-green cycle for the focused tests, then run the complete
coordination suite:

```powershell
$env:PYTHONDONTWRITEBYTECODE = "1"
python -m unittest discover -s tools/agent-coordination/tests `
  -p "test_coordination.py" -k coreutils -v
python -m unittest discover -s tools/agent-coordination/tests
git diff --check
```

Before the implementation, the new positive suffix cases must fail because
no targets are extracted. After the one-line normalization is wired through
the existing comparisons, the focused tests and the full coordination suite
must pass without bytecode artifacts or unrelated changes.

## Risks and controls

- **False denial of a literal suffixed POSIX executable:** Accepted because
  it fails toward protection and matches the issue #76 precedent.
- **Accidental PowerShell widening:** Prevented by retaining raw-token
  PowerShell matching and explicit boundary tests.
- **Accidental path or case normalization:** Prevented by the local
  `rstrip(". ")` operation and boundary tests.
- **Partial matcher coverage:** Prevented by the full command/suffix matrix.

## Exclusions

- Lowercasing or basename normalization for coreutils executables.
- Runtime proof that a particular Windows shell launches every suffixed
  spelling.
- Expanding the set of recognized write commands or adding a full shell
  parser.
- Changes to PowerShell alias definitions or Git executable handling.

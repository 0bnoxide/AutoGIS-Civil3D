# Checkout and worktree procedure

The step-by-step session lifecycle. Policy lives in
[docs/agent-guide.md](agent-guide.md); this file is the how.

Every command below is run from the repository unless noted.
`COORD` means `python tools/agent-coordination/coordination.py`.

## Starting a work item

1. Read [CLAUDE.md](../CLAUDE.md) or [AGENTS.md](../AGENTS.md), the
   [agent guide](agent-guide.md), the open roadmap gate in
   [docs/roadmap.md](roadmap.md), and open GitHub issues and PRs.
2. `COORD doctor` — read the findings; do not proceed past a corrupt
   registry or missing hooks without fixing them (`COORD init` installs
   hooks and creates state).
3. `COORD status` — inspect live claims before choosing a slice.
4. Synchronize the primary checkout: `COORD sync-main` (fast-forward only,
   clean tree required).
5. Create your branch and linked worktree, named for your agent and slice:

   ```
   git worktree add .worktrees/<agent>+<slug> -b <agent>/<slug> origin/main
   ```

6. Claim it (session id: a stable identifier for your session):

   ```
   COORD claim --session <id> --kind branch --value <agent>/<slug>
   COORD claim --session <id> --kind worktree --value .worktrees/<agent>+<slug>
   ```

   A rejection names the holder. Pick another slice or coordinate through
   the owner; never force-release another session's claim yourself.

## While working

7. `COORD check --session <id>` before write-producing operations confirms
   you are on your claimed branch and outside `main`. The harness adapters
   run the same rule automatically.
8. Work only within your claimed scope. Validate with the project's checks
   before pushing: the .NET suite (`dotnet test -c Release`), the Python
   tool tests (`python -m unittest discover -s tools/<tool>/tests`), and
   `python tools/checks/docs_checks.py` when you touched `docs/`.

## Finishing

9. Push the branch and open a PR; review follows
   [CONTRIBUTING.md](../CONTRIBUTING.md). GitHub is authoritative for status
   from this point.
10. After merge or abandonment: release your claims
    (`COORD release --id <claim-id>`) and remove the worktree:

    ```
    git worktree remove .worktrees/<agent>+<slug>
    ```

    Cleanup removes only the named worktree — never a computed or broad
    path. Claims stay live until you release them; nothing expires them.

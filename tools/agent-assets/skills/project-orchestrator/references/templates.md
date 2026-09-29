# Orchestration templates

Adapt these to the approved project; copy only missing pieces. Values shown as examples are not real assignments or evidence. Resolve scope and task gates before dispatch. Reuse existing files rather than adding parallel authorities. No Git, model provider, scanner, native-goal API or scheduler is required merely to use the templates.

## 1. Supervisor brief: ORCHESTRATOR.md

```markdown
# Project supervisor

Authoritative specification: PIPELINE.md and the referenced task briefs.
Observed execution state: PROJECT_STATE.json. Supervisor alone writes it.
Workers own only their assigned paths. Independent verifiers do not implement.

Read these controls and the current task before acting. Reconcile actual
workers and goals with saved state. Never dispatch a second writer because
a session is absent from one API. Require confirmed release before takeover.

Completion markers trigger review, not acceptance. Bind each handoff to its
task/attempt, freeze relevant evidence, independently verify all gates, and
rehash reviewed bytes before supervisor acceptance. Preserve prior attempts.
Assign repairs only for demonstrated deficiencies; keep accepted unrelated
work closed. Advance only accepted dependencies within the approved scope.

Project-specific allowed/forbidden actions, model pins, fallback authority,
concurrency, budgets and external prerequisites live in the approved pipeline.
An instruction to persist does not authorize new spending, publication,
credential changes, permission bypasses or unassigned production actions.
```

## 2. Pipeline: PIPELINE.md

Record the objective, in-scope outcome, explicit non-goals, execution permissions, external prerequisites, and final integration gate. Keep user approval evidence or its reference. Do not turn a plan-only request into execution authority.

| Task ID | Task brief | Dependencies | Worker / effort | Independent verifier / effort | Acceptance gate |
|---|---|---|---|---|---|
| T1 | tasks/T1.md | none | resolve from user choice or configured runtime default | separate agent/session; resolve before launch | Task T1's explicit criteria |

Set `max_concurrent_writers` according to the project, starting sequentially if unknown. State which fallbacks, retries and costs are authorized. Include only explicit budgets; absence is not zero and not permission for new spending. If the user requests continuous goals, record that authority and inspect runtime support. Later phases may stay unassigned until upstream evidence makes their specifications actionable.

## 3. State: PROJECT_STATE.json

This parseable starter is an example, not a claim of approval or execution. Replace objective/scope with the user's actual request, and extend tasks only when concrete. `null` means unknown or unset; statuses are observations, not native-tool commands.

```json
{
  "schema_version": 1,
  "project": "Example project",
  "objective": "Deliver the approved bounded outcome",
  "authorized_scope": [],
  "deferred_scope": [],
  "authorization_reference": null,
  "status": "planning",
  "revision": 0,
  "current_task_ids": [],
  "max_concurrent_writers": 1,
  "forbidden_actions": [],
  "budgets": {"tokens": null, "cost": null, "deadline": null},
  "supervisor_goal": {"explicitly_requested": false, "id": null, "observed_status": null},
  "tasks": {
    "T1": {
      "brief": "tasks/T1.md",
      "dependencies": [],
      "owned_paths": [],
      "status": "not_started",
      "current_attempt_id": null,
      "owner": {"agent_id": null, "session_id": null, "ownership_state": "unassigned"},
      "assignment": {"model": null, "effort": null, "selection_source": null},
      "observed_runtime": {"model": null, "effort": null, "source": null},
      "worker_goal": {"id": null, "observed_status": null},
      "handoff": {"path": null, "consumed_attempt_id": null},
      "submission": null,
      "verification": null,
      "acceptance": null,
      "blocker": null,
      "next_action": "Resolve this task's specification and dispatch prerequisites"
    }
  },
  "next_action": "Reconcile scope and current ownership before dispatch"
}
```

Useful task states: `not_started`, `running`, `awaiting_handoff`, `ready_for_review`, `in_review`, `needs_repair`, `accepted`, `blocked`, `cancelled`. Keep an unresolved owner explicitly `ownership_unconfirmed`, regardless of whether a query returns not-found. Record who confirmed release, when, and for which attempt before replacement.

Store attempt history outside this concise current-state file. Increment its revision on real transitions, not unchanged polling. Keep historical decisions immutable; use a new superseding record for changed acceptance or scope.

## 4. Worker task and dispatch

Each task brief contains: objective and stop boundary; accepted prerequisite references; owned and protected paths; required outputs; numbered acceptance criteria with commands or inspection evidence; relevant permissions; handoff destination and task/attempt IDs. Distinguish synthetic expectations from required real observations. A delegated worker may not change its own acceptance criteria or certify completion of the parent project.

Use this short dispatch shape when the brief already contains the details:

```text
Implement T1, attempt run-001, using tasks/T1.md.
Read project controls and the accepted dependency references in the brief.
Own only the assigned paths. Preserve other writers and accepted evidence.
Use the recorded model/effort assignment and applicable project restrictions.
If explicitly requested and supported, activate the bounded worker goal and
report its ID; otherwise report the actual execution mechanism.
Save your report, manifest and attempt-specific handoff last, then stop edits.
Report blockers to the supervisor. Do not self-accept, launch another task,
repair an upstream producer or delegate helpers without that assignment.
```

Save requested model/effort, actual observable values, identifiers, start/end times, result, command/evidence paths, unresolved issues and next action in one attempt record. When actual settings are unavailable, record unknown instead of inferring them from the requested values.

## 5. Worker report, manifest and completion marker

Worker report: criterion-to-evidence table, actual commands/exits, failures and test-first evidence where applicable, files changed, limitations, runtime observations, and stopped-editing confirmation. Missing or unrun evidence is explicit.

The supervisor chooses unique attempt/submission identifiers. A handoff might contain:

```json
{
  "task_id": "T1",
  "attempt_id": "run-001",
  "status": "ready_for_verification",
  "worker_stopped_editing": true,
  "report": "logs/T1/run-001/worker-report.md",
  "manifest": "logs/T1/run-001/manifest.json"
}
```

Only the actual worker writes that release after all outputs are saved. Add the actual timestamp and session identity; never copy this example as real evidence. A stale attempt or a user report of continued editing prevents release, even if the marker says true.

Manifest contract: `task_id`, `attempt_id`, `artifacts` containing workspace-relative `path`, `size_bytes`, `sha256`, plus explicit exclusions. Include authored deliverables and raw evidence required to reproduce the gates. Exclude mutable build caches and unrelated history. Validate inventory completeness, normalized path containment, duplicates, reparse/symlink escapes and actual sizes/hashes without exposing credentials. Resolve conflicting writers before taking the snapshot.

The supervisor creates a new submission record binding the actual task/attempt, artifact manifest hash, requirements version/hash, dependency submission IDs and confirmed ownership release. Include worker report and handoff in the frozen set; exclude self-hashing records themselves. Use available standard hash/file tools rather than inventing a hashing framework. A snapshot hash alone is not evidence that the required behavior occurred.

## 6. Independent verification and acceptance

```text
Independently verify T1 submission-001 against tasks/T1.md and its frozen
dependencies. You did not implement or repair this submission. Read the
actual source/artifacts, not just the worker summary. Validate snapshot
integrity and every numbered gate. Run safe proportional checks in new
owned scratch paths; retain raw evidence. Do not repair source or mutate
accepted evidence, supervisor state or the submitted files.
Write the assigned review report with separate specification and quality
verdicts; return PASS, FAIL or BLOCKED with findings and concrete evidence.
Hash-check reviewed files after testing, report gaps and stop editing.
```

Required review fields: task/attempt/submission identity; reviewer identity; independence; requirements and manifest digests; each gate's verdict/evidence/qualifications; commands and actual results; pre/post integrity results; material findings; residual risks. PASS means all required gates passed for this scope, not merely exit zero or no reported errors. Reviewers may reuse valid unchanged upstream acceptance with its qualifications.

Supervisor acceptance is a separate record containing:

- Exact accepted scope and excluded/deferred scope.
- Frozen submission and independent review identities/digests.
- Each required gate's disposition and material findings resolved.
- Final supervisor integrity check against reviewed bytes.
- Sufficient fresh supervisor verification for the risk; do not blindly rerun every historical suite.
- Accepted dependencies, inherited qualifications, remaining risks and next action.

Advance only after this record exists. If a required result is blocked/unrun, record it as such; either repair the gap or obtain explicit scope revision. Do not relabel unknown behavior as PASS.

## 7. Focused repair and recovery

Repair brief: failing submission/review, exact unresolved finding IDs with reproductions, assigned owner/paths, expected evidence to close each finding, preserved baselines, and new attempt/handoff ID. Send it to the original owner when available. For cross-owner defects, reopen the producer's affected acceptance, repair/reverify/reaccept it, then rerun affected dependents. Do not silently mutate upstream artifacts or restart the entire pipeline.

Recovery checkpoint: last confirmed attempt, live-status observation and source, unresolved ownership, saved artifact paths, current blockers, authorized retry/fallback policy and next safe action. `Not found`, silence and elapsed time are not ownership release. If quiescence cannot be confirmed, wait for or request release; do not dispatch a replacement to the same paths. An explicitly authorized isolated branch is a separate decision, not an assumed escape hatch.

Use the available runtime's goal/wait tools according to their actual descriptions. Create goals only on explicit request. If unavailable, record that limitation and provide an ordinary/manual handoff; never fake activation, schedule monitoring by prose alone, or promise persistence across closure. Goal completion follows acceptance of the authorized scope, not a worker handoff. Goal pause/block states must follow the runtime's rules and user direction.

## 8. Completion message

Report accepted outcome; strongest compact verification evidence; unresolved or deferred work; next authorized action; links to state/acceptance. Report actual usage if required by the goal runtime. Keep separate the finished task, authorized tranche and whole project. Do not launch the next larger scope merely because a task graph contains it.

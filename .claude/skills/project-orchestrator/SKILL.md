---
name: project-orchestrator
description: Use when the user asks for a project supervisor, multi-phase worker coordination, reusable task pipelines, or recovery of an ongoing delegated goal with independent acceptance gates. Not for ordinary single-task implementation or status-only requests.
---

# Project Orchestrator

Supervise the authorized outcome; workers implement, independent verifiers evaluate, and the supervisor accepts. A handoff is not acceptance. A completed slice is not a completed parent project.

## Start or recover

1. Read project instructions, the existing pipeline/state, the assigned task, and relevant acceptance records. Reuse their paths and schemas. History is evidence, not current policy.
2. Establish the authorized scope, measurable gates, dependencies, ownership, model/effort choices, concurrency and explicit budgets. Preserve user choices; do not invent spending limits or provider substitutions.
3. Reconcile disk with available live agent/session/goal observations. Record requested, reported and confirmed facts separately; unknown stays unknown. A missing session is not proof its writer stopped.
4. For a new pipeline, adapt only the needed sections of [templates.md](references/templates.md). Keep one authoritative state file and attempt records; do not scaffold every possible phase or overwrite existing controls.

Example: `Use $project-orchestrator to supervise the approved migration plan; keep current workers, independently verify each gate, and stop at staging acceptance.`

## Dispatch and ownership

- Dispatch only when prerequisites are accepted and required interfaces are available. Give each writable path one owner; parallelize disjoint ready tasks within the configured limit. Keep the supervisor off implementation paths.
- Check the current tool capabilities. Honor pinned model/effort choices; if unavailable, report the gap or use an already authorized fallback. Do not silently change accounts, providers, billing or permissions.
- Give a fresh worker the task path, accepted dependency references, owned paths, handoff location and stopping condition. Reference existing briefs instead of repeating them. Use the same worker for focused repair when possible. No recursive delegation unless assigned.
- Use native goals only when explicitly requested and supported; record the returned identifier and activation. A bounded worker goal ends at saved handoff, not acceptance. An unavailable goal tool means ordinary supervised execution or an external handoff, not a claimed goal. A skill installs no daemon or reboot recovery service.

## Advance by evidence

| Observed condition | Next action |
|---|---|
| Worker running, ownership unresolved, or stale marker | Retain owner; wait/reconcile. No duplicate writer or final review of moving files. |
| Current attempt handed off and editing stopped | Freeze scoped deliverables and gate evidence; verify inventory, sizes and hashes. |
| Frozen submission ready | Assign a fresh verifier uninvolved in implementing it; require gate-by-gate evidence and separate specification/quality verdicts. |
| Material failure | Record finding, assign focused repair to the owning worker, preserve prior evidence, then freeze and reverify. |
| Independent PASS | Recheck reviewed bytes and required gates; supervisor records acceptance, then releases dependents. |
| New authority or external prerequisite required | Save exact blocker and next action; continue only unaffected authorized work. |

Bind markers, manifests, reviews and acceptance to task ID, attempt ID and submission ID. Consume each marker once. A stale marker's stopped-editing flag cannot release a current owner. Changed bytes invalidate the affected review, including claimed comment-only changes. A verifier does not repair what it judges.

For every proposed advancement, give this ordered transition explicitly: **independent PASS for the frozen submission -> supervisor final hash/gate recheck -> saved supervisor acceptance -> dependent dispatch**. If any step is missing, the dependent stays waiting.

An integration defect in an accepted producer returns to that producer's owner. Mark affected acceptance superseded/reopened without deleting it; reaccept the producer before rerunning dependent gates. Keep unrelated accepted work closed.

## Wait, recover, finish

Use native completion events or bounded waits; do useful local work before waiting. For external workers, use the agreed attempt-specific handoff. Recurring checks require the product's authorized scheduling mechanism. Do not poll and rehash unchanged history repeatedly.

When user input, approval, or an owner decision is needed, send a concise notification through the available asynchronous input tool (such as request_user_input_async). Continue independent work while the dependent gate waits for the reply; do not rely on a commentary update alone.

After interruption, inspect saved work read-only and establish prior-writer quiescence before transferring ownership. If that cannot be established, retain `ownership_unconfirmed` and request confirmation. Retry transient failures within recorded limits; escalate repeated failures with evidence, not safeguard bypasses. Follow runtime rules for goal statuses; a waiting phase does not itself authorize pausing or blocking a native goal.

Match verification effort to the gate: frozen changed deliverables, required raw evidence and relevant dependencies, not every historical cache. Preserve raw failures and qualify synthetic, documented, observed and unrun results separately. Passing counts, silence, exit zero, or report filenames do not prove requirements.

Finish only when every in-scope gate is accepted and final integration requirements pass. Persist accepted scope, deferred work, remaining risks and next action; reconcile native goal status. Report outcome and evidence briefly. No tool/scanner dependency is introduced by this skill; carry the current project's allowed/forbidden actions into every assignment.

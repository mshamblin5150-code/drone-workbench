# Ticket 7: combined Codex and Claude assessment

Date: 2026-11-12. Both bounded experiments are published. This assessment
compares their reports and receipts; it does not rerun or independently reproduce
either experiment.

Follow-up: [Codex connection diagnosis](codex-connection-diagnosis.md) resolved
the isolated Unix-listener handshake with a .NET WebSocket client. Two sequential
connections read empty snapshots from the same owned server. Existing desktop
owner access remains unverified; active-work reconnect/recovery remains untested.

**Verdict: useful adapter feasibility evidence, incomplete required coverage.**
Keep [ticket 7](https://github.com/mshamblin5150-code/drone-workbench/issues/7)
open. Preserve the confirmed PC-wide observation goal and enable controls only
where ownership, authority and a supported route have been verified. A missing
control route does not require abandoning observation of that source.

## Evidence by surface

| Surface | Codex | Claude |
| --- | --- | --- |
| Existing desktop sessions | Standalone external observation/control connection unverified | Agent-view observation demonstrated; external control unverified |
| Externally launched native interactive CLI | Not demonstrated | No separate synthetic interactive run; desktop sessions appeared as interactive |
| Newly owned test workload | Isolated app-server stdio handshake and model-free command execution demonstrated | Model-free background sessions observed and controlled through CLI |
| Correct versus wrong target | Wrong command target rejected, correct command survived until targeted termination | Wrong background-session target rejected, correct session survived until targeted stop |
| Acceptance versus actual exit | Separate terminate response, terminal exec response and Windows process-handle exit | Separate stop response and session/PTY-host process-handle exits |
| Repeated control | Terminate after exit rejected | Stop after stop succeeded again; removal after removal rejected |
| Crash/restart evidence | Not exercised with active work | Synthetic background process restarted under the same short ID with a new PID; state stale during the gap |
| Lifecycle hooks | Not exercised | Prospective per-session hooks fired on start/clean stop; external kill produced no SessionEnd |
| Model turns, subagents, busy tools | Not exercised | Not exercised |

The Claude report uses "process tree" for its idle fixture. Its receipt establishes
exit of the session process and PTY host. Treat that as those observed processes,
not a general guarantee that every tool descendant exits under load. The report
itself lists busy-session descendant cleanup as untested. Codex's fixture proves
only the exact observed sleeper exited.

## Consequences for the shared contract

1. **Publish capabilities per source instance and target kind.** Claude background
   stop and Codex owned-command termination are different operations. Neither
   grants desktop-session interruption, pause, resume or unrestricted commands.
2. **Separate source identity from process incarnation.** Keep full source session
   identity and parent relationships when available; maintain short IDs only as
   verified source-local control references. Codex command IDs are connection
   scoped. A PID alone is insufficient, and replacements invalidate stale control
   bindings until reconciled.
3. **Keep intent, receipt and outcome separate.** An idempotent success may mean
   "already stopped". A missing response may hide an accepted effect. Do not
   promise universal exactly-once control from either provider's tested behavior.
4. **Combine observations without erasing uncertainty.** Claude missing PID/status
   and stale state during a restart must not become "completed" or "idle".
   Missing SessionEnd does not establish liveness. A new SessionStart may reflect
   restart rather than a new human job. Maintain observation/receipt timestamps,
   source reach, process evidence and a reason for unknown/stale status.
5. **Use supported stop rather than arbitrary process kill.** The Claude fixture
   demonstrated automatic restart after an external kill. A process kill can
   defeat the intended stop outcome; it must not be a substitute control route.
6. **Filter at the boundary.** Both sources can expose private paths/content.
   Claude hook fields include transcript paths and session titles; Codex
   initialization returns home information. Schema presence never authorizes
   persistence or display. Apply allowlists before storage, logging and indexing.

These are implementation implications within the existing architecture and
authority policy, not activation of new controls or global hooks.

## Remaining evidence, in order

1. **Codex existing-owner observation:** the isolated Unix proxy handshake is now
   diagnosed and corrected; identify a supported connection to the actual
   desktop/CLI owner. The observed desktop server uses the documented stdio
   default and exposes no discovered external endpoint. Do not
   inspect private databases, bootstrap a replacement daemon or substitute another
   server's stored history. If no supported route is available, return that
   concrete coverage limitation to the owner before narrowing the product scope.
2. **Independent entrypoints:** synthetic native interactive sessions, multiple
   projects and externally started work. Distinguish source kind from actual
   launch path; neither report establishes all PC/user/configuration contexts.
3. **Active-work control:** synthetic model turns/subagents and tools, exact-target
   controls where supported, busy-session interruption/stop, and separate process
   descendant outcomes. Use only disposable workload identities and preserve real
   work. Credential or paid API setup is not authorized by this assessment.
4. **Interrupted observation/control:** dropped connection or final event, lost
   acknowledgement, source restart, fresh snapshots and stale target rejection.
   Claude one-shot re-query is useful evidence; it does not prove Workbench's
   durable state/recovery implementation.
5. **Workbench acceptance:** prove privacy filtering, control authorization and
   source capabilities with the actual host/adapter implementation. No production
   adapter or dashboard exists in these experiments.

The next Codex work is the existing-owner connection gap. The next Claude work is
independent interactive/background coverage and, where a supported route exists,
busy synthetic work and descendant outcomes. Further tests require bounded
experiments; do not treat this list as permission to control real sessions.

## Source reports

- [Codex results](codex-ticket7-results.md) and [receipt](codex-ticket7-receipt.json),
  published in commit `ef5c769`.
- [Claude results](claude-ticket7-results.md) and [receipt](claude-ticket7-receipt.json),
  published in commit `320b2e2`.
- [Acceptance matrix](../plan/acceptance-matrix.md) and
  [confirmed build plan](../plan/build-plan.md).

No source reports or receipts were rewritten by this reconciliation. Both local
session self-descriptions remain untracked and unpublished.

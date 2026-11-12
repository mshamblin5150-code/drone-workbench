# Drone Workbench build plan

Status: proposed synthesis for shared-understanding confirmation. This is a
decision-backed implementation route, not implemented software or proof that
all external integrations work. The canonical [Wayfinder map](https://github.com/mshamblin5150-code/drone-workbench/issues/1)
and its resolution comments own the human decisions.

## Outcome and scope

The first release provides PC-wide Windows observation across projects and the
user's Codex and Claude entrypoints, including work started outside Workbench.
It combines a drone CLI, user-sign-in background collection, and a live local
Blazor dashboard. It provides verified workload controls, file/worktree inventory,
cleanup proposals and approve-and-run execution under the ratified preservation
rules. Unknown ownership and source gaps stay visible with controls unavailable.

First release includes intentionally published structured summaries and 30 days
of searchable ordinary history. It does not ingest full prompts/transcripts or
mine private files for summaries. Required cleanup evidence, preserved material
and recovery copies are separately retained.

Later phases contain the shared heavy-work queue, environment/model profiles,
capacity benchmarking as a user feature, and second-machine workers. Internal
bounded execution of collector operations is not that deferred workload queue.

## Chosen implementation shape

Use a modular .NET application with one authoritative per-user background host.
The host owns collection, commands, local state, cleanup runs and the loopback
Blazor endpoint. A thin CLI contacts that host. A small supervisor owns desired
running/stopped state and bounded crash recovery. The browser owns neither
collection nor accepted operations.

```text
Sign-in entry -> lifecycle supervisor -> Workbench host
                                      |-> observation adapters
Drone CLI -> local user-scoped channel |-> authoritative domain modules
Local Blazor dashboard ---------------|-> local state + bounded live views
                                      |-> verified control/cleanup adapters
```

Start with `Workbench.Core`, `Workbench.Host`, `Workbench.Cli`, and
`Workbench.Supervisor`, plus interface-focused tests. Keep provider and storage
implementation details private. Add a separate worker process only when a
measured native failure, hang or resource problem justifies isolation.

One host owns Workbench domain state and the local transactional SQLite writer.
The supervisor alone owns its small durable desired-state/retry record, since it
must survive the host's absence. Neither CLI nor dashboard writes database tables
or lifecycle files directly.

Runtime state belongs under a user-local application-data location outside the
source checkout and scanned target roots. Keep private per-project path mappings
within the permitted project context. Global views use safe labels/counts where
project rules restrict filenames or paths. Exclude Workbench's state, recovery
roots and planning caches from automatic candidate discovery.

See [architecture rationale](architecture-rationale.md) for the alternatives,
module interfaces and accepted trade-offs.

## Human and drone interfaces

Illustrative CLI contract, to implement and test rather than treat as existing:

```text
drone-workbench status --json
drone-workbench sources --json
drone-workbench report --stdin
drone-workbench inspect --selection <id> --json
drone-workbench proposal show <id> --json
drone-workbench operation show <id> --json
drone-workbench lifecycle status --json
```

Use structured standard input for summaries rather than putting text into command
arguments. Derive caller permissions on the host. Published summaries are scoped
claims, not proof of exclusive ownership. A publisher cannot invent a verified
source identity or grant itself control/cleanup authority.

The CLI can request inspection and observe proposals; it has no cleanup-approval
verb. The authenticated local dashboard presents exact targets, preservation,
coverage gaps and recovery terms before Approve and run. The host checks the
dashboard action independently of whether a button was enabled.

Dashboard views:

1. Overview: machine resources, activity counts, active operations and blockers.
2. Work: group by project/source; show summaries, source state, freshness and
   verified controls. Count sessions, subagents, jobs and processes separately.
3. Coverage: every configured/discovered source, its scope, version, last refresh,
   gaps and capability evidence; reconcile unmatched processes and source work.
4. Hygiene: scan coverage, artifact classes, logical/allocated measurements,
   eligibility findings and exact proposals.
5. Executions and recovery: per-target outcomes, preservation evidence, recovery
   availability and expiry-review items.
6. Lifecycle: running/stopped intent, retry exhaustion, explicit stop/resume and
   measured resource settings.

Bind web listeners to loopback, with local authentication, permitted Host/Origin
checks and anti-forgery protection for mutations. The CLI channel is user-scoped
and explicitly local-only. Drone reporting/control credentials cannot invoke the
dashboard approval route; client-supplied `human` flags confer no privilege.
This enforces the intended application workflow, not an isolation boundary against
arbitrary code already holding all of the Windows user's authority. Windows Hello,
biometric checks and new OS security configuration are not added requirements.

## Observation, identity and live state

Normalize external metadata inside source adapters before persistence, indexing,
logging or browser delivery. Do not persist raw envelopes, command lines,
environment values, arbitrary errors, rejected summaries or diagnostic dumps of
source objects. Admit summaries through an intentionally narrow publication
schema and the owning project's disclosure rules; unrestricted text cannot be
declared safe merely because a filter ran.

Use source-instance IDs and native object identities, with process creation
identity as well as PID. Preserve evidence for associations and shared consumers.
Late messages from an old source generation must not revive replaced work.
Distinguish idle, active, waiting, not-loaded, unknown, stale, unsupported and
unavailable; a disconnected source does not mark its jobs complete.

Commit state and its revision together before notifying views. Live updates carry
small safe change hints; clients refresh from authoritative views. Slow clients
cannot block collection. A gapped/expired cursor requires a new snapshot rather
than indefinite event retention. Browser closure disposes subscriptions only.

## Source capability matrix and first feasibility gate

| Source | Existing evidence | Required evidence before claiming support |
| --- | --- | --- |
| Windows processes/resources | Documented metadata APIs | Scope visible to current account; denied fields; reused PIDs; detached/shared work; unavailable counters |
| Claude CLI status | Installed version/help and one filtered live JSON query succeeded | Synthetic externally started interactive/background work, configuration-root coverage and source-native stop outcomes |
| Codex existing live owner | Installed help advertises app-server/proxy tooling; the narrow daemon version query did not establish a running owner | Supported Windows endpoint discovery/connection, live versus stored work, subagents and exact turn interruption |
| Structured publications | Product requirement and proposed schema | Publisher scope, safe admission, canary rejection and freshness/association behavior |
| Special execution surfaces | Windows may show their host processes | Identify WSL/container/remote/other-user requirements from actual use; do not pretend host-process visibility is internal work coverage |

The chat's supplied app tools are not proof of an external interface callable by
a standalone C# application. Starting another Codex server and reading stored
history is not proof of observing the existing desktop owner.

The first implementation milestone is a bounded synthetic feasibility experiment.
Use disposable source/job identities and synthetic material only; exact test
controls apply only to those identities. No global hooks, extra privileges,
new listeners or configuration changes are a fallback without an explicit ruling.
If required Codex/Claude reach is unsupported, record the concrete limitation and
return for a scope/integration decision. Do not silently call a process-only
fallback complete first-release coverage.

## Verified controls

The Work Control module rechecks source identity, freshness, ownership, supported
action and caller scope immediately before dispatch. Read-only OS visibility
does not enable general process termination. Shared services remain separately
owned and are not stopped when one associated job ends.

Track request accepted, source acknowledged, confirmed turn/job outcome, and
verified process exit separately. An interrupted turn does not prove every
detached tool ended. An ambiguous delivery becomes reconciliation/unknown rather
than a blind retry of a non-idempotent control.

## Cleanup and recovery contract

Eligible classes are proven regenerable temporary/cache material and completed,
linked non-primary Git worktrees. Primary checkouts, the source repository and
preservation/state roots are protected. Unsupported worktree variants or uncertain
active use remain unresolved rather than eligible by age or clean Git status.

The Hygiene module owns the entire workflow:

1. Inspect target identities, link/containment behavior, coverage, current use,
   locks, changes, unique commits, ignored/untracked material, deliverables,
   required evidence and project rules. Record unread scope and regeneration proof.
2. Produce an immutable exact proposal: selected targets/actions, evidence
   revisions, preservation set and recovery terms. No substituted targets.
3. The dashboard's Approve and run action consumes authority for that proposal/run.
   Freshly recheck target and policy facts; a material change requires fresh review.
4. Preserve unique/required bytes and sufficient Git objects/reconstruction data.
   A manifest or reference to objects that may disappear is not a verified backup.
   Verify preservation before mutation; uncertain restoration blocks the candidate.
5. Journal intended external actions and observed outcomes. Use Git-aware worktree
   operations and tested platform identity/containment handling. A database
   transaction cannot make filesystem/Git mutations atomic.
6. Record each target's completed, held, failed or uncertain result. A crash after
   a possible side effect requires reconciliation before further destruction;
   it never authorizes a blanket replay.
7. Prove restoration in a non-overwriting synthetic drill. Recovery refuses
   collisions and reports partial outcomes without discarding the source copy.

Accepting a run persists its identity, proposal binding and approval consumption
before acknowledging it. A lost response is resolved by querying or retrying the
same request identity, which returns that run rather than starting another one.
Browser/request cancellation can stop waiting; it cannot cancel a durably accepted
cleanup. Subsequent cancellation is a separate supported operation with its own
safe-checkpoint and outcome rules.

Ordinary history expires automatically after 30 days. Cleanup approval/execution
evidence, preserved files and recovery material use separate data classes and
indexes. Recovery copies are held at least 30 days, then become eligible for
expiry review; they are not deleted automatically. Longer owning-project rules
take precedence. Releasing recovery material is a new specific review/action,
not authority inherited from the original cleanup.

History expiry includes search entries, ordinary change/outbox payloads, caches
and backup design. Queries enforce the time window even while maintenance is
stopped, then physical maintenance catches up on resume. Do not retain an immortal
event log or reset a summary's lifetime merely by rebuilding a view. Document
actual storage deletion behavior; no forensic-erasure guarantee is claimed.

## Lifecycle and responsiveness

Use a per-user sign-in startup entry for the supervisor. It launches at most one
host, honors durable stopped-by-user intent, retries unexpected exits with
increasing bounded delays and records exhaustion. Explicit resume clears the
stop intent. Installation, sign-in and recovery all read the same desired state.

The supervisor retains a small user-scoped local status/stop/resume interface
while the host is absent. Operator lifecycle commands contact it directly; drone
reporting credentials do not gain resume authority. If stopping the host removes
the Blazor endpoint, the open browser shows disconnected/stale state and the
operator recovery command, not an apparently live Resume button. An operator can
resume through that surviving local interface and the browser reconnects.

Persist stop intent and halt new collection/scans before acknowledging stop.
For an accepted cleanup already in progress, fence new actions and wind down to
a verified safe checkpoint; report Stop pending if that is not complete. A crash
or ambiguous external action is reconciled on the next explicit resume. Stopping
the collector never implies that cleanup completed or grants blind destructive
continuation on restart.

Begin with a Task Scheduler sign-in entry plus a small supervisor as the proposed
Windows mechanism; verify its exact per-user and restart behavior before adoption.
No startup entry has been installed during planning.

Keep lightweight source observation running; expensive scans are bounded,
resumable and yield under measured pressure. Establish CPU, memory, disk,
sampling, history-size and retry limits using idle/busy/unavailable-source
experiments. Numeric values remain measured acceptance outputs, not invented
planning constants. A material experience or coverage trade-off returns to the
human; routine measured settings belong to engineering work.

## Implementation sequence

| Slice | Reviewable outcome | Exit evidence |
| --- | --- | --- |
| S0: feasibility and reproducibility | Supported source/surface matrix; verified build/runtime baseline and project-scoped engineering adapters | Existing-owner attachment/control experiment; exact SDK and locked transitive restore; discovery/readback in intended harnesses; no unsupported coverage claim |
| S1: one read-only vertical slice | Host, CLI and Blazor share synthetic state and then verified metadata | One owner/writer; authenticated routes; source gaps; browser independence; no private canary in storage/logs/UI |
| S2: observation and bounded history | PC-wide source views, permitted publications, searchable ordinary history | External work and identities; shared/unmatched activity; stale states; 30-day expiry independent of cleanup data |
| S3: inventory and proposal review | Resumable file/worktree scans and exact preservation proposals | Links/denied/partial scope; active/locked/dirty/unique/ignored cases; project rule adapter; no mutation |
| S4: verified actions and recovery | Supported job controls and approve-and-run cleanup | Exact authority binding; changed targets held; preservation failures block mutation; crash reconciliation; real synthetic restore drill |
| S5: lifecycle, packaging and release | User-sign-in operation and measured responsiveness in one installable bundle | Persistent explicit stop; bounded retries/exhaustion; busy-PC yielding; update/state preservation; full acceptance matrix and independent review |

Lifecycle behavior is exercised through fixtures from S1, not deferred as a
surprise until S5. Destructive production actions remain unavailable until S4
evidence passes. The first useful read-only slices are review artifacts, not a
claim that the complete selected first release has shipped.

## Toolchain and verification

The [baseline](../toolchain/baseline.json) owns exact pins. Complete Pocock 1.3.2
and PStack 0.15.36 package snapshots have been downloaded and every listed blob
verified; [the receipt](../toolchain/source-verification.json) records SHA-256 and
Git-object identities. They are inactive snapshots. Reference/dependency closure,
project-scoped compatibility adapters and native discovery checks precede use;
shared/global skill installations remain unchanged.

This repository uses `GLOSSARY.md`, matching the latest pinned Pocock package.
Project guidance maps legacy installed references to `CONTEXT.md` onto that
single glossary; it does not rename other projects' files. The passive source-link
census found only template/example targets among six unresolved literal links;
it is not proof that every external executable or harness resource is available.

Proposed .NET baseline: SDK 11.0.100, runtime/ASP.NET Core 11.0.0, C# 15.0,
Windows x64. SDK release metadata is verified; the SDK is not installed.
Direct NuGet artifacts are identified and hashed, but full target/RID compatibility
and transitive locking await S0. Freeze successful resolution in `global.json`,
central exact package versions, `packages.lock.json`, and any local-tool manifest.
Use locked restore in verification. Refresh latest only at a reviewed boundary.

Planned checks: build with analyzers/warnings as errors, formatting verification,
and meaningful unit/integration tests through module interfaces. Synthetic test
fixtures and controllable clocks exercise failure, expiry and recovery; CI uses
those fixtures, not this PC's real sessions or clinical files. Pin CI actions by
immutable commit when adding the workflow. Ruff/mypy remain conditional on actual
Python helpers; no Python application layer is required by this design.

## Completion and open evidence

Use [the acceptance matrix](acceptance-matrix.md) to trace each requirement to
its slice and evidence. Codex project registration remains an administrative
[open task](https://github.com/mshamblin5150-code/drone-workbench/issues/5).
Desktop attachment, synthetic controls, restoration, numeric budgets and full
dependency/discovery checks are explicit implementation gates, not observed facts.

This plan can be confirmed without pretending those gates have passed. A failed
gate that changes the agreed experience requires a new human ruling. Confirmation
of the plan does not itself start production implementation or activate cleanup.

# Workbench source contract

Status: proposal for owner confirmation. Nothing here is compiled or verified
code. The [Claude adapter plan](claude-adapter-plan.md) implements it, and the
[acceptance scenarios](claude-adapter-acceptance.md) test it.

This reference defines the one seam between every source adapter and the
Activity module named in the [architecture rationale](architecture-rationale.md).
Claude, Codex, Windows process observation and intentional publications all
cross this seam with the same types. An adapter never hands raw source text,
envelopes or exceptions to Activity, storage, logs or views.

The contract preserves the findings in the
[Claude results](../research/claude-ticket7-results.md),
[Codex results](../research/codex-ticket7-results.md),
[combined assessment](../research/ticket7-combined-assessment.md) and
[Codex connection diagnosis](../research/codex-connection-diagnosis.md).

## Seam

```csharp
interface ISourceAdapter
{
    SourceKind Kind { get; }
    Task<CapabilityReport> DescribeAsync(CancellationToken ct);
    IAsyncEnumerable<SafeSourceBatch> ObserveAsync(CancellationToken ct);
    Task<ControlDispatch> DispatchAsync(VerifiedControl control, CancellationToken ct);
    Task<ControlEvidence> ReconcileAsync(ControlReceiptRef receipt, CancellationToken ct);
}
```

Activity calls `ObserveAsync` and passes each batch to
`IActivity.ObserveAsync`. Activity alone builds a `VerifiedControl` after its
own checks. `VerifiedControl` carries the exact `WorkKey`, source generation,
expected incarnation and required state for atomic source enforcement. An adapter
cannot mint a `VerifiedControl`, and a batch cannot carry one.

## Terms

This file uses **work item** as the [glossary](../../GLOSSARY.md) defines it:
anything a source reports with its own identity, such as a session, turn,
subagent, thread, command or Workbench job. A **Job** is one kind of work item,
`WorkKind.Job`. Views count each kind separately.

## Identity

| Type | Meaning | Rule |
| --- | --- | --- |
| `SourceInstanceId` | One reporting instance, such as Claude Code for one Windows user and one configuration root, or one Codex app-server owner | Workbench mints it. Its key is source kind, the owner (the Claude configuration root, or for Codex the owning server process identity or exposed endpoint), and a keyed fingerprint of that owner. Two Codex servers sharing one `CODEX_HOME` are two source instances. Paths stay in private local storage |
| `SourceGeneration` | Counter for one adapter connection or source version | Increments on adapter start, reconnect, or an observed source version change. A batch from an older generation cannot revive or replace newer facts |
| `WorkKey` | Stable identity of one work item | `(SourceInstanceId, WorkKind, NativeId)`. `WorkKind` is `Session`, `Turn`, `Subagent`, `Thread`, `Command` or `Job`. Claude `sessionId` and Codex thread ID are native IDs. A Codex `Command` native ID includes its connection generation, so a command never keeps its identity across a reconnect. After a reconnect it becomes `Unknown` with `ConnectionLost` unless the new connection reports it again |
| `ParentKey` | Optional parent `WorkKey` | Subagent to session, turn to session or thread. Present only with source evidence |
| `ControlReference` | Source-local handle used to send a control | Carries `ReferenceScope`, either `SourceStable` or `ConnectionScoped(connectionGeneration)`. Claude short `id` is `SourceStable` and must equal the leading characters of the observed `sessionId`. A Codex `command/exec` `processId` is `ConnectionScoped` and becomes invalid on reconnect |
| `ProcessIncarnation` | One operating-system process lifetime | `(pid, creationTimeUtc)`. A PID alone never identifies work. A new incarnation for the same `WorkKey` is a restart, not new work |
| `Association` | Link between a `WorkKey` and a `ProcessIncarnation`, project or publication | Carries its evidence (`AgentViewPid`, `ParentProcessChain`, `HookSession`, `PublisherClaim`). Association is never ownership |
| `OwnershipEvidence` | Why a source instance is responsible for a work item | For a Claude background session, the agent view of the current Windows user's configuration root lists it as `kind=background` with a short `id`, and its incarnation runs as the same Windows user. For a Codex command, the command runs on a server Workbench started, with entrypoint `WorkbenchHosted`. Association alone never sets it. A stopped-target no-op retains validated same-user ownership evidence from its prior incarnation, bound to this exact work and source, and requires a fresh stopped observation in the current generation. Missing ownership evidence refuses the no-op too |
| `ProjectRef` | Workbench project identity | Resolved from a source working directory through the private local mapping, then the directory is discarded. Unresolved work is `Unassigned`, not dropped |

A Claude interactive session and a desktop-owned session have no short `id`.
They get a `WorkKey` and no `ControlReference`.

## Time

Every fact carries Workbench time and, when the source provides it, source time.

| Field | Source | Use |
| --- | --- | --- |
| `ObservedAt` | Host `TimeProvider` when the adapter received the fact | Freshness, ordering within a generation, history expiry |
| `SourceReportedAt` | Source value such as agent-view `startedAt` | Display only. Workbench never corrects skew or computes freshness from it |
| `ReceivedAt` | Host time when a hook or publication arrived | Same as `ObservedAt` for push channels |
| `LastConfirmedAt` | Latest `ObservedAt` of a poll that listed the work | Display. Staleness counts missed polls, not this time |
| `StateSince` | First `ObservedAt` of the current projected state | Display and dwell checks |

`LivenessSettings` injects five values: `MissedPollLimit`, `MissedPollMargin`,
`StartingWindow`, `RestartWindow` and `LostSettlingWindow`. Their numbers come
from measurements under
[Measure and record collector budgets](https://github.com/mshamblin5150-code/drone-workbench/issues/24).
This contract fixes none of them.

The poll interval is not a setting. The collector owns its schedule and reports
each scheduled attempt as one batch that carries the interval it scheduled:

- `SafeSourceBatch.Polled` is a successful poll, with the listing and a process
  inventory that is complete or partial.
- `SafeSourceBatch.Failed` is a failed attempt, with its `DisconnectReason`.
- `SafeSourceBatch.Unavailable` reports that the executable or endpoint is
  absent.

`SafeSourceBatch.Hooks` is not scheduled. It carries no interval and confirms
nothing.

### Missed polls

Staleness counts missed polls. It never compares elapsed time against one
threshold. Activity keeps one count for each source and one for each work item.

- A scheduled batch that does not confirm the subject adds one miss when it
  arrives. For a source, only a `Polled` batch confirms it, and an
  `Unavailable` batch restarts its count instead of adding a miss. For a work
  item, only a `Polled` batch that lists the item confirms it. A confirmation
  resets the count to zero.
- An expected poll that never arrived also counts as a miss. After the latest
  scheduled batch, the first expected poll is missed once that batch's interval
  plus `MissedPollMargin` has passed with no further batch. Each later expected
  poll is missed one interval after the one before it.
- When the next scheduled batch arrives, Activity first adds the expected polls
  already missed under the old schedule to the source count and to each work
  item count. Then it applies the batch, and the batch's interval starts the new
  schedule. A late batch therefore keeps every expected poll missed before it
  arrived, and a batch that does not confirm the subject never lowers its count.
- The limit is reached when the arrived misses plus the overdue expected polls
  reach `MissedPollLimit`.

A source at the limit reads `Disconnected`, and its work reads `Stale` with
`SourceDisconnected`. A work item at its own limit while its source is still
`Connected` reads `Stale` with `NoConfirmation`. An item that on-time
successful polls stop listing therefore goes `Stale` after `MissedPollLimit`
polls that did not list it, however long or short those intervals were. A late
poll that does not list the item also counts the expected polls it missed.

## Channels

| Channel | Example | Evidence strength |
| --- | --- | --- |
| `Poll` | `claude agents --json --all`, Codex `thread/loaded/list` | Snapshot of what the source lists at `ObservedAt` |
| `ProcessScan` | Windows process identity and parent chain | Process liveness only. No semantic state |
| `Hook` | Claude `SessionStart`, `SessionEnd`, `Stop`, `Notification`, `SubagentStart`, `SubagentStop` | Prospective event. Missing events prove nothing |
| `Publication` | `drone-workbench report --stdin` | A scoped claim by a publisher. Never identity or authority |
| `ControlResult` | Exit code and classified message of a control command | Source acknowledgement only. Not an outcome |

## Liveness

Work state and source state are separate enumerations. Each projected state
carries a `ReasonCode`, `StateSince`, `LastConfirmedAt` and the channel that set
it.

### Work states

| State | Entered when | Never entered from |
| --- | --- | --- |
| `Starting` | Listed without `pid` and `status`, within `StartingWindow` of first sight | A later restart gap |
| `Busy` | `status=busy` with a live incarnation | Hook events alone |
| `Waiting` | `status=waiting`, or a `Notification` hook with `permission_prompt`, `idle_prompt`, `agent_needs_input` or `elicitation_dialog` | Missing data |
| `Idle` | `status=idle` with a live incarnation | Missing `status` |
| `Restarting` | A previously live entry loses `pid` and `status` while still listed, or the incarnation exits while the source still lists the work and no `SessionEnd` preceded the exit | Absence from the list |
| `Stopped` | Background `state=stopped`, or a verified control outcome | Idempotent stop acknowledgement alone |
| `Ended` | Interactive entry gone and its last incarnation exited (`ProcessExited`). `SessionEnd` followed by incarnation exit, even while still listed (`SessionEndedThenExited`). A `Stopped` entry removed from the list while a complete process inventory shows no process (`RemovedAfterStop`) | Absence alone, or `SessionEnd` alone |
| `Lost` | The settling rule below holds for unlisted work with no recorded process incarnation (`AbsentThroughSettling`) | `Stopped`, or any absence the settling rule does not cover |
| `Stale` | The work reached `MissedPollLimit` missed polls (`NoConfirmation`), its source is `Disconnected` (`SourceDisconnected`), or its source reported `Unavailable` (`SourceUnavailable`) | `Ended` or `Lost` |
| `Unknown` | Listed entry with contradictory or unrecognized fields, incarnation alive but entry missing (`NotInAgentView`), unlisted with no process to check before `Lost` settles (`NotInAgentView`), unlisted while a partial inventory leaves the process unknown (`ProcessInventoryIncomplete`), `Starting` past `StartingWindow` (`StartupOverdue`), or `Restarting` past `RestartWindow` (`RestartOverdue`) | Not applicable |
| `NotLoaded` | Codex stored thread not loaded in the live owner | Claude sources |

A **life** of a work item is the span from its first sighting, or its
reappearance after a terminal state, to its next terminal state. `Ended` and
`Lost` are terminal for that life. If an `Ended` or `Lost` work item is listed
again, including a respawn after `SessionEndedThenExited`, it starts a new life
under the same `WorkKey`. The new life starts with a fresh first-seen time and
carries no `SessionEnd` evidence or ownership evidence from the old one.

`Lost` means the work disappeared with no evidence of how it ended, so views
never show it as `Ended` or successful. It applies only to work with no recorded
process incarnation. Work with one resolves through that process instead. A
complete inventory that no longer shows it gives `Ended` with `ProcessExited`.
Activity keeps two times for the settling rule:

- `AbsentSince` is when the work was first unlisted with no process to check.
  A listing clears it.
- `SettledSince` is the start of the source's current unbroken run of on-time
  `Polled` batches with complete process inventories. A partial inventory, a
  `Failed` batch, an `Unavailable` batch or a new source generation clears it.
  A batch that arrives later than the previous batch's interval plus
  `MissedPollMargin` restarts it at that batch.

The work becomes `Lost` at the first poll that does not list it and arrives
more than `LostSettlingWindow` after the later of those two times.

`RestartOverdue` is sticky. A later restart gap keeps the work `Unknown` with
`RestartOverdue` until the entry lists a live incarnation again.

A source `state` value is recorded but does not override the rules above. The
Claude restart experiment showed `state=blocked` persisting while the process
was gone, so a raw `state` cannot keep work `Idle`, `Busy` or `Waiting`.

### Source states

| State | Meaning |
| --- | --- |
| `Connected` | The latest successful poll set it, and the source's missed polls have not reached `MissedPollLimit` |
| `Disconnected` | The source's missed polls reached `MissedPollLimit`. Each `Failed` batch counts as one missed poll. The reason is the `DisconnectReason` of the last `Failed` batch since the latest `Polled` or `Unavailable` batch, one of `Timeout`, `NonZeroExit`, `MalformedOutput`, `OutputTooLarge`, `TransportError` or `SchemaDrift`. When no `Failed` batch arrived in that span, silence alone reached the limit and the reason is `Timeout`. A source whose every attempt so far failed was never connected, so it reads `Disconnected` from its first `Failed` batch. Work from this source becomes `Stale` with reason `SourceDisconnected` |
| `Unavailable` | The latest batch was `Unavailable`, so the executable or endpoint is absent, such as no Claude CLI on `PATH` or no exposed Codex desktop endpoint. It applies at once and restarts the source's missed-poll count. Work from this source becomes `Stale` with reason `SourceUnavailable` at once. If the source then falls silent, it reads `Disconnected` with `Timeout` once overdue polls reach the limit |
| `Unsupported` | Workbench has no adapter for this entrypoint |

## Coverage and capability report

`DescribeAsync` returns one `CapabilityReport` per source instance. Views show it
on the Coverage page and in `drone-workbench sources --json`.

```csharp
record CapabilityReport(
    SourceInstanceId Source, SourceGeneration Generation, string SourceVersion,
    SourceState State, IReadOnlyList<EntrypointCoverage> Entrypoints,
    DateTimeOffset ObservedAt);

record EntrypointCoverage(
    Entrypoint Entrypoint,              // Desktop, InteractiveCli, Background, Headless, WorkbenchHosted, Ide, Cloud, Unknown
    CoverageLevel Observation,          // Verified, Observed, Partial, Unverified, Unsupported, Unreachable
    IReadOnlyList<Channel> Channels,
    IReadOnlyList<WorkKind> WorkKinds,
    IReadOnlyList<ControlCapability> Controls,
    EvidenceRef Evidence, string? GapReason);

record ControlCapability(
    ControlAction Action, WorkKind Target, IReadOnlyList<TargetCondition> VerifiedFor,
    bool Enabled, DisabledReason? Disabled, EvidenceRef Evidence);
```

Rules:

- `Verified` needs a receipt from a Workbench conformance run on the reported
  `SourceVersion`. `Observed` means a recorded research experiment saw it, but
  no Workbench conformance receipt exists yet. Documentation or help text alone
  gives `Unverified`. `Unsupported` means Workbench has no adapter for it.
  `Unreachable` means the adapter exists but no supported route reaches the
  owner.
- `CoverageLevel.Unreachable` describes an entrypoint. `SourceState.Unavailable`
  describes a source instance. They are different fields.
- `WorkbenchHosted` covers work on a Codex server that Workbench started.
  `Headless` stays reserved for work the user runs, such as `claude -p`.
- A source version change sets every control to `Enabled=false` with
  `DisabledReason.VersionChanged` until a supported conditional route and its
  conformance evidence are re-established on the new version. A source restart invalidates generation-bound
  dispatch authorization. Neither restart nor conformance can enable a route
  that lacks atomic preconditions. Observation continues and shows the version
  change.
- `VerifiedFor` lists the target conditions the evidence covers, such as `Idle`.
  Activity refuses dispatch when the target's current state is not listed.
  The zero-dispatch `AlreadyStopped` observation path below is separate.
- An entrypoint with no route says so in `GapReason`. Missing coverage never
  becomes an empty work list.

## Controls

### Actions

| Action | Source | Target | Current evidence |
| --- | --- | --- | --- |
| `StopBackgroundSession` | Claude | `Session` with entrypoint `Background`, a `SourceStable` reference and `OwnershipEvidence` | Native session-scoped stop observed in ticket 7 on CLI 2.1.312. Workbench `Enabled=false`, `NoConditionalDispatch`, with no dispatch conditions in `VerifiedFor`. No supported atomic route established |
| `TerminateOwnedCommand` | Codex | `Command` on a Workbench-owned server, entrypoint `WorkbenchHosted` | Observed in ticket 7 for a synthetic sleeper on CLI 0.167.0-alpha.4.1. No Workbench conformance yet |
| `InterruptTurn` | Codex | `Turn` | Unverified. Disabled |

No action targets an interactive or desktop-owned Claude session, a Codex
desktop thread, a name, a partial name, or a bare process. Read-only process
visibility never enables termination.

When its dispatch requirements are met, `StopBackgroundSession` acts on the current user's background sessions,
including ones started outside Workbench, because the first release promises
verified controls for PC-wide work. The gate requires `OwnershipEvidence` for
the target. The proposed default caller scope is the authenticated dashboard
operator only. Drone reporting scope and the CLI cannot stop sessions. Letting
drones request stops is a separate owner decision.

### Dispatch authority

A dispatch must atomically bind the exact `WorkKey`, source generation,
`ProcessIncarnation` and required state, including `Idle` when that is the
verified condition. A source-stable session reference alone grants no authority
over a replacement incarnation.

The observed `claude stop <short id>` route cannot enforce those preconditions
atomically. Workbench therefore keeps it disabled with `NoConditionalDispatch`
and sends no stop command, even for a fresh, owned, idle target. Rechecking,
holding the old process handle, source acknowledgement, or another conformance
run against that unconditional command cannot close the race or guarantee that
a replacement stays untouched. No alternate conditional Claude API is established.

Enabling real dispatch requires an actually supported source route that enforces
all those preconditions atomically, plus conformance proof on that source
version. A broader session-scoped authority model requires a separately
authorized future policy change. Verified PC-wide controls remain a product
requirement, with this gap explicit until one of those conditions is met.

### Receipt stages

Activity persists each stage with its `ObservedAt` before it reports the stage.

```text
Requested -> Refused                                     (terminal)
Requested -> NoActionNeeded(AlreadyStopped)               (terminal, zero dispatch)
Requested -> Accepted -> Dispatched -> SourceAcknowledged | SourceRejected | DeliveryUnknown
SourceAcknowledged | DeliveryUnknown -> OutcomeVerified | OutcomeNotObserved | OutcomeContradicted
SourceRejected -> OutcomeVerified | OutcomeNotObserved    (reconciliation only)
```

Process exit is separate evidence. Each receipt holds a `ProcessExit` record per
recorded incarnation, with `Exited`, `StillRunning` or `NotObserved` and its
`ObservedAt`. A source outcome never implies process exit, and process exit
never implies the source outcome.

| Stage | Meaning |
| --- | --- |
| `Requested` | A caller asked. Carries a client `RequestId` and exact `WorkKey`. A dispatch request also carries the `ProcessIncarnation` the caller saw. A different current incarnation gives `Refused(IncarnationChanged)` |
| `Refused` | The gate refused before dispatch. Carries a `RefusalReason` such as `NoSupportedRoute`, `NoConditionalDispatch`, `NoOwnershipEvidence`, `TargetConditionUnverified`, `StaleTarget`, `IncarnationChanged`, `CallerScope`, `VersionChanged` or `RequestIdConflict`. A request for `Stale`, `Unknown`, `Ended` or `Lost` work gives `Refused(StaleTarget)`. Nothing was sent to the source |
| `NoActionNeeded` | `AlreadyStopped` is a fresh validated observation for an authenticated operator and exact owned `WorkKey`, with no current live incarnation. No live handle is required. No source command runs and no later receipt stage follows |
| `Accepted` | A supported conditional route exists. Activity rechecked identity, ownership evidence, freshness, capability, target condition, reference scope and caller scope, and recorded the target's prior state and incarnation |
| `Dispatched` | The adapter started the source command or request |
| `SourceAcknowledged` | A supported dispatched request received source success. This is not the Workbench `AlreadyStopped` path |
| `SourceRejected` | The source refused, with a classified reason such as `NoMatchingJob` |
| `DeliveryUnknown` | Timeout, crash or lost reply after dispatch |
| `OutcomeVerified` | The source's own view matches the intent. For a Claude stop this is `state=stopped` in the `--all` agent view and no live incarnation listed. The `ProcessExit` records are reported beside it |
| `OutcomeNotObserved` | The verification window ended without that evidence |
| `OutcomeContradicted` | Evidence opposes the intent, such as a new incarnation under the same short ID |

### Retry and attribution

- For an authenticated caller authorized to read the receipt, the same `RequestId`
  returns the existing receipt. A repeat adds no dispatch.
- A `RequestId` reused for a different action or target is `Refused` with
  `RequestIdConflict`. Nothing is sent to the source.
- After `DeliveryUnknown`, Activity runs `ReconcileAsync` first. It dispatches
  a new attempt only on a new `RequestId`, and only when the target incarnation
  still matches the recorded one. An attached or respawned session is a new
  target.
- For a new request on stopped work, validate caller scope, exact `WorkKey`,
  ownership, source generation and fresh stopped evidence with no live incarnation.
  This is a snapshot result, not a promise that the session stays stopped.
  Return terminal `NoActionNeeded(AlreadyStopped)` without `Accepted`, `Dispatched`
  or `SourceAcknowledged`. Say "already stopped", never "Workbench stopped it".
  Missing, stale or contradictory evidence refuses with its reason or remains
  unknown. It never produces this no-op result.
- A later restart requires a new request and all dispatch gates again. An old
  no-op receipt never authorizes a new incarnation.
- Native CLI repeat-stop success is a provider research fact only. It does not
  translate into Workbench dispatch or acknowledgement. Codex repeat-terminate
  rejection remains a distinct source result in provider fixtures.
- Process kill is never a fallback route. The Claude fixture respawned after an
  external kill.

## Filtering before persistence

Adapters parse source output with an allowlist into typed values. Every
unlisted field is dropped in the parser. Raw bytes never reach storage, logs,
exception messages, live updates or CLI output. Parse errors carry a code and a
field name from the allowlist, never source text.

### Claude agent view

| Field | Disposition |
| --- | --- |
| `kind` | Keep as enum. Unknown value gives `Unknown` with `SchemaDrift` |
| `status` | Keep as enum `busy`, `waiting`, `idle` |
| `state` | Keep as enum `working`, `blocked`, `done`, `failed`, `stopped` |
| `sessionId` | Keep after UUID validation |
| `id` | Keep after format validation and prefix match against `sessionId` |
| `pid` | Use in memory to build a `ProcessIncarnation`. Persist only inside the incarnation record |
| `startedAt` | Keep as `SourceReportedAt` |
| `waitingFor` | Map known values to an enum. Drop unmatched text and record `Unknown` |
| `cwd` | Resolve to `ProjectRef` in memory, then drop |
| `name` | Drop |
| Any other field | Drop. Count dropped field names per poll for drift reporting |

### Claude hooks

The `Basis` column says whether ticket 7 observed the event or only the
official hooks documentation describes it. CA4 must confirm every `Docs` row
against a synthetic run before relying on it.

| Event | Basis | Kept fields | Dropped fields |
| --- | --- | --- | --- |
| Every event | Observed | `hook_event_name`, `session_id`, `cwd` (resolved, then dropped) | `transcript_path`, `scratchpad_dir`, `permission_mode`, every unlisted field |
| `SessionStart` | Observed | `source`, `model` | `session_title`, `agent_type`, cache fields |
| `SessionEnd` | Observed | `reason` | Every other field |
| `Stop` | Docs | Event only | `last_assistant_message`, `background_tasks`, `session_crons`, `stop_hook_active` |
| `Notification` | Docs | `notification_type` | `message`, `title` |
| `SubagentStart` | Docs | `agent_id` | `agent_type` |
| `SubagentStop` | Docs | `agent_id` | `agent_transcript_path`, `last_assistant_message`, `agent_type`, `stop_hook_active` |

`UserPromptSubmit`, `PreToolUse`, `PostToolUse`, `PreCompact` and every other
event are not subscribed. Their payloads carry prompts, tool inputs, tool output
or compact instructions.

The hook verb runs in the Claude session's process tree and cannot read the
host's private project mapping. It sends `cwd` to the host only inside a
`ProjectHint` field of the local user-scoped channel message. `ProjectHint` has
no serializer for storage, logs or live updates. The host resolves it to a
`ProjectRef` and discards it before the observation is committed.

### Codex

Codex `initialize` home and platform fields, command lines, thread titles and
turn content are dropped. Thread, turn and command identifiers, status enums
and timestamps are kept.

## Intentional summaries

`drone-workbench report --stdin` accepts one JSON object. The host derives the
publisher scope from the local channel. Publications follow the build plan's
publication rules.

```json
{
  "schema": 1,
  "project": "<ProjectRef the publisher claims>",
  "work": { "sourceKind": "claude", "sessionId": "<optional claimed UUID>" },
  "phase": "planning | implementing | verifying | blocked | done",
  "headline": "<short text, admitted only when the project's disclosure rule allows it>",
  "counts": { "filesChanged": 0, "testsPassed": 0, "testsFailed": 0 }
}
```

- A claimed `sessionId` links to observed work only when that `WorkKey` exists
  and resolves to the same `ProjectRef`. Otherwise the publication is stored
  unassociated.
- Unknown properties reject the whole publication. A rejected publication is
  counted, and its content is never stored or logged.
- A publication cannot set work state, ownership or capability.
- When the project's disclosure rule refuses `headline`, the host drops the
  field, admits the rest and counts the refusal. The refused text is stored or
  logged nowhere.

## Codex fit

| Codex finding | Contract expression |
| --- | --- |
| Desktop app-server runs over private stdio with no exposed endpoint | `Entrypoint.Desktop`, `Observation=Unreachable`, `GapReason=NoSupportedEndpoint`. `ProcessScan` still shows the app-server process |
| Workbench-owned or explicitly exposed server over WebSocket on a Unix socket | A separate `SourceInstanceId` from the desktop owner, even with the same `CODEX_HOME`. A server Workbench started reports `Entrypoint.WorkbenchHosted`. `Observation=Partial` until active-work and reconnect evidence exists |
| `command/exec` `processId` is connection scoped | `Command` `WorkKey` and `ControlReference` both carry the connection generation. Reconnect invalidates both |
| Repeat terminate rejected | `SourceRejected(NoActiveCommand)` |
| Loaded versus stored threads | `NotLoaded` work state |

## Change rule

Adding a source field needs a disposition row in this file and a canary test
that proves the dropped form never reaches storage, logs or views.

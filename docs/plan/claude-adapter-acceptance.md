# Claude adapter acceptance scenarios

Status: proposed acceptance contract. No scenario has passed against Workbench
code, because no adapter exists. The `Evidence today` column says what the
ticket 7 experiments or the official docs already show about the source.

These scenarios refine the "Claude state and control", "Distinct identities",
"Unknown and stale behavior", "Structured summaries and privacy" and "Verified
external controls" rows of the [acceptance matrix](acceptance-matrix.md). They
use the types in the [source contract](source-contract.md). The
[Claude adapter plan](claude-adapter-plan.md) assigns each scenario to a pull
request.

## Fixture rules

- `P` scenarios use synthetic observations or controlled-fake adapter routes.
  They need no Claude process. Conditional control fixtures prove the contract
  only. They never establish a supported Claude route or provider conformance.
- `S` scenarios start synthetic background sessions with `claude --bg` and no
  prompt. Each uses a per-session `--settings` file, `--setting-sources ""` and
  `--strict-mcp-config`, and the scenario removes its sessions with `claude rm`
  by exact short ID at the end.
- `O` scenarios need the operator to start a Claude session by hand, which is
  also the realistic case of work started outside Workbench. Workbench only
  observes. An agent never starts an interactive `claude`, because that can
  load user settings and write folder-trust state to user configuration.
- `R` scenarios read the real agent view through the adapter's filter. They
  never stop, attach to, read logs of, or remove real sessions.
- Every `S`, `O` and `M` lane records SHA-256 hashes of the user, project and
  local Claude `settings.json` files before and after. A changed hash fails the
  lane.
- `CanaryScanner` searches for two sets of values. Fixture canaries come from
  the fixture files. Real values (paths, names, session IDs and PIDs) are read
  by the scanner process itself at scan time, held in memory only, and never
  written. Its report holds match counts and file names, not values.
- `M` scenarios need a synthetic model turn. They stay blocked until the owner
  authorizes model use for testing. A blocked `M` scenario is a coverage gap,
  not a pass.
- No scenario edits user, project or managed Claude settings, installs a hook,
  or reads a transcript.

## Coverage and PC-wide visibility

| ID | Type | Given | When | Then | Evidence today | PR |
| --- | --- | --- | --- | --- | --- | --- |
| CV-1 | R | Desktop Code sessions are running | The adapter polls | Each appears as a `Session` with `Entrypoint.Desktop`, classified by its parent process chain, with no `ControlReference` and controls `NoSupportedRoute` | Observed listing. Ticket 7 matched desktop sessions to app child processes by PID. The parent-chain classifier is untested | CA3 |
| CV-2 | O | The operator started an idle native interactive `claude` without a prompt outside Workbench | The adapter polls and successfully inventories processes | If absent from the agent view, it appears only in unmatched process accounting with `NotInAgentView`, not as an invented session. Incomplete inventory reports unknown totals. Controls remain unavailable | Observed absence across three launches in the post-foundation assessment | CA3 |
| CV-2b | M | A first synthetic model turn in native interactive CLI is separately authorized | The adapter polls after the prompt | Record whether the source lists it before claiming `InteractiveCli` session coverage. Until demonstrated, keep post-prompt coverage `Unverified` | Not tested; model-turn authorization remains required | CA3 |
| CV-3 | S | Two synthetic background sessions run in two synthetic projects | The adapter polls | Both appear with `Entrypoint.Background`, separate `ProjectRef` values and a `SourceStable` reference each | Observed for one project | CA3 |
| CV-4 | M | A synthetic `claude -p` or Agent SDK run is active | The adapter polls during the run | The run is listed, or the Coverage view shows `Entrypoint.Headless` as `Unverified` with a gap reason. It is never silently absent | Unverified | CA3 |
| CV-5 | R | A `claude` process runs that the agent view does not list | The adapter reconciles processes | Coverage shows an unmatched Claude process count with reason `NotInAgentView` | Unverified | CA3 |
| CV-6 | P | The Claude CLI is not on `PATH` | The adapter starts | Source state is `Unavailable`. The work list is not empty-and-healthy. Coverage names the gap | Not tested | CA3 |
| CV-7 | P | No conformance evidence exists for IDE, cloud, WSL, elevated or other configuration-root sessions | The Coverage view renders | Each of those rows shows `Unverified` with a gap reason. None shows `Verified`, `Observed` or an empty list | Gaps recorded in ticket 7 | CA6 |
| CV-8 | P | An interactive entry whose parent chain reaches neither the desktop app nor a known terminal host | The adapter classifies it | `Entrypoint` is `Unknown` with `UnrecognizedLaunchPath`, not `InteractiveCli` | Not tested | CA3 |

## Identity

| ID | Type | Given | When | Then | Evidence today | PR |
| --- | --- | --- | --- | --- | --- | --- |
| ID-1 | P | A background entry has `id` that is not a prefix of `sessionId` | The parser reads it | The entry is `Unknown` with `SchemaDrift`. No `ControlReference` is created | Observed prefix relation | CA2 |
| ID-2 | S | A synthetic background session process is killed externally | The supervisor respawns it | The `WorkKey` is unchanged. A new `ProcessIncarnation` is linked. No new session appears | Observed respawn, same short ID, new PID | CA3 |
| ID-3 | P | A PID from an exited incarnation is reused by an unrelated process | The adapter correlates | Creation time differs, so the old work is not revived and the unrelated process is not associated | Not tested | CA3 |
| ID-4 | P | A batch from an older `SourceGeneration` arrives after a newer one | Activity applies it | The older batch changes nothing | Not tested | CA1 |
| ID-5 | M | A session starts a subagent with hooks enabled for the synthetic run | Hooks arrive | The subagent is a separate `WorkKey` with `ParentKey` set. Session and subagent counts stay separate | Unverified. Agent view lists no subagents | CA4 |

## Liveness and staleness

| ID | Type | Given | When | Then | Evidence today | PR |
| --- | --- | --- | --- | --- | --- | --- |
| LV-1 | S | A background session was just started | The adapter polls inside `StartingWindow` | State is `Starting`, not `Ended` or `Unknown` | Observed missing `pid` and `status` briefly | CA3 |
| LV-2 | S | A background process was killed and respawn has not finished | The adapter polls in the gap | State is `Restarting`. A raw `state=blocked` does not produce `Idle` or `Waiting` | Observed stale `state` | CA3 |
| LV-3 | P | The agent-view command times out, exits non-zero, emits malformed JSON or exceeds the output limit | Each failure arrives as a `Failed` batch, and the failures plus any overdue expected polls reach `MissedPollLimit` | Source is `Disconnected` with the last failure's reason. Its work becomes `Stale` with `SourceDisconnected`. Nothing becomes `Ended` or zero | Not tested | CA3 |
| LV-4 | P | An interactive entry disappears while its incarnation is still alive | The adapter reconciles | State is `Unknown` with `NotInAgentView` | Not tested | CA3 |
| LV-5 | P | An interactive entry disappears and its incarnation exited | The adapter reconciles | State is `Ended`, reason `ProcessExited` | Not tested | CA3 |
| LV-6 | P | A `SessionEnd` hook arrives while the incarnation stays alive | Activity applies it | State stays as polled until the incarnation exits | Observed `SessionEnd` on stop | CA4 |
| LV-7 | P | `status=waiting` with a known `waitingFor` value and an unknown one | The parser reads both | The known value maps to an enum. The unknown text is dropped and recorded as `Unknown` | Docs only | CA2 |
| LV-8 | P | A work item with no recorded process incarnation is unlisted, and every poll is on time, succeeds and reports a complete process inventory | `LostSettlingWindow` passes after the later of `AbsentSince` and `SettledSince` | State is `Lost` with `AbsentThroughSettling`, never `Ended`. If the entry is listed again, it starts a new life under the same `WorkKey`. A partial inventory, a `Failed` or `Unavailable` batch, a late poll or a new source generation inside the window restarts it, and a listing clears it | Not tested | CA1 |
| LV-9 | P | Each scheduled batch carries the collector's interval, and the source has live work | Expected polls are missed, attempts fail, or successful polls stop listing one item | Each scheduled batch that does not confirm the subject is one miss, and overdue expected polls count from the latest batch's interval plus `MissedPollMargin`. A late batch keeps the expected polls missed before it, so a `Failed` batch after silence reached the limit leaves the source `Disconnected` and its work `Stale`. Below `MissedPollLimit` misses, the source stays `Connected` and work keeps its state. At the limit, the source is `Disconnected`, with `Timeout` for silence alone or the last failure's reason, and its work is `Stale` with `SourceDisconnected`. An item unlisted by `MissedPollLimit` successful polls is `Stale` with `NoConfirmation` while the source stays `Connected`. Hooks confirm nothing. Missed polls alone make nothing `Ended` or `Lost` | Not tested | CA1 |

## Workbench and source restarts

An incarnation change can occur between samples without an observed restart
gap. Link the new incarnation when supported by the metadata, but do not invent
an intermediate `Restarting` observation. Supervisor health is independent of
successful agent-view polling. Until a supported signal exists, report it as
unknown and keep controls unavailable.

| ID | Type | Given | When | Then | Evidence today | PR |
| --- | --- | --- | --- | --- | --- | --- |
| RS-1 | S | Workbench host restarts while synthetic sessions run | The first poll after restart completes | Existing `WorkKey`s are matched again. No duplicate work. `SourceGeneration` increments | Fresh re-query observed | CA3 |
| RS-2 | P | Host restarts with a controlled-fake stop receipt in `Dispatched` | Activity resumes | Reconcile the observed outcome and separate exit evidence. No blind re-dispatch. Source restart invalidates generation-bound authorization and never enables current CLI stop | Not tested | CA5 |
| RS-3 | P | The reported Claude CLI version changes | The next `DescribeAsync` runs | Controls report `Enabled=false`, `VersionChanged`. Observation continues. Re-running unconditional stop conformance cannot enable controls. A supported conditional route and proof on that version remain required | Not tested | CA3 |
| RS-4 | R | The Claude desktop app restarts | The adapter polls through the restart | Desktop sessions pass through `Stale` or `Unknown`, then match by `sessionId` or end. Never a silent drop | Unverified | CA3 |

## Hooks

| ID | Type | Given | When | Then | Evidence today | PR |
| --- | --- | --- | --- | --- | --- | --- |
| HK-1 | P | A `SessionStart` payload carries canary strings in `transcript_path`, `scratchpad_dir`, `session_title`, `cwd` and an unlisted field | `drone-workbench hook claude` reads it | Only kept fields reach the host. Canaries are absent from storage, logs, live messages and CLI output | Observed field names | CA4 |
| HK-2 | P | The host is not running | The hook verb runs | It exits 0 within its time budget and writes nothing | Not tested | CA4 |
| HK-3 | S | A synthetic session was started with a per-session hook file and then killed externally | The supervisor respawns it | No `SessionEnd` is expected. A second `SessionStart` with `source=startup` maps to the same `WorkKey` as a restart | Observed | CA4 |
| HK-4 | P | Hooks were never configured | Coverage renders | Each session shows hook channel `NotObserved`. Polling alone drives state | Not applicable | CA4 |
| HK-5 | P | A `Notification` hook with `permission_prompt` arrives | Activity applies it | State becomes `Waiting`. `message` and `title` are dropped | Docs only | CA4 |
| HK-6 | P | A hook channel message carries a canary in `ProjectHint` | The host resolves and commits it | The canary is absent from storage, logs, live messages and errors. Only the `ProjectRef` remains | Not tested | CA4 |

## Controls

| ID | Type | Given | When | Then | Evidence today | PR |
| --- | --- | --- | --- | --- | --- | --- |
| CT-1 | S | An idle synthetic background session on the current Claude CLI | The authenticated operator requests a stop | `Refused(NoConditionalDispatch)`, zero dispatch and no side effect. A separate conditional-route fixture verifies accepted, dispatched, acknowledged and outcome stages only when the fake atomically binds exact work, generation, incarnation and `Idle` | Native stop observed outside Workbench. No conditional route established | CA5 |
| CT-2 | S | Two synthetic sessions, and a conditional-route fixture with a replacement race | One stop is requested | Current CLI dispatches nothing. The fake refuses changed incarnation, generation or required state atomically before a side effect. Neither replacement nor unrelated work is affected | Native wrong-ID rejection observed. Atomic guarantee unverified for Claude | CA5 |
| CT-3 | S | Fresh validated stopped evidence for an exact owned `WorkKey`, no current live incarnation and an authenticated operator | A new stop request arrives | `Requested -> NoActionNeeded(AlreadyStopped)` is terminal with zero dispatch. No `Accepted`, `Dispatched` or `SourceAcknowledged`. The view says "already stopped", never that Workbench stopped it. No live handle is needed. Missing, stale or contradictory evidence refuses or remains unknown. A later restart requires a new fully gated request | Native repeat-stop success observed only as provider research | CA5 |
| CT-4 | P | The same `RequestId` is submitted twice | Activity receives the second | It returns the first receipt. The repeat adds no dispatch | Not tested | CA1 |
| CT-5 | P | A controlled-fake conditional route acknowledged a bound stop, then reports a new incarnation under the same short ID | Verification runs | Receipt ends `OutcomeContradicted`. Atomic replacement protection is tested separately before dispatch. Reconciliation does not retroactively prevent a side effect | Respawn observed only after external kill. No Claude conditional route established | CA5 |
| CT-6 | R | A desktop or interactive session | The operator caller requests a stop through `IActivity.RequestControlAsync` | Receipt is `Refused(NoSupportedRoute)`. No source command runs | Not applicable | CA5 |
| CT-7 | P | A name or partial name is supplied as a target | Activity validates | `Refused`. Only a verified `ControlReference` is accepted | Docs only | CA5 |
| CT-8 | P | A busy synthetic observation, and a fake target that changes from `Idle` to `Busy` at dispatch | A stop is requested | Current Claude route stays `Enabled=false` with `NoConditionalDispatch`. Fake conditional dispatch refuses the changed state atomically. Busy-stop research alone cannot enable current CLI control | Busy stop unverified. No conditional route established | CA5 |
| CT-10 | P | A fixture changes incarnation, source generation or required `Idle` state between gate and dispatch | The conditional fake checks atomically | It refuses before any side effect. A later restart needs a new request and cannot reuse an old no-op receipt. Current CLI requests remain disabled regardless of local rechecks | Native respawn observed after kill. No atomic Claude route established | CA5 |
| CT-11 | P | A drone reporting caller | It requests a stop | `Refused(CallerScope)` | Not applicable | CA5 |
| CT-12 | P | A background entry from a configuration root that is not the current user's, or with no `OwnershipEvidence` | A stop is requested | `Refused(NoOwnershipEvidence)` | Not tested | CA5 |
| CT-9 | P | A controlled-fake conditional dispatch times out | Activity reconciles | `DeliveryUnknown`, then the observed outcome with separate process-exit evidence. No blind replay. A new attempt needs a new `RequestId` and all atomic route gates. Current Claude CLI still dispatches nothing | Not tested | CA5 |

## Filtering and summaries

| ID | Type | Given | When | Then | Evidence today | PR |
| --- | --- | --- | --- | --- | --- | --- |
| FL-1 | P | Agent-view bytes with canaries in `cwd`, `name` and an unknown field | The adapter ingests and the host commits | Canaries are absent from the SQLite file bytes, logs, live messages, CLI JSON and error text | Observed field names | CA2 |
| FL-2 | P | Malformed agent-view bytes containing a canary | The parser fails | The error has a code and allowlisted field name only | Not tested | CA2 |
| SM-1 | P | A publication with a claimed `sessionId` for observed work in the same project | The host admits it | It links to that `WorkKey` | Not tested | CA6 |
| SM-2 | P | A publication with an unknown property containing a canary | The host receives it | The whole publication is rejected. The canary is stored or logged nowhere | Not tested | CA6 |
| SM-3 | P | A publication claims a session in a different project | The host admits it | It is stored unassociated. It changes no state, ownership or capability | Not tested | CA6 |
| SM-4 | P | A publication whose `headline` holds a canary, for a project whose disclosure rule refuses headlines | The host admits it | The rest is admitted, the refusal is counted, and the canary is stored or logged nowhere | Not tested | CA6 |

## Shared contract fit with Codex

| ID | Type | Given | When | Then | Evidence today | PR |
| --- | --- | --- | --- | --- | --- | --- |
| CX-1 | P | Codex desktop facts from the connection diagnosis | Contract fixtures are built | Desktop coverage is `Unreachable` with `NoSupportedEndpoint` and a process-presence count | Observed | CA1 |
| CX-4 | P | A Codex desktop owner and a Workbench-owned server share one `CODEX_HOME` | Source instances are minted | They get two `SourceInstanceId`s, and neither's work appears under the other | Observed risk in the diagnosis | CA1 |
| CX-2 | P | A Codex `ConnectionScoped` command reference | The connection generation changes | The reference is invalid and a control request is refused | Observed connection scope | CA1 |
| CX-3 | P | Provider-only Codex repeat-terminate and native Claude repeat-stop fixtures, plus a Workbench stopped-target request | Receipts are classified | Provider facts retain rejection and acknowledgement respectively. Workbench returns terminal `NoActionNeeded(AlreadyStopped)` with zero dispatch and no stop attribution | Native provider results observed only | CA1 |

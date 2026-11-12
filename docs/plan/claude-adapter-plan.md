# Claude adapter plan

This plan adds the shared [source contract](source-contract.md) and a Claude adapter to Workbench.
The operator gets PC-wide Claude visibility, including desktop and externally started work, with every gap named.
Workbench shows what a source verifiably reports. A control also needs a supported atomic route and version-specific conformance. Current Claude stop remains disabled.
Codex executes the plan. The PR ids in order are CA1, CA2, CA3, CA4, CA5 and CA6.
The [acceptance scenarios](claude-adapter-acceptance.md) define pass conditions. Execution waits for the operator's explicit go.

## How to read this

One box is one unit of work. Every box names the evidence that checks it. A nested box is a sub-step of the box above it. Check a box only when its evidence exists, a file, a log line, a screenshot, a test run, or a SHA. The body is a how-to. The appendices explain and record.

The proposed program runs `pstack/skills/poteto-mode/playbooks/autopilot-stack.md`. Its operator-only merge policy awaits owner confirmation before execution and does not replace current authorization. Under that proposal, CA1 to CA6 stop at merge-ready.

Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

Resolve every `pstack/...` path in this plan against `.local/engineering-stack/sources/pstack/` at the locked commit, as `docs/agents/engineering-stack.md` requires. In Codex, open the `dwb-pstack-` and `dwb-kit-` entrypoints listed in `docs/toolchain/engineering-catalog.json`.

## Program checklist

### Arm the program

- [ ] State the protocol and this plan to the operator, then stop. Start execution only on the operator's explicit go.
- [ ] Confirm the prerequisites before any PR starts.
  - [ ] Run `pwsh -NoProfile -File tools/engineering-stack.ps1 -Action Verify` and record `verified` true, 4 packages and 103 skills.
  - [ ] Confirm the S0 toolchain slice has landed on `main`. It provides `global.json` with SDK 11.0.100, `Directory.Packages.props`, `packages.lock.json` and the `src/` and `tests/` projects named in the build plan. If it has not landed, stop and report. This plan does not install an SDK.
  - [ ] Confirm `claude --version` prints `2.1.312 (Claude Code)`. On another version, record the version and disable every Claude control. CA5 conformance cannot enable unconditional stop on any version.
- [ ] Read these from trunk at program start. Re-read them at every tick.
  - [ ] `git show origin/main:docs/plan/claude-adapter-plan.md`
  - [ ] `git show origin/main:docs/plan/source-contract.md`
  - [ ] `git show origin/main:docs/plan/claude-adapter-acceptance.md`
  - [ ] `git show origin/main:docs/agents/engineering-stack.md`
  - [ ] Read `pstack/skills/poteto-mode/playbooks/autopilot-stack.md`, `pstack/skills/poteto-mode/playbooks/autopilot-full.md`, `pstack/skills/swarm/SKILL.md`, `pstack/skills/poteto-mode/playbooks/opening-a-pr.md` and the Team Kit `control-cli` and `control-ui` skills from the pinned sources. They are ignored files, so `git show` cannot read them.
- [ ] Skipped in Codex. Arm the audit tick as `/loop 1h` with the tick prompt below. Codex has no supported loop. The coordinator runs the tick prompt by hand after each owner report and at least once per working session.
- [ ] Use this tick prompt, verbatim. "Re-read the execution playbook from trunk. Audit the operation against it and fix drift in this tick. Probe every active lane and judge progress by side effects only. Stand down a stuck lane and dispatch its replacement now. Then post a short status message to the operator in chat only when the audit found a tracked change that no earlier status message reported, such as a PR opened, a code-ready head, a round launched or closed, a verdict, a merge, a stuck agent and the action taken, a blocker added or cleared, or a decision only the operator can make. Name every such change and nothing else. Do not repeat a table, the merged list, or an unchanged blocker. If the audit found none, end the turn with no reply text. Either way, log this tick's row in your decision trail. The row names the items reported, or none."
- [ ] On the operator's hold or stand-down, send every owner a zero-writes order at once.

### Spawn owners

- [ ] Spawn one owner per PR with the full lifecycle the execution playbook names. In Codex, use `collaboration.spawn_agent` with the original goal, this plan's path, the PR section, the file fence and the completion criteria. Tell writers they share the codebase.
- [ ] Follow this dependency graph. Each PR stacks on its parent branch.
  - [ ] CA1 is first. It branches from `main` after S0.
  - [ ] CA2 after CA1.
  - [ ] CA3 after CA2.
  - [ ] CA4 after CA3 and after the S1 host channel, store and CLI land on `main`.
  - [ ] CA5 after CA3 and after the S1 store lands. CA4 and CA5 are independent of each other.
  - [ ] CA6 after CA4, CA5 and the S1 dashboard.
- [ ] Hold the file boundaries.
  - [ ] CA1 touches only `src/Workbench.Core/Activity/Contract/**`, `tests/Workbench.Core.Tests/Activity/Contract/**`, `tests/Workbench.TestSupport/**` and the solution file entry for `Workbench.TestSupport`.
  - [ ] CA2 touches only `src/Workbench.Host/Sources/Claude/Parsing/**`, `tests/Workbench.Host.Tests/Sources/Claude/Parsing/**`, `tests/fixtures/claude/agent-view/**` and `tools/fixtures/claude-synthetic.ps1`.
  - [ ] CA3 touches only `src/Workbench.Host/Sources/Claude/Observation/**`, `src/Workbench.Host/Sources/Windows/ProcessIdentity/**`, `src/Workbench.Host/Composition/ClaudeSourceRegistration.cs` and `tests/Workbench.Host.Tests/Sources/Claude/Observation/**`.
  - [ ] CA4 touches only `src/Workbench.Cli/Commands/HookClaude*.cs`, `src/Workbench.Host/Sources/Claude/Hooks/**`, `src/Workbench.Host/Store/Migrations/*ClaudeHook*`, `tests/Workbench.Cli.Tests/HookClaude/**`, `tests/Workbench.Host.Tests/Sources/Claude/Hooks/**` and `tests/fixtures/claude/hooks/**`.
  - [ ] CA5 touches only `src/Workbench.Host/Sources/Claude/Control/**`, `src/Workbench.Core/Activity/Control/**`, `src/Workbench.Host/Store/Migrations/*ControlReceipt*` and `tests/Workbench.Host.Tests/Sources/Claude/Control/**`. CA5 adds no CLI verb.
  - [ ] CA6 touches only `src/Workbench.Core/Activity/Publications/**`, `src/Workbench.Cli/Commands/Report*.cs`, `src/Workbench.Cli/Commands/Sources*.cs`, `src/Workbench.Host/Views/Coverage/**`, `src/Workbench.Host/Dashboard/Coverage/**` and their tests.
  - [ ] No PR touches `src/Workbench.Host/Sources/Codex/**`, global or project Claude settings, hook files, `~/.claude/**` or `docs/research/**`.
- [ ] Hold the review gate. CA6 changes an interaction. It waits for the operator's review in chat with screenshots and a video before merge.

### PR mechanics, for every PR

- [ ] Resolve the forge once. Default to `gh`; if `command -v origin` succeeds and Origin can resolve the repository, use `origin pr` for every PR operation. Record any fallback to `gh`. Never require `gt`.
- [ ] Open the PR ready, never draft, per **Opening a PR**. Use the run's built-in PR tool when it has one, else `origin pr create --status open --base <base-branch>` or `gh pr create --base <base-branch>` according to the resolved forge. A stack child targets its parent branch.
- [ ] Run `dotnet build -warnaserror`, `dotnet format --verify-no-changes` and `dotnet test` with locked restore once before the PR-facing push. Push with hooks on.
- [ ] Run `/deslop` before each commit and `/no-comments` before review. In Codex, apply Team Kit `deslop` to the diff and run PStack `no-comments` through its agent prompt in a fresh helper.
- [ ] Triage every Bugbot and security-reviewer comment per `../references/bugbot-triage.md`. Skipped when the PR has no Bugbot check or comment.
- [ ] Rebase the stack root onto current trunk and each child onto its parent branch's exact tip before the code-ready report and babysit. Keep that base in fix rounds. Rebase again at merge prep, on a conflict with that base, on a CI failure caused by a base change, or when a known concurrent change to the same files lands. A new parent head restacks its children. After a parent merges, move its child onto trunk per `playbooks/shipping.md`.
- [ ] Before each commit, run `git diff --cached` through `CanaryScanner` from CA1 and confirm the diff holds no real path, session ID, name or PID.

### Verdict and merge, for every PR

- [ ] A full swarm runs once, at the code-ready head. Another full swarm runs only for new behavior, a redesign, or a large new diff. A rebase, a merge-conflict resolution, or a minor fix gets a single verifier at the new head, and a wording-only change gets none. Run each full swarm per `pstack/skills/swarm/SKILL.md`. One gates lane. The ten live lanes from the PR's **Verify, live** block. The perf lane from its **Verify, perf** block. Two or more audit lanes, each with its own focus, that read the diff and the receipts and distrust the PR body. The root audits the receipts in the merge-ready report before the verdict.
- [ ] Record which reviewer roles ran. The default cross-family panel is unavailable in Codex. Inherited-model helpers give separate contexts, not multi-model verification. Report the missing family as skipped.
- [ ] Judge every finding by the execution playbook's blocking rule. The verdict is clean when none blocks. A failure on trunk behavior in a lane marked `Regression lane against trunk.` is recorded against trunk and does not block the PR. A new head gets a fresh verdict, except for results that stay valid under the patch-id rule in `playbooks/shipping.md`.
- [ ] Stop each PR at verified and green at its head and hand it to the operator. Nobody pushes after the handoff without the operator's go. A restack that leaves `git patch-id` unchanged keeps its verdict.

### Boot recipe, for every live lane

Each live lane runs in its own local worktree at the PR head. Cursor cloud VMs are unavailable, so no lane claims VM isolation. Drive through `control-cli` from `cursor-team-kit`, and through `control-ui` for the CA6 dashboard.

- [ ] `git fetch origin <head-branch> && git checkout <head SHA>`.
- [ ] For a stacked PR, "trunk" in a regression lane means the parent PR's head, because `main` does not yet hold the parent.
- [ ] Run `dotnet build` with locked restore. For lanes typed `S`, run `tools/fixtures/claude-synthetic.ps1 -SettingsHash` and save the hashes, then run `-Start <count>` and record the short IDs it prints. Start the Workbench host from the PR head for CA4 to CA6.
- [ ] Never start an interactive `claude` from a lane. Lanes typed `O` ask the operator to start the session and only observe it.
- [ ] Deliver input only through `control-cli` or `control-ui`. Read-only diagnostics are the filtered `claude agents --json --all` view through the adapter, `drone-workbench sources --json` and the Windows process identity of synthetic sessions.
- [ ] End every lane with `tools/fixtures/claude-synthetic.ps1 -Remove <short IDs>`, confirm the default agent view lists none of them, and compare `-SettingsHash` output with the saved hashes. A changed hash fails the lane.
- [ ] Save every capture to `.local/swarm/<pr-id>/worker-<n>/<slug>` and return the paths with the report. Captures stay local because they come from this PC.

## Define the shared source contract (CA1)

**Depends on.** S0 toolchain slice on `main`.

**Files.**

- [ ] Create `src/Workbench.Core/Activity/Contract/Identity.cs` with `SourceInstanceId`, `SourceGeneration`, `WorkKey`, `ControlReference`, `ProcessIncarnation`, `Association` and `ProjectRef`.
- [ ] Create `src/Workbench.Core/Activity/Contract/Liveness.cs` with `WorkState`, `SourceState`, `ReasonCode` and the projection reducer.
- [ ] Create `src/Workbench.Core/Activity/Contract/Capabilities.cs` with `CapabilityReport`, `EntrypointCoverage` and `ControlCapability`.
- [ ] Create `src/Workbench.Core/Activity/Contract/ControlReceipt.cs` with the receipt stages and transitions.
- [ ] Create `src/Workbench.Core/Activity/Contract/SafeSourceBatch.cs` and `ISourceAdapter.cs`.
- [ ] Create `tests/Workbench.Core.Tests/Activity/Contract/` with one test class per file above, plus `ReplayTests.cs` holding one named test per live-lane replay.
- [ ] Create `tests/Workbench.TestSupport/Canary/CanaryScanner.cs`, a reusable scanner over files, log sinks, captured output and staged diffs. It reads real values at scan time, holds them in memory only, and reports counts and file names.

**Build.**

- [ ] Encode the source contract as types. `WorkState` transitions live in one reducer table in `Liveness.cs`, not in adapter conditionals. `ControlReceipt` allows only the stage edges in the contract.
- [ ] `ControlReference` makes `ConnectionScoped` references unusable after the connection generation changes, by construction.
- [ ] A batch from an older `SourceGeneration` is a no-op in the reducer.
- [ ] Use an injected `TimeProvider` everywhere. No `DateTime.Now`.

**You see.**

- [ ] `dotnet test --filter FullyQualifiedName~Activity.Contract` passes, and the reducer table test prints one line per allowed transition.

**Verify, unit.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] `LivenessReducerTests` covers LV-2, LV-4, LV-5, LV-6 and ID-4 with fixture inputs. Run `dotnet test --filter LivenessReducerTests`.
- [ ] `ControlReceiptTests` covers CT-4, CX-2 and CX-3. Run `dotnet test --filter ControlReceiptTests`.
- [ ] `CapabilityReportTests` covers CX-1, CX-4 and the version-change rule. Run `dotnet test --filter CapabilityReportTests`.
- [ ] `CanaryScannerTests` proves the scanner finds planted fixture and runtime values and never writes them. Run `dotnet test --filter CanaryScannerTests`.

**Verify, live.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked. Ten lanes on `inherit-parent` at the PR head, per the boot recipe. CA1 has no runtime surface, so each lane runs one named test in `ReplayTests.cs` through `control-cli` with `dotnet test --filter` and saves its output.

- [ ] Lane 1. Regression lane against trunk. Run `dotnet test` at trunk and head. Trunk lacks the contract, so record that and gate the full contract suite at head. Save `ca1-regression.txt`. Pass when trunk tests still pass at head and every contract test passes.
- [ ] Lane 2. Replay a Claude start, idle, kill, respawn and stop sequence. Save `ca1-claude-lifecycle.txt`. Pass when states read `Starting`, `Idle`, `Restarting`, `Idle`, `Stopped` and the `WorkKey` never changes.
- [ ] Lane 3. Replay an older-generation batch after a newer one. Save `ca1-late-batch.txt`. Pass when the projection is unchanged.
- [ ] Lane 4. Replay a source disconnect with three live work items. Save `ca1-disconnect.txt`. Pass when all three read `Stale` with `SourceDisconnected` and none reads `Ended`.
- [ ] Lane 5. Replay a PID reused with a new creation time. Save `ca1-pid-reuse.txt`. Pass when the old `WorkKey` stays `Ended` and gains no association.
- [ ] Lane 6. Replay a duplicate `RequestId`. Save `ca1-duplicate-request.txt`. Pass when one receipt and one dispatch exist.
- [ ] Lane 7. Replay a Codex reconnect with a live command reference. Save `ca1-codex-reconnect.txt`. Pass when the reference is rejected as stale.
- [ ] Lane 8. Replay Claude and Codex repeat-control fixtures. Save `ca1-repeat-control.txt`. Pass when a Workbench stopped-target request ends `NoActionNeeded(AlreadyStopped)` with zero dispatch, while provider-only repeat-control fixtures retain native Claude acknowledgement and Codex rejection without attributing a Workbench action.
- [ ] Lane 9. Build a capability report for a version change. Save `ca1-version-change.txt`. Pass when every control reads `Enabled=false` with `VersionChanged` and observation stays enabled.
- [ ] Lane 10. Build the Codex desktop coverage fixture. Save `ca1-codex-desktop.txt`. Pass when desktop reads `Unreachable` with `NoSupportedEndpoint`.

**Verify, perf.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] Metric. Batch-to-projection-ready latency, plus time and allocations to apply one batch of 200 work items to a projection holding 10,000 items.
- [ ] Probe. A BenchmarkDotNet case in `tests/Workbench.Core.Tests/Activity/Contract/ReducerBenchmarks.cs`, run three times interleaved with a 100,000-item projection.
- [ ] Baseline. Trunk has no reducer. Record that, then record the head value at 10,000 items first.
- [ ] Rule. Fail when the 100,000-item time exceeds three times the 10,000-item time, which shows growth with history size. Before this box passes or CA1 becomes merge-ready, select and record measured numeric absolute budgets in issue 9 for reducer time and allocations at both sizes, plus batch-to-projection-ready latency. The scaling ratio is supplementary. Missing budgets block acceptance.

**Review gate.** None. CA1 is not review-gated.

**Merge.**

- [ ] Root's clean verdict at the exact head SHA.
- [ ] Bugbot triage done.
- [ ] Rebase the root onto current trunk, or this child onto its parent branch's exact tip. Preserve the verdict only when `git patch-id` is unchanged. Otherwise obtain fresh review and a verdict at the new head.
- [ ] The operator lands the stack bottom-up under the confirmed execution policy.

## Parse the Claude agent view through an allowlist (CA2)

**Depends on.** CA1.

**Files.**

- [ ] Create `src/Workbench.Host/Sources/Claude/Parsing/AgentViewParser.cs`.
- [ ] Create `src/Workbench.Host/Sources/Claude/Parsing/AgentViewEntry.cs`.
- [ ] Create `tests/fixtures/claude/agent-view/` with synthetic files only. Use `interactive-desktop.json`, `background-idle.json`, `background-starting.json`, `background-restart-gap.json`, `stopped-all.json`, `unknown-enum.json`, `id-prefix-mismatch.json`, `canary.json`, `malformed.json` and `oversized.json`.
- [ ] Create `tests/Workbench.Host.Tests/Sources/Claude/Parsing/AgentViewParserTests.cs` and `CanaryScanTests.cs`.
- [ ] Create `tools/fixtures/claude-synthetic.ps1` with `-Start`, `-Stop`, `-Kill`, `-Remove`, `-List` and `-SettingsHash`. It starts `claude --bg` with no prompt, a per-session `--settings` file, `--setting-sources ""` and `--strict-mcp-config`. It acts only on short IDs it recorded in its own state file under `.local/fixtures/claude/`, and `-Kill` kills only the recorded incarnation of such an ID.

**Build.**

- [ ] `AgentViewParser.Parse(ReadOnlySpan<byte>)` reads with `Utf8JsonReader` and copies only the allowlisted fields in the contract table. `cwd` leaves the parser only as a transient `ProjectHint` value that has no `ToString`, serializer or log formatter.
- [ ] Enforce a maximum input size and a maximum entry count. Return `ParseFailure` with a code and field name. Never include input text in an error or exception.
- [ ] Validate `sessionId` as a UUID and `id` as a prefix of it.

**You see.**

- [ ] `dotnet test --filter AgentViewParserTests` passes, and `canary.json` yields entries with no field holding a canary.

**Verify, unit.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] `AgentViewParserTests` covers ID-1, LV-7, FL-2 and every fixture file. Run `dotnet test --filter AgentViewParserTests`.
- [ ] `CanaryScanTests` covers FL-1 for parser output, exception text and the test log sink. Run `dotnet test --filter CanaryScanTests`.

**Verify, live.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked. Ten lanes on `inherit-parent` at the PR head, per the boot recipe. Lanes 2 to 5 read live bytes through the parser in memory and save only parser output.

- [ ] Lane 1. Regression lane against trunk. Run the CA1 lane 2 replay at trunk and head. Save `ca2-regression.txt`. Pass when both produce the same projection.
- [ ] Lane 2. Pipe the real `claude agents --json --all` output into the parser harness. Save `ca2-real-shape.txt`. Pass when the saved output holds only enum values, counts and `ProjectHint` presence flags, and every entry parses.
- [ ] Lane 3. Start two synthetic background sessions and parse the view. Save `ca2-background.txt`. Pass when both short IDs prefix their session IDs.
- [ ] Lane 4. Parse the view within the first seconds after a synthetic start. Save `ca2-starting.txt`. Pass when the entry parses with no `pid` and no `status` and is not an error.
- [ ] Lane 5. Stop one synthetic session with `-Stop` and parse `--all`. Save `ca2-stopped.txt`. Pass when its entry reads `state=stopped` with no `pid`.
- [ ] Lane 6. Parse `unknown-enum.json`. Save `ca2-unknown-enum.txt`. Pass when the entry reads `Unknown` with `SchemaDrift`.
- [ ] Lane 7. Parse `malformed.json`. Save `ca2-malformed.txt`. Pass when the failure holds a code and no input text.
- [ ] Lane 8. Parse `oversized.json`. Save `ca2-oversized.txt`. Pass when parsing stops at the limit with `OutputTooLarge`.
- [ ] Lane 9. Run `CanaryScanner` over every lane capture and the test log directory. Save `ca2-canary-scan.txt`. Pass when it finds zero canaries.
- [ ] Lane 10. Parse `id-prefix-mismatch.json`. Save `ca2-prefix-mismatch.txt`. Pass when no `ControlReference` exists.

**Verify, perf.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] Metric. Parse time, allocations and input-to-safe-batch latency for a 50-entry agent view.
- [ ] Probe. A BenchmarkDotNet case in `AgentViewParserBenchmarks.cs`, interleaved with a 500-entry input.
- [ ] Baseline. Trunk has no parser. Record that, then record the 50-entry head value first.
- [ ] Rule. Fail when allocations per entry at 500 exceed allocations per entry at 50 by more than 50 percent. Before this box passes or CA2 becomes merge-ready, select and record measured numeric absolute budgets in issue 9 for parse time and allocations at both sizes, plus input-to-safe-batch latency. The allocation ratio is supplementary. Missing budgets block acceptance.

**Review gate.** None. CA2 is not review-gated.

**Merge.**

- [ ] Root's clean verdict at the exact head SHA.
- [ ] Bugbot triage done.
- [ ] Rebase the root onto current trunk, or this child onto its parent branch's exact tip. Preserve the verdict only when `git patch-id` is unchanged. Otherwise obtain fresh review and a verdict at the new head.
- [ ] The operator lands the stack bottom-up under the confirmed execution policy.

## Observe Claude sessions PC-wide with process identity (CA3)

**Depends on.** CA2.

**Files.**

- [ ] Create `src/Workbench.Host/Sources/Claude/Observation/AgentViewPoller.cs`.
- [ ] Create `src/Workbench.Host/Sources/Claude/Observation/ClaudeSourceAdapter.cs` implementing `ISourceAdapter`.
- [ ] Create `src/Workbench.Host/Sources/Claude/Observation/EntrypointClassifier.cs`.
- [ ] Create `src/Workbench.Host/Sources/Windows/ProcessIdentity/ProcessIdentityReader.cs`.
- [ ] Create `src/Workbench.Host/Composition/ClaudeSourceRegistration.cs` to register the adapter with the host's source list.
- [ ] Create `tests/Workbench.Host.Tests/Sources/Claude/Observation/` tests.

**Build.**

- [ ] `AgentViewPoller` runs `claude agents --json --all` with an argument list and no shell, a timeout, a stdout byte limit and discarded stderr. It never passes `--cwd`, `--add-dir`, `--model`, `--agent` or any dispatch option.
- [ ] `ProcessIdentityReader` returns PID, creation time, parent PID and image name for given PIDs. It never reads command lines or environment blocks.
- [ ] `EntrypointClassifier` sets `Desktop` when the parent chain reaches the Claude desktop app image, `Background` for `kind=background`, and `InteractiveCli` only when the chain reaches a known terminal host. Every other interactive entry is `Unknown` with `UnrecognizedLaunchPath`, so IDE, SDK and WSL launches show as gaps. The evidence label is `ParentProcessChain`.
- [ ] `ClaudeSourceAdapter.DescribeAsync` reports the coverage table in Appendix A, bound to the observed CLI version, and lists unmatched Claude processes as a count with `NotInAgentView`.
- [ ] Resolve `ProjectHint` through an `IProjectResolver` interface. Use an in-memory fake until S1 supplies the private mapping.
- [ ] Write an observation conformance receipt for the CLI version after lanes 2 to 8 pass, in private local storage. Until that receipt exists for the current version, `DescribeAsync` reports `Desktop` and `Background` as `Observed`, not `Verified`.

**You see.**

- [ ] `tools/fixtures/claude-synthetic.ps1 -Start 2` followed by the adapter test console prints two `Background` sessions, `Idle`, with distinct `WorkKey`s, and every real session as `Desktop` or `InteractiveCli` with zero controls.

**Verify, unit.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] `AgentViewPollerTests` covers LV-3 and CV-6 with a fake process runner. Run `dotnet test --filter AgentViewPollerTests`.
- [ ] `EntrypointClassifierTests` covers CV-1, CV-2 and CV-8 with synthetic process chains. Run `dotnet test --filter EntrypointClassifierTests`.
- [ ] `ClaudeSourceAdapterTests` covers ID-3, CV-5, CV-7 and RS-3. Run `dotnet test --filter ClaudeSourceAdapterTests`.

**Verify, live.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked. Ten lanes on `inherit-parent` at the PR head, per the boot recipe.

- [ ] Lane 1. Regression lane against trunk. Run the CA2 lane 3 scenario at trunk and head. Save `ca3-regression.txt`. Pass when both parse the same entries and head also projects them.
- [ ] Lane 2. Read real sessions through the adapter only (CV-1). Save `ca3-desktop.txt`. Pass when desktop sessions read `Desktop`, have no `ControlReference` and the capture holds no path, name or session ID.
- [ ] Lane 3. Operator-assisted (CV-2). Ask the operator to start `claude` in a terminal of their choice and say when it is open. Poll through the adapter only. Save `ca3-interactive.txt`. Pass when the new session reads `InteractiveCli` with no controls. If the operator declines, record CV-2 as unverified.
- [ ] Lane 4. Start background sessions in two synthetic projects (CV-3). Save `ca3-two-projects.txt`. Pass when they carry different `ProjectRef` values.
- [ ] Lane 5. Poll during the first seconds after a start (LV-1). Save `ca3-starting.txt`. Pass when the state is `Starting`.
- [ ] Lane 6. Kill one synthetic session process with `-Kill` and poll through respawn (LV-2, ID-2). Save `ca3-respawn.txt`. Pass when the state passes through `Restarting`, the `WorkKey` is unchanged and a new incarnation is linked.
- [ ] Lane 7. Restart the adapter test console with synthetic sessions running (RS-1). Save `ca3-host-restart.txt`. Pass when no duplicate work appears and `SourceGeneration` increments.
- [ ] Lane 8. Point the poller at a missing executable (CV-6). Save `ca3-unavailable.txt`. Pass when the source reads `Unavailable` and coverage names the gap.
- [ ] Lane 9. Read the coverage report. Save `ca3-coverage.txt`. Pass when `Headless`, `Ide`, `Cloud`, WSL, elevated and other configuration roots read `Unverified` with gap reasons, and `Desktop` and `Background` read `Verified` with this run's receipt.
- [ ] Lane 10. Run `CanaryScanner` over all captures and the host log. Save `ca3-canary-scan.txt`. Pass when it finds zero canaries and zero real paths.

**Verify, perf.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] Metric. Wall time, CPU time and peak working set of one poll cycle, including the `claude agents` child process and process identity reads, plus source-change-to-projection latency.
- [ ] Probe. Run 30 poll cycles with 0, 3 and 10 synthetic background sessions, interleaved, and record each value.
- [ ] Baseline. Trunk has no poller. Record that, then record the 0-session head value first.
- [ ] Rule. Record the poll interval used for the run in the capture. It is a run parameter, not a budget. Fail when any poll cycle takes longer than that interval or when CPU time grows faster than linearly in session count. Before this box passes or CA3 becomes merge-ready, select and record measured numeric absolute budgets in issue 9 for poll wall time, CPU and peak working set, plus source-change-to-projection latency. The interval and scaling rules are supplementary. Missing budgets block acceptance.

**Review gate.** None. CA3 is not review-gated.

**Merge.**

- [ ] Root's clean verdict at the exact head SHA.
- [ ] Bugbot triage done.
- [ ] Rebase the root onto current trunk, or this child onto its parent branch's exact tip. Preserve the verdict only when `git patch-id` is unchanged. Otherwise obtain fresh review and a verdict at the new head.
- [ ] The operator lands the stack bottom-up under the confirmed execution policy.

## Accept optional Claude lifecycle hooks (CA4)

**Depends on.** CA3 and the S1 host channel, store and CLI.

**Files.**

- [ ] Create `src/Workbench.Cli/Commands/HookClaudeCommand.cs` for `drone-workbench hook claude`.
- [ ] Create `src/Workbench.Host/Sources/Claude/Hooks/HookIntake.cs`.
- [ ] Create `tests/fixtures/claude/hooks/` with synthetic payloads for `SessionStart`, `SessionEnd`, `Stop`, `Notification`, `SubagentStart`, `SubagentStop`, a canary payload and an unsubscribed `UserPromptSubmit` payload.
- [ ] Create `tests/Workbench.Cli.Tests/HookClaude/` and `tests/Workbench.Host.Tests/Sources/Claude/Hooks/` tests.

**Build.**

- [ ] `drone-workbench hook claude` reads one JSON object from stdin, keeps only the fields in the contract hook table and sends them to the host. It drops every unsubscribed event in the CLI process. It sends `cwd` only as a `ProjectHint` that the host resolves and discards before commit.
- [ ] Confirm each `Docs` row in the contract hook table against a synthetic session in lanes 2 to 4, and mark any field that did not appear as unconfirmed in the coverage report.
- [ ] The verb always exits 0, has a short send timeout and writes nothing to stdout or stderr. A missing host means the event is dropped.
- [ ] `HookIntake` maps events to observations with channel `Hook`. A second `SessionStart` for a known `WorkKey` is a restart signal, not a new session. `SessionEnd` alone never ends work.
- [ ] Coverage reports a per-session hook channel of `Observed` or `NotObserved`. Workbench never reads or writes any settings file to learn whether hooks exist.
- [ ] Do not install a hook. Lanes supply hooks only through a per-session `--settings` file for synthetic sessions.

**You see.**

- [ ] A synthetic session started with the per-session hook file shows hook channel `Observed`, and its `SessionStart` row holds `source=startup` and nothing from `transcript_path` or `session_title`.

**Verify, unit.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] `HookClaudeCommandTests` covers HK-1 and HK-2. Run `dotnet test --filter HookClaudeCommandTests`.
- [ ] `HookIntakeTests` covers HK-3, HK-4, HK-5, HK-6, LV-6 and ID-5 with fixtures. Run `dotnet test --filter HookIntakeTests`.

**Verify, live.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked. Ten lanes on `inherit-parent` at the PR head, per the boot recipe.

- [ ] Lane 1. Regression lane against trunk. Run CA3 lane 4 at trunk and head with no hooks. Save `ca4-regression.txt`. Pass when results match and every session reads hook channel `NotObserved`.
- [ ] Lane 2. Start a synthetic session with the per-session hook file. Save `ca4-session-start.txt`. Pass when one `SessionStart` with `source=startup` is stored.
- [ ] Lane 3. Stop that session by exact short ID. Save `ca4-session-end.txt`. Pass when `SessionEnd` with `reason=other` is stored and state follows the poll.
- [ ] Lane 4. Kill a hooked synthetic session externally (HK-3). Save `ca4-kill.txt`. Pass when no `SessionEnd` arrives and the second `SessionStart` maps to the same `WorkKey`.
- [ ] Lane 5. Stop the host, then start a hooked synthetic session (HK-2). Save `ca4-host-absent.txt`. Pass when the session starts normally and nothing is stored.
- [ ] Lane 6. Pipe the canary payload into the verb (HK-1). Save `ca4-canary.txt`. Pass when `CanaryScanner` finds zero canaries in the store, logs and live messages.
- [ ] Lane 7. Pipe the `UserPromptSubmit` fixture into the verb. Save `ca4-unsubscribed.txt`. Pass when nothing reaches the host.
- [ ] Lane 8. Pipe a `Notification` fixture with `permission_prompt` (HK-5). Save `ca4-waiting.txt`. Pass when state reads `Waiting` and no `message` or `title` is stored.
- [ ] Lane 9. Pipe subagent start and stop fixtures (ID-5 at fixture level). Save `ca4-subagent.txt`. Pass when a separate subagent `WorkKey` with `ParentKey` appears and session counts are unchanged. Record that the live `M` form of ID-5 stays blocked.
- [ ] Lane 10. Confirm no settings changed. Save `ca4-settings-hash.txt`. Pass when hashes of user, project and local Claude settings files match their values before the lanes ran.

**Verify, perf.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] Metric. Hook process wall time, CPU and peak working set with the host present and absent, plus event-to-host-visible-state latency when present.
- [ ] Probe. Run the verb 50 times per case with the `SessionStart` fixture, interleaved.
- [ ] Baseline. Trunk has no verb. Record that, then record the host-present value first.
- [ ] Rule. Before this box passes or CA4 becomes merge-ready, select and record measured numeric absolute budgets in issue 9 for hook wall time, CPU and peak working set in both cases, plus event-to-host-visible-state latency. Missing budgets block acceptance. Record a numeric send timeout within those budgets. An unspecified timeout plus start time is not an acceptance gate.

**Review gate.** None. CA4 is not review-gated.

**Merge.**

- [ ] Root's clean verdict at the exact head SHA.
- [ ] Bugbot triage done.
- [ ] Rebase the root onto current trunk, or this child onto its parent branch's exact tip. Preserve the verdict only when `git patch-id` is unchanged. Otherwise obtain fresh review and a verdict at the new head.
- [ ] The operator lands the stack bottom-up under the confirmed execution policy.

## Gate Claude controls and reconcile verified outcomes (CA5)

**Depends on.** CA3 and the S1 store.

**Files.**

- [ ] Create `src/Workbench.Core/Activity/Control/ControlGate.cs`.
- [ ] Create `src/Workbench.Host/Sources/Claude/Control/BackgroundStopRoute.cs`.
- [ ] Create `src/Workbench.Host/Sources/Claude/Control/StopOutcomeVerifier.cs`.
- [ ] Create `tests/Workbench.Host.Tests/Sources/Claude/Control/` tests.

**Build.**

- [ ] `ControlGate` authenticates the caller and first returns the existing authorized receipt for a duplicate `RequestId`. For a new request it validates authenticated operator scope, exact `WorkKey`, ownership, generation and fresh evidence. A confirmed stopped target with no live incarnation ends `Requested -> NoActionNeeded(AlreadyStopped)`. Missing, stale or contradictory evidence refuses or remains unknown. This path needs no live handle and creates no dispatch stages.
- [ ] For dispatch, recheck identity, freshness, incarnation, reference scope, capability and required state. Require a supported source route that atomically binds exact work, source generation, incarnation and state. `BackgroundStopRoute` refuses the current `claude stop <short id>` route with `NoConditionalDispatch`. Never invoke it through Workbench. Handles, rechecks and post-dispatch acknowledgement cannot prevent its replacement race.
- [ ] Keep `StopBackgroundSession` at `Enabled=false` with empty `VerifiedFor` and the gap `NoConditionalDispatch`. Version changes disable with `VersionChanged`. Source restarts invalidate generation-bound authorization. Neither event enables the route. Real dispatch remains blocked until an actually supported conditional route has version-specific conformance proof, or a separately authorized future policy change changes authority.
- [ ] Implement dispatch receipt transitions and `StopOutcomeVerifier` through a controlled-fake conditional route only. The fake atomically rejects changed incarnation, generation or required state. After a fixture dispatch, reconcile source outcome separately from per-incarnation `ProcessExit`, including timeout, contradiction and restart without replay. Record the verification window on each receipt. Fake proof is contract evidence, never provider conformance.
- [ ] Never call `claude rm`, `kill`, `respawn`, `attach`, `logs` or `daemon stop` from a control route. Never terminate a process directly. Native fixture preparation and cleanup remain separate from Workbench control.

**You see.**

- [ ] A current-CLI idle-target request shows `Refused(NoConditionalDispatch)` and zero dispatch. Fresh stopped evidence shows terminal `NoActionNeeded(AlreadyStopped)`, never a claim that Workbench stopped it.

**Verify, unit.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] `ControlGateTests` covers CT-1 through CT-4, CT-6 through CT-8 and CT-10 through CT-12, including missing or stale stopped evidence, replay and a later restart. Run `dotnet test --filter ControlGateTests`.
- [ ] `BackgroundStopRouteTests` covers CT-5, CT-9 and RS-2 through the controlled-fake conditional route, plus atomic rejection when generation, incarnation or `Idle` changes at dispatch. Assert zero current-CLI dispatch. Run `dotnet test --filter BackgroundStopRouteTests`.

**Verify, live.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked. Ten lanes on `inherit-parent` at the PR head, per the boot recipe. Current-CLI lanes observe synthetic targets and exercise refusals or no-ops. Conditional dispatch lanes drive named fixture replays through `control-cli` and are not provider conformance.

- [ ] Lane 1. Regression lane against trunk. Run CA3 lane 4 at trunk and head. Save `ca5-regression.txt`. Pass when observation matches and head reports background stop disabled with `NoConditionalDispatch`.
- [ ] Lane 2. Request a stop on an idle synthetic session as the operator caller (CT-1). Save `ca5-stop-disabled.txt`. Pass when `Refused(NoConditionalDispatch)` occurs, zero commands dispatch and its incarnation stays alive.
- [ ] Lane 3. Start two sessions and request one stop (CT-2). Run the conditional-fixture exact-target replay too. Save `ca5-exact-target.txt`. Pass when the current CLI dispatches nothing and the fake cannot affect a replacement or the other work.
- [ ] Lane 4. Prepare a stopped synthetic target with the native fixture, then submit a new request (CT-3). Save `ca5-already-stopped.txt`. Pass when fresh stopped evidence yields only terminal `NoActionNeeded(AlreadyStopped)` with zero dispatch and no Workbench-stop attribution.
- [ ] Lane 5. Submit the same `RequestId` twice for refusal and no-op cases (CT-4). Save `ca5-same-request.txt`. Pass when each returns its existing receipt and total dispatch remains zero.
- [ ] Lane 6. Replay a changed incarnation and a changed required state at the fake route's atomic dispatch boundary (CT-10). Save `ca5-incarnation.txt`. Pass when neither causes a side effect. Also verify current-CLI requests remain disabled.
- [ ] Lane 7. Request a stop on a real desktop session through `IActivity.RequestControlAsync` as the operator caller (CT-6). Save `ca5-desktop-refused.txt`. Pass when `Refused(NoSupportedRoute)` occurs and no control child process starts.
- [ ] Lane 8. Request a stop by partial name, as a drone caller, and without ownership evidence (CT-7, CT-11, CT-12). Save `ca5-gates.txt`. Pass when each refuses before dispatch.
- [ ] Lane 9. Restart the host with a controlled-fake receipt in `Dispatched` (RS-2). Save `ca5-reconcile.txt`. Pass when reconciliation records observed outcome and separate exit evidence without a second dispatch. Source restart and version change must not enable current Claude controls.
- [ ] Lane 10. Report the busy-stop gap (CT-8), then replay stale and contradictory stopped evidence. Save `ca5-busy-gap.txt`. Pass when no current-CLI condition enables stop, invalid evidence never produces `AlreadyStopped`, and a later restart needs a new fully gated request.

**Verify, perf.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] Metric. Gate processing latency, CPU time and allocations for refusals and already-stopped no-ops, plus request-to-visible-receipt latency. Measure fake conditional reconciliation separately as contract timing only.
- [ ] Probe. Run 50 requests per refusal and no-op case, interleaved with equal observation-only cycles and fixture reconciliation cases. Run no real stops.
- [ ] Baseline. Trunk has no gate. Record that, then record observation-only CPU and memory first. Do not compare fake timing with native stop research.
- [ ] Rule. Before this box passes or CA5 becomes merge-ready, select and record measured numeric absolute budgets in issue 9 for gate latency, CPU, allocations and request-to-visible-receipt latency. Missing budgets block acceptance. Fixture reconciliation needs its own numeric timing budget and never proves provider performance. An actual conditional source route remains blocked until supported and conformed, with measured dispatch-to-visible-outcome budgets set before its performance acceptance. An unspecified timeout is not a budget.

**Review gate.** None. CA5 is not review-gated. The dashboard control is exposed in S4 only after its dispatch requirements are met.

**Merge.**

- [ ] Root's clean verdict at the exact head SHA.
- [ ] Bugbot triage done.
- [ ] Rebase the root onto current trunk, or this child onto its parent branch's exact tip. Preserve the verdict only when `git patch-id` is unchanged. Otherwise obtain fresh review and a verdict at the new head.
- [ ] The operator lands the stack bottom-up under the confirmed execution policy.

## Publish summaries and show Claude coverage (CA6)

**Depends on.** CA4, CA5 and the S1 dashboard.

**Files.**

- [ ] Create `src/Workbench.Core/Activity/Publications/PublicationSchema.cs` and `PublicationAdmission.cs`.
- [ ] Edit `src/Workbench.Cli/Commands/ReportCommand.cs` to accept the schema-1 object.
- [ ] Edit `src/Workbench.Cli/Commands/SourcesCommand.cs` to print the capability report.
- [ ] Create `src/Workbench.Host/Views/Coverage/ClaudeCoverageView.cs`.
- [ ] Create `src/Workbench.Host/Dashboard/Coverage/ClaudeSourcePanel.razor`.
- [ ] Create matching tests under `tests/`.

**Build.**

- [ ] `PublicationAdmission` rejects any unknown property, links a claimed `sessionId` only within the same `ProjectRef` and never changes state, ownership or capability.
- [ ] `drone-workbench sources --json` prints per-entrypoint coverage, version, hook channel counts, control capability and gap reasons.
- [ ] The Coverage panel shows the same data, with gaps shown as rows, and counts sessions, subagents and processes separately. Background stop stays disabled with `NoConditionalDispatch`. Receipt views show `NoActionNeeded(AlreadyStopped)` as a timestamped observation, with no dispatch stages or Workbench-stop attribution.

**You see.**

- [ ] The Coverage page lists Claude `Desktop`, `InteractiveCli`, `Background`, `Headless`, `Ide`, `Cloud` and `Unknown` rows, each with a level, evidence and gap reason.

**Verify, unit.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] `PublicationAdmissionTests` covers SM-1, SM-2, SM-3 and SM-4. Run `dotnet test --filter PublicationAdmissionTests`.
- [ ] `ClaudeCoverageViewTests` covers CV-7. Run `dotnet test --filter ClaudeCoverageViewTests`.

**Verify, live.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked. Ten lanes on `inherit-parent` at the PR head, per the boot recipe.

- [ ] Lane 1. Regression lane against trunk. Open the S1 dashboard at trunk and head. Trunk lacks the Claude panel, so record that and gate the panel plus an unchanged Overview at head. Save `ca6-regression.png`. Pass when Overview counts match between trunk and head.
- [ ] Lane 2. Open the Coverage page with two synthetic sessions running. Save `ca6-coverage.png`. Pass when both appear under `Background` with stop disabled and `NoConditionalDispatch` visible.
- [ ] Lane 3. Read the Coverage page for real desktop sessions. Save `ca6-desktop.png`. Pass when they show `Desktop`, no stop control and the reason `NoSupportedRoute`.
- [ ] Lane 4. Run `drone-workbench sources --json`. Save `ca6-sources.json`. Pass when it matches the Coverage page row for row.
- [ ] Lane 5. Publish a valid summary claiming a synthetic session (SM-1). Save `ca6-summary.png`. Pass when the Work view shows it on that session.
- [ ] Lane 6. Publish a summary with an unknown canary property (SM-2). Save `ca6-rejected.png`. Pass when it is rejected and `CanaryScanner` finds nothing.
- [ ] Lane 7. Publish a summary claiming a session in another project (SM-3). Save `ca6-unassociated.png`. Pass when it shows unassociated.
- [ ] Lane 8. Disconnect the source by hiding the CLI from the poller. Save `ca6-disconnected.png`. Pass when the panel shows `Disconnected` and work shows `Stale`, not empty.
- [ ] Lane 9. Close and reopen the browser during polling. Save `ca6-browser-reopen.png`. Pass when collection continued and the page shows current state.
- [ ] Lane 10. Run `CanaryScanner` over the database file, logs and saved pages. Save `ca6-canary-scan.txt`. Pass when it finds zero canaries and zero real paths.

**Verify, perf.** Tests alone are not sufficient verification. A PR is verified only when its unit, live, and perf boxes are all checked.

- [ ] Metric. Coverage page render time, update-to-visible-state latency, host CPU time and peak working set with 10 synthetic sessions.
- [ ] Probe. Load the page 20 times at trunk and head, interleaved, through `control-ui`.
- [ ] Baseline. Record the trunk Overview render time first.
- [ ] Rule. Fail when the head Overview render time exceeds trunk by more than 20 percent. Before this box passes or CA6 becomes merge-ready, select and record measured numeric absolute budgets in issue 9 for added Coverage rendering, update-to-visible-state latency, host CPU and peak working set. The Overview ratio is supplementary. Missing budgets block acceptance.

**Review gate.** The operator reviews before merge.

- [ ] Copy lane 2, 3 and 8 screenshots into `.local/review/ca6-review-coverage.png`, `.local/review/ca6-review-desktop.png` and `.local/review/ca6-review-disconnected.png`. Blur real session rows before sharing.
- [ ] Record a 30 to 60 second video of the Coverage page with synthetic sessions only. Save it as `.local/review/ca6-review.mp4`.
- [ ] Post the screenshots and the video in chat. Stop at merge-ready. Wait for the operator's click.

**Merge.**

- [ ] Root's clean verdict at the exact head SHA.
- [ ] Bugbot triage done.
- [ ] Rebase the root onto current trunk, or this child onto its parent branch's exact tip. Preserve the verdict only when `git patch-id` is unchanged. Otherwise obtain fresh review and a verdict at the new head.
- [ ] The operator lands the stack bottom-up under the confirmed execution policy.

## Close the program

- [ ] Every box above is checked with its evidence.
- [ ] Every `M` scenario in the acceptance file is listed as blocked or passed, with its reason.
- [ ] Post a sanitized summary to the ticket 7 issue. Keep ticket 7 open unless the operator rules otherwise.
- [ ] Reply to the operator with the report the execution playbook names.

## Appendix A. Prototype evidence

The plan relies on the recorded ticket 7 runs and one new read-only check. No synthetic session was started and nothing was controlled for this plan.

| Question | Evidence | Status |
| --- | --- | --- |
| Can a standalone process see desktop and external Claude sessions | `claude agents --json` listed desktop sessions as `interactive`, matched to app child processes by PID. [Claude results](../research/claude-ticket7-results.md), commit `320b2e2` | Observed |
| Is the agent-view schema unchanged on 2026-11-12 | A read of `claude agents --json --all` on CLI 2.1.312, filtered in memory to key names and enum values, returned keys `cwd, kind, name, pid, sessionId, startedAt, status`, kind `interactive` and statuses `busy` and `idle`. No receipt was kept. `busy` is new relative to ticket 7 and matches the docs. CA2 lane 2 repeats the check | Observed, read-only, unreceipted |
| What does native background stop report | Wrong ID rejected, correct stop exited the session and PTY host, repeat stop succeeded | Native research on idle synthetic sessions only. No atomic incarnation or state precondition established |
| Does a killed session respawn | Same short ID, new PID within about 15 s, `state` stale in the gap | Observed |
| Do hooks see crashes | No `SessionEnd` on external kill, second `SessionStart` with `startup` | Observed |
| Documented `status`, `state`, `waitingFor`, hook events and payload fields | Official [agent view](https://code.claude.com/docs/en/agent-view.md), [hooks](https://code.claude.com/docs/en/hooks.md) and [CLI reference](https://code.claude.com/docs/en/cli-reference.md) pages, read through an AI-summarized fetch | Docs only. CA2 and CA4 fixtures must be checked against a live synthetic run |
| `claude rm` deletes the worktree | Installed `claude rm --help` on 2.1.312 | Observed help text |
| Can Codex desktop be reached | No. Private stdio, no endpoint. [Diagnosis](../research/codex-connection-diagnosis.md), commit `b60523f` | Gap preserved |

Claude coverage that CA3 reports at start.

| Entrypoint | Observation | Controls | Gap reason |
| --- | --- | --- | --- |
| `Desktop` | Observed. `Verified` after CA3 conformance | None | `NoSupportedRoute` |
| `InteractiveCli` | Unverified until CV-2 passes | None | `NoSupportedRoute` |
| `Background` | Observed. `Verified` after CA3 observation conformance | `StopBackgroundSession` disabled | `NoConditionalDispatch`. Busy stop also unverified |
| `Headless` | Unverified | None | Needs model-turn authorization |
| `Ide` | Unverified | None | No extension found in ticket 7 |
| `Cloud` | Unverified | None | Not inventoried |
| `Unknown` | Reported per session | None | `UnrecognizedLaunchPath` |

Unproven. Busy-session stop, descendant exit under load, subagents in the live view, headless and SDK runs, desktop app restart, other configuration roots, WSL and elevated sessions.

## Appendix B. Alternatives rejected

- **Hooks as the primary channel.** Hooks miss crashes, need settings changes, and carry prompt and transcript fields. Polling is primary and hooks only add detail.
- **Reading transcripts or `~/.claude` session files.** That reads private content and breaks the no-transcript rule.
- **Process kill as a stop route.** The supervisor respawns killed sessions, and kill bypasses the source's own outcome.
- **`claude rm` as a control.** It deletes the session worktree. Worktree removal belongs to Hygiene's approve-and-run flow.
- **`claude daemon stop --any`.** It stops every background session at once.
- **`claude logs` or `attach` for status.** They expose content and accept partial names.
- **Separate contracts per source.** Claude and Codex differ in control semantics but share identity, freshness and receipt rules. One contract with typed capability differences keeps Activity and views source-neutral.
- **Automatic conformance runs after a CLI upgrade.** That would start background sessions without the operator. The default disables controls until the operator runs the fixture.

## Appendix C. Risks

| Risk | PR | What the owner watches |
| --- | --- | --- |
| A CLI update changes field names or enums | CA2, CA3 | `SchemaDrift` counts and the version-change capability rule |
| Real session metadata leaks into fixtures or captures | Every PR | `CanaryScanner` and the pre-commit diff scan. Captures stay under `.local/` |
| Parent-chain classification fails under another launch path | CA3 | `Unknown` entrypoint counts in coverage |
| A synthetic fixture leaks a session | CA3, CA5 | The fixture state file and `-List` at lane end |
| A stop races a replacement or a state change | CA5 | Current CLI dispatch stays disabled. A future route must atomically enforce incarnation, generation and state before any side effect |
| CA1 and CA2 have no runtime surface, so their live lanes replay fixtures | CA1, CA2 | Lane captures name the fixture source |
| S0 and S1 are not landed | All | The arm checklist stops the program |
| The cross-family review panel is unavailable in Codex | All | The verdict records skipped roles |

## Appendix D. Links and reading list

- [Source contract](source-contract.md), [acceptance scenarios](claude-adapter-acceptance.md), [build plan](build-plan.md), [architecture rationale](architecture-rationale.md) and [acceptance matrix](acceptance-matrix.md).
- [Combined ticket 7 assessment](../research/ticket7-combined-assessment.md) and the receipts beside it.
- CA1 and CA5 get `pstack/skills/how/SKILL.md` and `pstack/skills/interrogate/SKILL.md`, because they define identity and control authority. Record interrogate as partially run when only inherited-model helpers exist.
- Keep the decision trail per `pstack/skills/show-me-your-work/SKILL.md` under `.local/trail/claude-adapter/`.
- Held for the operator. Hook enablement scope, summary distribution to Claude sessions, model-turn authorization for `M` scenarios, the operator conformance command after a CLI upgrade, and whether drone callers may ever request stops. The plan's default is dashboard operator only.

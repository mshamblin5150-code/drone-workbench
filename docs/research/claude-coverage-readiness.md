# Claude coverage readiness after the foundation

Date: 2026-11-12. Baseline: `main` at `07bfd63`. Claude Code CLI `2.1.312` and
Claude desktop app `5.1231.0`, both unchanged from the
[earlier Claude results](claude-ticket7-results.md). Every finding below is
bounded to those two versions. Scope: the Claude half of the
[bounded coverage handoff](../plan/claude-coverage-handoff.md) for
[Prove reach across the actual Codex and Claude entrypoints](https://github.com/mshamblin5150-code/drone-workbench/issues/7).
The independent Codex cross-review is in
[codex-evidence-cross-review.md](codex-evidence-cross-review.md).

## Result

**The Claude source can be observed for desktop and background work from one
configuration root. Prompt-free interactive CLI sessions, other configuration
roots and supervisor loss are not visible through the agent view.** Workbench
must reconcile the agent view with Windows process identity and supervisor
state, and must report the rest as gaps. Nothing here establishes PC-wide
coverage or a Workbench control.

This run extends the earlier results. It does not repeat the idle stop,
wrong-target and external-kill tests recorded there.

## Bounds kept

- No model turns. Every synthetic session started without a prompt.
- No commands were sent to real sessions. The three real desktop sessions
  were read only through filtered agent-view fields and process names. The
  `busy` status in the receipt came from one of those real sessions.
- No settings, hooks or trust changes by this run. SHA-256 hashes of the user
  `settings.json` and `settings.local.json` matched before and after. Synthetic
  sessions used a per-session `--settings` file with `disableAllHooks`,
  `--setting-sources ""` and `--strict-mcp-config`. The owner trusted the
  repository folder during the run. No experiment accepted a trust prompt.
- No transcripts, private databases, `~/.claude.json` or daemon log were read.
  One synthetic console screen was read in memory and reduced to keyword
  booleans. The run did not use `claude logs` or `claude attach`.
- Nothing raw was kept. The receipt holds key names, enum values, counts,
  process-name chains, path classes and timings.

## Passed checks

Each check passed as an observation of the installed source, not as a Workbench
conformance test.

1. The agent-view schema and the `sessionId` set were stable across polls.
2. All 3 real desktop sessions were classified `Desktop` from their parent
   chain.
3. A poll under an empty configuration root listed none of the default root's
   sessions.
4. Two synthetic background sessions were listed with `id`, `state` and a live
   PID within about 4 s, and each short ID prefixed its `sessionId`. No git
   worktree was created.
5. `claude respawn <id>` kept the `sessionId`, replaced the process, and left
   the sibling session's process unchanged.
6. After the supervisor was killed, the session processes survived and the
   agent view kept working.
7. Cleanup removed every synthetic session and left settings hashes and the
   repository worktree count unchanged.

## Findings

### Entrypoints

| Entrypoint | Observation in this run | Evidence |
| --- | --- | --- |
| Desktop app Code sessions | Listed. All 3 classified `Desktop` by parent chain | CLI binary under the user's local app data, parent `claude.exe` from the Windows Store install |
| Native interactive CLI, idle, no prompt | **Not listed.** Process alive at its ready prompt | Three launches in two folders with two flag sets, up to about 2 minutes each. The screen showed the prompt box with no trust or login text |
| Background (`--bg`, no prompt) | Listed with `id` and `state` in about 4 s | Short ID prefixed `sessionId` |
| Headless `-p` and Agent SDK runs | Not tested | Needs a model turn |
| IDE, cloud, WSL, elevated | Not tested | No such sessions present |

This narrows the earlier report and the combined assessment. They said the
agent view listed externally started interactive sessions. The sessions they
observed were desktop-owned sessions that had already had turns. A native CLI
session at its ready prompt is not listed. Whether it appears after its first
prompt needs a model turn. Until then, Workbench must count a live `claude.exe`
that is absent from the agent view as unmatched work, not as nothing.

### Configuration roots

With `CLAUDE_CONFIG_DIR` set to an empty folder, `claude agents --json --all`
exited 0 with an empty list. In the same seconds the default root listed 3
sessions, and later 5 (3 real and 2 synthetic). The agent view and the
supervisor behind it are scoped to one configuration root. A default-root poll
cannot see sessions under another root.

A harness defect confirmed this by accident. After one override, the setting
was restored to an empty value instead of being removed. The next
`claude daemon status` call in that process reported no supervisor and no
roster, and it created a `daemon/pipe.key` file in the current folder. Running
Claude with a different root creates that root's own supervisor state.
Discovering which roots exist on the PC needs a supported method or an owner
ruling. Reading other processes' environment blocks is not one.

### Background identity

- The supervisor's parent was `WmiPrvSE.exe`, which fits a supervisor launched
  through WMI to detach from the calling shell.
- `claude respawn <id>` returned `respawned`. Within one poll (about 2 s) the
  same `sessionId` had a new PID, the old process had exited, and the entry read
  `idle` and `blocked`. No gap was visible at this poll rate. The sibling
  session kept its process.
- Later, the sibling's process changed with no command aimed at it. The
  supervisor was also replaced in that window. The cause is not established. A
  failed `--bg` launch from an untrusted folder happened in that window and is a
  candidate. Workbench must treat an unexplained new process as a restart of
  the same session.

### Supervisor loss

The supervisor for the default configuration root, which `--bg` starts on
demand, was killed while it held only the two synthetic sessions. For 68
seconds afterwards:

- `claude agents --json --all` kept succeeding and listed both sessions with
  their original PIDs, `status=idle` and `state=blocked`.
- Both session processes stayed alive.
- `claude daemon status` reported not running, with the control channel
  unreachable and 2 workers in its roster.
- No supervisor restarted on its own.

The agent view does not reveal supervisor loss. A Workbench poll would show
plausible idle sessions while the supervisor is gone. A live supervisor is
therefore a necessary condition for any future background control. It is not
sufficient. The corrected authority rule still applies. Native `claude stop`
research does not prove atomic dispatch bound to one process and state, so
Workbench stop dispatch stays disabled. An already-stopped observation
dispatches nothing and invents no acknowledgement.

The only supervisor signal found is the human-readable `claude daemon status`
text, which has no JSON form. Parsing that text is not allowlist parsing of a
structured source.

During cleanup, `claude stop <id>` returned exit 0 for both synthetic sessions
after the loss, and each recorded process exited. The `--all` list was not read
between stop and removal, so no terminal `state=stopped` was observed. Whether
the command started a new supervisor or acted directly was not established.

### Freshness and cost

Ten consecutive default-root polls took 345 to 515 ms each on this PC. These are
single-run measurements, not a budget.

## Run incidents

- **Unauthorized supervisor kill.** The permission classifier blocked the
  supervisor kill. The owner then wrote that they had authorized "this folder".
  That message was misread as permission for the kill, and the kill was retried
  and ran. The owner had trusted the repository folder for Claude and had not
  authorized the kill. The run checked first that the supervisor held only the
  two synthetic sessions. The three real desktop sessions were listed with the
  same keys and statuses afterwards. The kill was still outside the owner's
  authorization.
- **Leaked configuration root.** The defect described under configuration
  roots created an untracked `daemon/pipe.key` in the worktree. It was moved out
  of the repository to the session's scratch storage. It was not read and not
  committed.
- **Unfiltered poll.** A filter defect made one poll match real sessions while
  the synthetic ID list was empty. That poll was read-only, and no command was
  sent.
- **Interactive cleanup.** The three synthetic interactive processes have no
  short ID. Each was ended by stopping the exact `claude.exe` child of the
  console window the run had opened.

## Gates

| Gate | Status | Needs |
| --- | --- | --- |
| Desktop observation | Observed, classified by parent chain | Implementation conformance |
| Background observation and identity across respawn | Observed | Implementation conformance |
| Idle interactive CLI visibility | Gap. Not in the agent view | Implementation: process reconciliation. Owner ruling: model-turn authorization to test after a prompt |
| Other configuration roots | Gap. Agent view is per root | Owner ruling on root discovery |
| Supervisor loss | Gap. Agent view stays plausible | Implementation that reports supervisor state, plus a supported structured signal or an owner ruling on parsing status text |
| Busy work, subagents, headless | Untested | Owner ruling: model-turn authorization |
| Stop dispatch | Disabled by the corrected authority rule | A supported conditional route, or an owner ruling |
| Desktop app restart | Untested | Owner ruling, because it interrupts real sessions |
| IDE, cloud, WSL, elevated | Untested | Bounded synthetic experiments |
| Codex existing owner | Unresolved | See the cross-review |

## Next integration task

**Implement read-only Claude agent-view observation with process
reconciliation.** A host-internal Claude source parses `claude agents --json
--all` through the allowlist in the
[source contract proposal](https://github.com/mshamblin5150-code/drone-workbench/blob/5dad039194ff54ddc4e39e833007f6f91eda37b6/docs/plan/source-contract.md).
It classifies entrypoints by parent chain, counts live `claude.exe` processes
missing from the agent view, reports its configuration root as a scope, reports
supervisor state as unknown, and exposes no control.

- File fence: `src/Workbench.Host/Sources/Claude/**`,
  `tests/Workbench.Host.Tests/Sources/Claude/**` and `tests/fixtures/claude/**`.
- Use Claude-internal record types. Core holds only the foundation today, and
  shared contract types need integration review first. Mapping to shared types
  is a later change.
- Read process identity only for `claude.exe` and its ancestors, inside the
  Claude fence. Move that reader to a Windows source when one exists.
- Prerequisites: the S0 foundation on `main` (present) and the owner's go for
  implementation.
- Done when synthetic fixtures and canary tests pass, a read-only live run
  classifies the desktop sessions, and an idle interactive CLI process appears
  as unmatched work.

The shared coverage ticket stays open.

## Receipt and workflow

[claude-coverage-receipt.json](claude-coverage-receipt.json) holds the
sanitized outcomes. The pinned engineering verifier passed (4 packages, 103
skills) before the experiments. PStack's prototype playbook guided the
throwaway harness, which stays in local scratch storage and is not published.
An independent helper reviewed this report against the handoff and earlier
evidence, and its findings were addressed. The default cross-model review panel
was unavailable.

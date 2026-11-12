# Claude observation loop verification

Date: 2026-11-12. Windows x64, 22 logical processors, Claude Code CLI
`2.1.322`. Scope: the [host-owned observation loop](../plan/claude-observation-loop.md).
These are single-PC measurements. They are evidence for
[Set collector restart behavior and resource budgets](https://github.com/mshamblin5150-code/drone-workbench/issues/9),
not budgets.

## Checks

| Check | Result |
| --- | --- |
| Pinned engineering stack | Verifier passed for 4 packages and 103 skills |
| Baseline on unmodified `main` | `tools/verify-dotnet.ps1` passed before any change |
| Full verification on this branch | `tools/verify-dotnet.ps1` passed. 0 warnings. 105 tests passed (Core 10, Supervisor 4, CLI 14, Host 77). Direct VSTest passed 14 CLI tests. Windows x64 publish passed |
| Live read through the owner-checked CLI | Exit 0, `Fresh`, three sessions, supervisor `Unknown`, exact unmatched count `null` |
| Invalid `DRONE_WORKBENCH_INSTANCE` | CLI exit 2 |
| Ordinary tests | Synthetic only. Host processes run with an empty `PATH`, so the Claude source reports `MissingExecutable` and no real Claude command runs |
| Live loop, read-only | 15 polls at 15 s against the installed CLI. Every snapshot read `Fresh`. Three desktop sessions, supervisor `Unknown`, controls unavailable |
| Unknown counts in the live snapshot | Process inventory incomplete. Known unmatched lower bound 11, exact unmatched count `null`, gap `ProcessInventoryIncomplete` reported |
| Second Host for the same user | Refused with exit 1 and one stderr line, before Kestrel or a capture started |
| CLI after the Host stopped | Exit 3, `hostReachable: false`, reason `NoHostListening` |

The live counts are one observation. Unmatched processes can include UI,
supervisor and helper processes.

## Poll cost

One poll runs `claude --version`, then `claude agents --json --all`, then parses
the result and reads Windows process identity in the Host.
`tools/measure-claude-polling.ps1` measured the child commands and a one-shot
Host capture 20 times, interleaved, with 0 errors and 3 listed entries per run.
The live loop measured the Host's own CPU over 15 warm polls.

| Part | CPU per poll | Runs | Range |
| --- | --- | --- | --- |
| `claude agents --json --all` | 351.6 ms median | 20 | 281.2 to 453.1 ms |
| `claude --version` | 31.2 ms median | 20 | 15.6 to 156.2 ms |
| Host in the warm loop | 161.5 ms mean | 15 polls | Not sampled per poll |
| Total | about 544 ms | | |

The agent listing is the limiter at about 65 percent of a poll. It is not
process startup, because `--version` starts the same executable and costs a
tenth as much. Its internal cause is inside the Claude CLI and was not
profiled. The Host's share probably comes from starting two child processes and
reading the process inventory. That share was not profiled either.

Wall time for the agent listing was 393.1 ms median. Windows reports process CPU
in 15.6 ms steps. Machine CPU was a median 50 percent during the 20 runs because
other sessions were active, so the spread is wider than on an idle PC. Child CPU
counts each `claude.exe` process only, not any process it starts.

## Interval candidates

| Interval | Share of one core | Share of this PC |
| --- | --- | --- |
| 5 s | 10.9 % | 0.49 % |
| 10 s | 5.4 % | 0.25 % |
| 15 s | 3.6 % | 0.16 % |
| 30 s | 1.8 % | 0.08 % |
| 60 s | 0.9 % | 0.04 % |

The Host's working set stayed between 58 and 64 MB over the 15 live polls, with
no upward trend after the first poll.

## Proposed settings

- **Poll interval 15 s.** It costs about 0.16 percent of this PC and about 3.6
  percent of one core. A change in session state shows up within one interval.
  The owner's responsiveness-first ruling favors 30 s if a 15 s delay is
  acceptable to trade for half the cost.
- **Stale after 45 s.** That is three intervals. A slow poll that waits out both
  10 s command timeouts still finishes within 45 s of the previous success, so it
  does not flip the snapshot to stale on its own. A failed poll marks the
  snapshot stale immediately with `LatestAttemptFailed`, regardless of age.

These values are constants in `ObservationSettings`. Changing them needs a
ruling on ticket 9, not a code redesign.

## Not covered

- Idle-PC measurements and other hardware.
- CPU of processes that the Claude CLI starts.
- A long soak run, sleep and resume, and source restarts during a live poll.
- An elevated CLI reading a non-elevated Host. The owner check targets this
  case, but no elevated run was made.
- A pipe owned by another Windows user. That needs a second account.

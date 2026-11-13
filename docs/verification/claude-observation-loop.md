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
| Full verification on this branch | `tools/verify-dotnet.ps1` passed. 0 warnings. 106 tests passed (Core 10, Supervisor 4, CLI 15, Host 77). Direct VSTest passed 15 CLI tests. Windows x64 publish passed |
| Live read through the owner-checked CLI | Exit 0, `Fresh`, three sessions, supervisor `Unknown`, exact unmatched count `null` |
| Invalid `DRONE_WORKBENCH_INSTANCE` | CLI exit 2 |
| Client impersonation level | A synthetic server's `RunAsClient` saw `Impersonation` before the fix and sees `Identification` after it. The regression test is committed ahead of the fix |
| Measurement guards | `tools/test-measure-claude-polling.ps1` passes 12 synthetic checks. They cover a timeout with a live parent and grandchild, a parent that exits while its child holds stdout, over-cap output, child environment removal, relative and empty configuration roots, and zero or negative intervals. The exited-parent case failed before the runner placed each process in a Job Object, with the child left running |
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
Host capture 20 times, interleaved. All 20 samples completed with no failures,
on the default configuration root, with 4 listed entries per run. The live loop
measured the Host's own CPU over 15 warm polls.

| Part | CPU per poll | Runs | Range |
| --- | --- | --- | --- |
| `claude agents --json --all` | 296.9 ms median | 20 | 234.4 to 359.4 ms |
| `claude --version` | 31.2 ms median | 20 | 15.6 to 78.1 ms |
| Host in the warm loop | 161.5 ms mean | 15 polls | Not sampled per poll |
| Total | about 490 ms | | |

The agent listing is the limiter at about 61 percent of a poll. It is not
process startup, because `--version` starts the same executable and costs a
tenth as much. Its internal cause is inside the Claude CLI and was not
profiled. The Host's share probably comes from starting two child processes and
reading the process inventory. That share was not profiled either.

Wall time for the agent listing was 319.5 ms median. Windows reports process CPU
in 15.6 ms steps. Machine CPU was a median 37 percent during the 20 runs because
other sessions were active, so the spread is wider than on an idle PC. Child CPU
counts each `claude.exe` process only, not any process it starts.

The script gives each command a timeout (10 s by default) and a shared 1 MiB
output cap. Each measured process runs in its own Job Object. On either limit,
the script terminates the job, which stops descendants even after their parent
has exited. A child started in the moment before the job assignment would
escape it. The report counts runs that could not be placed in a job. It refuses to run
unless the configuration root passes the same checks as `ClaudeReadCommandRunner`,
uses the same child environment and working directory, and records only a
matched version string. A first measurement, made before the script had these
guards, gave a 351.6 ms agent-listing median on the same root. These figures
replace it.

## Interval candidates

| Interval | Share of one core | Share of this PC |
| --- | --- | --- |
| 5 s | 9.8 % | 0.45 % |
| 10 s | 4.9 % | 0.22 % |
| 15 s | 3.3 % | 0.15 % |
| 30 s | 1.6 % | 0.07 % |
| 60 s | 0.8 % | 0.04 % |

The Host's working set stayed between 58 and 64 MB over the 15 live polls, with
no upward trend after the first poll.

## Proposed settings

- **Poll interval 15 s.** It costs about 0.15 percent of this PC and about 3.3
  percent of one core. A change in session state shows up within one interval.
  The owner's responsiveness-first ruling favors 30 s if a 15 s delay is
  acceptable to trade for half the cost.
- **Stale after 45 s.** That is three intervals. A slow poll that waits out both
  10 s command timeouts still finishes within 45 s of the previous success, so it
  does not flip the snapshot to stale on its own. A failed poll marks the
  snapshot stale immediately with `LatestAttemptFailed`, regardless of age.

These values are constants in `ObservationSettings`. Changing them needs a
ruling on ticket 9, not a code redesign.

## Source version change

The installed Claude CLI updated itself from `2.1.322` to `2.1.323` during the
review fixes. Neither this branch nor PR 21 installed or updated it. PR 21's
two-version allowlist refuses `2.1.323`. The loop therefore reports
`UnsupportedVersion`, and the measurement script counts its one-shot Host
captures as failures. The recorded measurements were taken on `2.1.322`.
Admitting `2.1.323` needs the same compatibility check that PR 21 used, which is
outside this PR.

## Not covered

- Idle-PC measurements and other hardware.
- CPU of processes that the Claude CLI starts.
- A long soak run, sleep and resume, and source restarts during a live poll.
- An elevated CLI reading a non-elevated Host. The owner check targets this
  case, but no elevated run was made.
- A pipe owned by another Windows user. That needs a second account.

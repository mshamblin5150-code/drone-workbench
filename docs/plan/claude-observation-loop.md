# Host-owned Claude observation loop

The owner asked for one host-owned observation loop over the read-only Claude
source from [PR 21](https://github.com/mshamblin5150-code/drone-workbench/pull/21),
an in-memory latest snapshot, a local channel scoped to the Windows user, and
`drone-workbench sources --json` to read that snapshot. Polling settings are
measured for [Set collector restart behavior and resource budgets](https://github.com/mshamblin5150-code/drone-workbench/issues/9).

Out of scope: source controls, hooks, cleanup, sign-in installation, the
six-stage adapter program, persistence, the dashboard and any change to Codex
desktop launch behavior.

## Usage

```text
Workbench.Host                         # health on loopback, Claude loop, local channel
drone-workbench sources --json         # latest snapshot from the running host
```

The CLI always prints one JSON object. Exit codes are as follows.

| Exit | Reason | Meaning |
| --- | --- | --- |
| 0 | none | The host answered with a schema-1 snapshot |
| 2 | none | Usage error, such as a missing `--json` or an invalid instance suffix |
| 3 | `NoHostListening` | No host instance accepted the connection in time, either because none runs or because every instance is busy |
| 3 | `ConnectFailed` | The connection failed for another I/O reason |
| 3 | `UntrustedOrDenied` | Access was denied, or the pipe is not owned by the current user |
| 4 | `ProtocolError` | A pipe answered, but not with a valid snapshot |
| 130 | `Cancelled` | The user cancelled the command |

A missing host never prints a healthy empty snapshot.

## Data shape

The loop writes one immutable `ObservationState` and swaps it atomically. The
channel reads it and projects it with the host clock at read time.

```text
ObservationState
  Revision            long, +1 per publish
  LatestAttempt       ClaudeObservation?   newest result, success or failure
  LatestObservation   ClaudeObservation?   newest Observed or Partial result
  ConsecutiveFailures int

Freshness (computed at read time)
  Pending   no attempt yet
  Fresh     latest attempt succeeded and its ObservedAt is within StaleAfter
  Stale     an observation exists, and the latest attempt failed or the observation is older than StaleAfter
  None      attempts exist, but none succeeded
```

A stale observation keeps its session rows, unknown counts, supervisor
`Unknown` and coverage gaps unchanged. Freshness wraps it. The projection never
turns a failed or expired attempt into an empty or idle source.

## Channel

- Named pipe `drone-workbench-v1-<hash>`, where `<hash>` is the first 16 hex
  characters of SHA-256 over the current user's SID. Optional
  `DRONE_WORKBENCH_INSTANCE` (`[a-z0-9]{1,16}`) adds a suffix so tests and
  development hosts do not collide with a real host.
- Server DACL is protected and holds two entries. It allows the current user
  and denies the `NETWORK` SID. The first instance uses `FirstPipeInstance`, so
  a second host for the same user fails to start.
- After connecting, and before sending anything, the client reads the server
  pipe's owner and requires it to be the current user's SID. The built-in
  `PipeOptions.CurrentUserOnly` check is not used, because it may compare
  against the token's default owner. For an elevated administrator that owner
  is the Administrators group, which would reject the real host.
- The server keeps the name held while it replaces instances. It creates a
  replacement before releasing an abandoned instance whenever an instance is
  free. A failed instance creation backs off and retries instead of stopping the
  host. A failing snapshot projection answers
  `{"schema":1,"error":"SnapshotUnavailable"}`.
- The protocol is one UTF-8 JSON line per connection,
  `{"schema":1,"request":"sources"}`. The request is capped at 256 bytes, and
  the connection is limited by a timeout. Any other request gets
  `{"schema":1,"error":"UnsupportedRequest"}`.
- This enforces the local user boundary for the application. It is not
  isolation from other code running as the same Windows user.

## Alternatives

| Alternative | Benefit | Cost | Choice |
| --- | --- | --- | --- |
| Named pipe with a user-only DACL | Local, user-scoped, no port or token, matches the build plan's CLI channel | Windows-specific | Chosen |
| Loopback HTTP data route | Reuses Kestrel | Any local user can reach it without a separate authentication scheme | Rejected |
| CLI runs its own capture | No host needed | Two collectors and two truths, against the one-host design | Rejected |

## Throughput checkpoint

- [x] Blocking first steps. Refresh main, verify the pinned stack, run the full
  .NET verification on unmodified main, read PR 21 and ticket 9.
- [x] Independent workstreams. One owner writes the coupled Host, Core, CLI and
  test code. The lead writes the disjoint measurement script and runs live
  measurements.
- [x] Shared mutable state. Only the implementation owner writes `src/` and
  `tests/`. The lead writes `tools/measure-claude-polling.ps1` and docs.
- [x] Smallest safe decomposition. The loop, snapshot, channel and CLI share one
  response shape, so one owner keeps them consistent.

## Workflow limits

PStack's Feature playbook applies. The brief fixes the shape, so the parallel
`architect` design runners did not run. The default cross-model panel is
unavailable. These roles ran, each in a separate helper context:

- one implementation owner for the first build;
- an independent correctness and security reviewer;
- PStack's `no-comments` prompt;
- a fresh implementation owner for the accepted review fixes.

The lead reviewed both diffs, ran the full verification, and ran the live checks.

# Read-only Claude observation slice

The owner authorized [this bounded implementation](https://github.com/mshamblin5150-code/drone-workbench/issues/20)
after reviewing the merged assessment and proposal. It implements observation
only. The full source contract, six-stage adapter program, background collection,
dashboard, and controls remain separate work.

## Grounding and choice

Host currently serves only loopback health. Core contains foundation identity,
and CLI reports foundation diagnostics. An authenticated source-data transport
and an authoritative store do not exist yet. Exposing live metadata over HTTP
would therefore add a premature data endpoint.

Two independent design contexts compared these alternatives:

| Alternative | Benefit | Cost |
| --- | --- | --- |
| Internal source with an opt-in test runner | Leaves product arguments unchanged | Adds runner plumbing without a directly usable capture command |
| Internal source with a one-shot Host command | Exercises the full provider-to-safe-output path | Adds an explicitly bounded diagnostic command |

Use the one-shot command with the other candidate's child-only environment
construction and typed failed-attempt result. This follows Foundational Thinking
by establishing the evidence boundary before a polling lifecycle. Laziness
Protocol keeps history, shared control types, and transport infrastructure out
of this slice. Model the Domain makes the read-command set closed.

## Caller and boundary

`Workbench.Host observe-claude --json` captures once and exits. It opens no HTTP
listener. Normal no-argument Host health behavior stays unchanged. Only source
version and agent listing are callable. No target, arbitrary command, control,
hook, daemon-management, or model-turn operation exists in this path.

The source uses validated session identities and process incarnations within each capture.
The console projection omits native IDs, PIDs, paths, names, and raw errors.
Session rows expose entrypoint, state, and whether process identity matched.
Process coverage exposes completeness, a known unmatched lower bound, and a
nullable exact unmatched count. Failed source attempts never become healthy
empty snapshots. There is no private history cache; the future authoritative
host will own retention and stale projections.

Supervisor health remains unknown even when agent listing succeeds. Unlisted
Claude processes are processes, not invented sessions. Counts can include UI or
helper processes. Denied identity reads and incomplete ancestry remain explicit.
No process command lines, environment blocks, transcripts, or private databases
are read. Other configuration roots are not discovered or scanned.

Child process environment dictionaries handle absent or empty configuration
roots without modifying the parent environment. Absolute selected roots may be
honored without exposing their paths. Relative roots are refused to prevent
working-directory state creation. Standard tests use synthetic provider and
process fixtures. Live verification is explicit and read-only.

The previous untracked experimental harness is superseded for this slice by a
maintained read-only verification path. It has no session-target filtering or
cleanup controls. An empty selection therefore cannot expand into a real-session
mutation. Disruptive experiments remain outside this authorization.

## Throughput checkpoint

- [x] Blocking first steps. Refresh main, verify the complete pinned stack,
  inspect the actual foundation and new findings, and compare two designs.
- [x] Independent workstreams. One owner writes the coupled Host source and
  tests. Another writes the disjoint verification script. The lead reconciles
  plans and owns review and delivery.
- [x] Shared mutable state. Only the production owner writes Host and Host
  tests. The tooling owner writes one new script. Source pins remain unchanged.
- [x] Smallest safe decomposition. Keep parsing, process evidence, composition,
  and their tests under one owner because they share the observation model.

## Verification and workflow limits

Require synthetic privacy, schema, process, environment, fixed-command, timeout,
and failure checks, plus ordinary Host regression tests. Then run the pinned
full build verification and one bounded live observation without starting work.
Record live cases absent from the environment as unverified.

The pinned Pocock issue workflow and PStack feature/architecture workflow apply.
Supported separate design contexts ran; the default cross-model panel and its
judge were unavailable. Independent review and runtime checks must identify
their actual roles and limits. This is a partial source slice, not PC-wide
coverage or closure of the source-feasibility ticket.

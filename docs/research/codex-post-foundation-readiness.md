# Codex readiness after the foundation

Date: 2026-11-12. Baseline: foundation merge `575f5d9`. This is the Codex follow-up
for [Prove reach across the actual Codex and Claude entrypoints](https://github.com/mshamblin5150-code/drone-workbench/issues/7).
The owner will assign Claude separately after this handoff.

## Result

The Codex assessment is ready for cross-review. Existing desktop-owner semantic
coverage remains unresolved. The foundation does not create a connection to that
owner, and a successful isolated server is not a substitute.

## Current owner evidence

A fresh filtered Windows snapshot found one app-server with a `ChatGPT.exe`
parent. Its observed options included analytics, with no explicit listener or
stdio option. No TCP listener was associated with that process. Default stdio is
the documented behavior; absence of an explicit option is not a direct inspection
of its private streams.

The process and listener queries succeeded, and the owner incarnation stayed
stable through the listener sample. Failed or unstable queries yield unknown
listener counts rather than zero.

The read-only daemon version query failed at its socket connection with reported
OS error 10050 and returned no JSON object. This does not prove that all Windows
socket connections fail or that no other endpoint could exist. The successful
owned Unix transport diagnosis already establishes why that generalization is
wrong. See the [sanitized current-owner receipt](codex-current-owner-receipt.json).

The [current documentation and installed-help review](codex-owner-route-review.md)
identified no supported attachment method under the present constraints.
Documented clients need an endpoint exposed by the actual owner. Changing launch
behavior or adding a bridge is a separate integration design, not a client-side
fix for private stdio.

## Pinned .NET 11 transport proof

A fresh framework-only C# 15 console prototype built with SDK `11.0.100` and ran
on .NET `11.0.0`, using installed Codex CLI `0.167.0-alpha.4.1`. The build returned
zero warnings and zero errors. It inherited no production source or package
configuration and used no additional NuGet dependencies. This is a throwaway
instrument, not an adapter or an analyzer-verified production project.

The author ran it once, then the lead independently repeated the released
harness. Each run started a different owned server with a fresh private home
and current-user-only Unix socket directory. Within each run, two clients
connected to the same server through WebSocket HTTP Upgrade over a Unix socket.
Each initialized and read an empty `thread/loaded/list` snapshot. Only
`initialize`, `initialized`, and `thread/loaded/list` were sent.

All four handshakes and snapshots passed. Both runs separately confirmed the
owned server's exit after teardown. Client closure reported
`ConnectionClosedPrematurely` on all four connections, followed by bounded client
abort. Graceful WebSocket closure is therefore unverified. Reconnection here
means another empty-state connection, not active-work recovery or event replay.

The [sanitized two-run receipt](codex-net11-transport-receipt.json) records exact
versions, source hashes, checks, and limits. The private harness and owned
artifacts remain under the implementation worktree's ignored
`.local/codex-net11-proof` directory, with short socket directories under the
primary checkout's `.local/c7n11-*`. Those paths contain synthetic state. The
throwaway harness is not in the public checkout, so this receipt alone is not a
fresh-clone rerun command.

This closes the earlier diagnosis's .NET 11 transport-build evidence gap. It
does not close its existing-owner or production acceptance gaps.

The pinned engineering verifier passed before this work. Pocock's research
workflow supplied an independent documentation review; PStack's prototype
workflow supplied the bounded empirical check. The default cross-model panel
was unavailable. The source review, prototype author, and lead repeat are
separate responsibilities, not a claim of cross-model agreement. No production
files changed, so the foundation's full test suite was not rerun for this report.

## Coverage boundaries

| Source or behavior | Evidence | Remaining limit |
| --- | --- | --- |
| Existing desktop owner | Filtered process metadata and failed default-daemon connection | No semantic thread, turn, or subagent access |
| Existing independent CLI work | Documented endpoint clients | Actual independent-owner coverage remains unverified |
| Owned isolated server | Earlier stdio tests and repeated .NET 11 Unix WebSocket handshake | Does not reach existing desktop or independent CLI owners |
| Synthetic command termination | Earlier exact-target acknowledgement, terminal result, and process-handle exit | No live model turn, subagent, or descendant-tree guarantee |
| Disconnect and reconnect | Earlier repeated empty snapshots on the same owned server | No active-work replay, event cursor, uncertain-effect reconciliation, or stale-control proof |
| Intentional publications | Proposed source contract | No reporting endpoint or independent liveness implementation yet |

The [earlier Codex results](codex-ticket7-results.md),
[connection diagnosis](codex-connection-diagnosis.md), and
[combined assessment](ticket7-combined-assessment.md) remain intact. This report
extends their evidence instead of replacing their limits.

## Shared-contract review

The corrected [shared contract proposal](https://github.com/mshamblin5150-code/drone-workbench/blob/5dad039194ff54ddc4e39e833007f6f91eda37b6/docs/plan/source-contract.md)
preserves separate source instances, connection generations, work identities,
process incarnations, and evidence stages. A second server sharing a home is
still a different owner. Codex command handles expire with their connection.
Reconnection cannot grant control over previous work merely by reusing an ID.

These rules fit the observed evidence. They are not implemented production
types. [PR 15](https://github.com/mshamblin5150-code/drone-workbench/pull/15) remains
separate. No production contract, adapter, control, hook, or existing-owner
listener was activated here. The owned prototype listener is a disposable fixture.

## Next owner

Claude can now extend its source assessment and independently cross-review these
findings using the [bounded handoff](../plan/claude-coverage-handoff.md). Its
successful source coverage would not establish Codex owner access.

After both assessments, select a supported owner exposure or reporting design
that can prove the required Codex reach. Preserve the PC-wide goal until the
owner explicitly changes it. The shared coverage ticket remains open. No
model-turn test, real-work control, desktop restart, daemon activation, global
configuration change, private history read, or cleanup operation is implied.

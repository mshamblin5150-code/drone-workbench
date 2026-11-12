# Architecture rationale

Status: proposed synthesis for confirmation. Three delegated designs were read
and compared using Pocock's deep-module method and PStack's design-space guidance.
These were available-agent perspectives, not independent model-family verification.

## Problem

Drone Workbench must observe several external agent entrypoints and Windows
execution without confusing visibility with ownership. Its human dashboard and
drone CLI need consistent decisions and live results. Cleanup adds preservation,
fresh approval and recovery obligations that cannot be distributed across clients.
The background process must remain responsive and independent of browser lifetime.

## Usage before shape

A drone queries current state, intentionally reports a permitted summary or
requests an inspection. It receives typed status, coverage and operation IDs.
The human reviews an exact proposal and requests Approve and run. Reconnection
reads the existing operation rather than authorizing another run.

Illustrative C# contracts, not compiled code:

```csharp
interface IWorkbenchViews
{
    Task<ViewPage> ReadAsync(ViewQuery query, CancellationToken ct);
    IAsyncEnumerable<ViewChange> WatchAsync(Revision after, CancellationToken ct);
}

interface IActivity
{
    Task<ObservationResult> ObserveAsync(SafeSourceBatch batch, CancellationToken ct);
    Task<ControlReceipt> RequestControlAsync(VerifiedControl request, CancellationToken ct);
}

interface IHygiene
{
    Task<OperationRef> ProposeAsync(InspectionRequest request, CancellationToken ct);
    Task<Acceptance<RunRef>> ApproveAndRunAsync(ReviewedProposal proposal,
        DashboardIntent intent, RequestId requestId, CancellationToken requestWait);
    Task<OperationRef> RecoverAsync(RecoveryRequest request, CancellationToken ct);
}
```

The host constructs trusted caller context from the authenticated route; submitted
actor flags and raw provider objects never become public authority. Drone routes
do not expose ApproveAndRun. Domain results distinguish unavailable, unsupported,
partial, stale, conflict, needs-review and failed outcomes rather than one Boolean.

ApproveAndRun durably accepts a host-owned operation before acknowledgement.
Request cancellation ends waiting, not the accepted operation. Retrying a lost
acknowledgement with the same request identity returns the same run; a new request
cannot reuse consumed approval. Views report final results independently.

## Shape and ownership

- **Activity** owns source identity, coverage, safe normalization, association and
  verified control reconciliation. Claude, Codex and Windows adapters are real
  variation at its seam; transport objects stay inside their implementation.
- **Hygiene** owns inventory eligibility, proposals, approval consumption,
  preservation, journaled mutation, reconciliation and recovery. Callers do not
  orchestrate a public sequence of low-level delete/check/save operations.
- **Retention** owns data-class-specific expiry and recovery-review eligibility.
  It cannot turn a routine history sweep into recovery/evidence deletion.
- **Views** owns bounded safe projections and revision/resnapshot behavior. It
  reads committed facts and cannot authorize controls or mutate domain state.
- **Lifecycle** owns durable desired running/stopped intent and bounded restart.
  The small supervisor upholds it and exposes a user-scoped operator
  status/stop/resume interface while the host/dashboard endpoint is absent.

The host alone writes domain persistence. Database types and low-level adapters
are internal seams for testing, not a public repository framework. SQLite is the
proposed single-PC transactional implementation; its one-writer limits fit this
ownership. Distributed/multi-machine state is outside the first release.

Depth is demonstrated by the deletion test: removing Hygiene or Activity would
redistribute difficult policy into every CLI/page/adapter. Removing a generic
forwarding bus would remove plumbing, so this design has no generic bus chain.

## Comparison and synthesis

The rubric was: preserve ratified scope, concentrate authority/invariants, hide
source complexity behind small interfaces, limit background operational overhead,
support recovery/retention evidence, and expose honest feasibility gates.

| Candidate | Strength | Concrete cost | Synthesis |
| --- | --- | --- | --- |
| One authority with deep modules | Small deployment topology, clear state ownership, simple caller seam | Host is a common failure point; blocking integrations need bounded execution | Base design |
| Independently supervised provider workers | Fault/resource isolation and independent recovery | Extra processes, IPC, epochs, packaging and ambiguous delivery | Keep isolation as an evidence-driven extension; use typed source envelopes and reconciliation discipline now |
| Workflow owners with live projections | Explicit command/cleanup states, responsive views and retention-aware updates | Projection/revision design; overbuilding it would create event-sourcing complexity | Adopt small rebuildable views, explicit states and separate retention classes; reject immortal observation replay logs |

The result keeps the first candidate's deployment simplicity and the third's
workflow/retention discipline. From the second it adopts bounded provider calls,
source-generation identity and the option to isolate a demonstrably faulty adapter.

## Trade-offs and rejections

- One host concentrates failure; bounded calls, journaling and supervisor recovery
  are required before adding process isolation based on observed need.
- External filesystem/Git mutations cannot be atomic with database transactions.
  Receipts and reconciliation replace false claims of exactly-once destruction.
- Thin clients require the host, but keep policy and storage invariants in one place.
- Bounded history cannot be the permanent event source. Safe view snapshots and
  minimal required workflow evidence survive without retaining ordinary history forever.
- A separate worker per provider or web/cleanup module is not the default. Neither
  independently deployed microservices nor a peer shared-SQLite writer model earns
  its complexity for the first release.
- Mandatory Windows Hello/WebAuthn enrollment was considered and rejected as an
  unrequested change to the interaction/deployment contract. Use authenticated,
  action-bound dashboard review and server-side authorization; do not claim that
  a browser click proves physical human presence against arbitrary same-user code.

## Evidence gates

Existing Codex Desktop attachment, full Claude configuration-root reach, native
control effects, link/identity safety, restoration, resource budgets, dependency
compatibility and discovery are not established by these sketches. Their synthetic
experiments and failure branches are explicit in the build plan. No private-state
scraping or broad process termination is an implicit fallback.

The next implementation work, after a separate implementation instruction, is S0:
verify the pinned toolchain and source/surface feasibility with a bounded synthetic
experiment before claiming complete first-release coverage.

## Review

An independent delegated review found two contract gaps: no surviving resume path
when the host/web endpoint is stopped, and ambiguity between browser request
cancellation and accepted cleanup lifetime. Both were corrected and re-reviewed.
The plan now specifies the supervisor's out-of-host operator interface, durable
run acceptance, idempotent lost-acknowledgement retry, and matching acceptance rows.
No remaining material issue was reported in that delta review. This is document
review evidence, not execution of the integration or cleanup tests.

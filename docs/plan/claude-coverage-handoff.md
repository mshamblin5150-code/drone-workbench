# Claude coverage handoff after the foundation

The owner assigned Codex to finish its side before asking Claude to continue.
Start from current `main`, which includes the .NET foundation from
[PR 17](https://github.com/mshamblin5150-code/drone-workbench/pull/17).
Read `AGENTS.md` and use the complete pinned engineering stack through
`docs/agents/engineering-stack.md`. Verify it before work. Preserve global
settings and the original local session-interface reports.

## Goal

Complete the Claude source assessment and cross-review the Codex evidence for
[Prove reach across the actual Codex and Claude entrypoints](https://github.com/mshamblin5150-code/drone-workbench/issues/7).
Identify the next implementable integration task without claiming PC-wide
coverage from isolated fixtures.

## Read first

- [Codex readiness](../research/codex-post-foundation-readiness.md) and its linked
  receipts. Existing desktop-owner access remains unresolved.
- [Earlier Claude results](../research/claude-ticket7-results.md) and
  [combined assessment](../research/ticket7-combined-assessment.md). Extend these
  findings rather than repeating already successful idle-session tests.
- [Corrected shared contract proposal](https://github.com/mshamblin5150-code/drone-workbench/blob/5dad039194ff54ddc4e39e833007f6f91eda37b6/docs/plan/source-contract.md)
  and the other documents in [PR 15](https://github.com/mshamblin5150-code/drone-workbench/pull/15).
  This is a reviewed proposal, still separate from production implementation.

## Your ownership

Own Claude-specific research, bounded synthetic conformance experiments, and
their sanitized evidence. Use an isolated branch from current `main`. Leave
Codex files, shared production types, and the foundation build settings outside
your write fence. Propose shared changes explicitly for integration review.

Recheck the installed Claude version and source roots through supported metadata
interfaces. Establish which desktop, interactive, background, and externally
started work is observable. Keep untested entrypoints and active-work cases
explicit. Use fresh model-free fixtures where sufficient. Actual model turns,
new credentials, global hooks, source configuration changes, and real-session
controls are outside this bounded handoff.

Preserve the corrected authority rule: native `claude stop <id>` research does
not prove atomic incarnation/state-bound dispatch. Real Workbench stop dispatch
stays disabled until a supported route proves that guarantee or the owner
separately chooses another authority model. Already-stopped observations dispatch
nothing and invent no acknowledgement.

Distinguish source acceptance, terminal source outcome, and independently
observed process exit. Record fresh versus stale metadata, identity changes,
disconnect gaps, and source-version bounds. Filter before writing public results.
Do not read transcripts or private databases as a telemetry fallback.

## Deliver

Publish a concise Claude readiness report and sanitized receipt. Cross-review
the Codex result independently, including its existing-owner limitation. List
passed checks, remaining gates, and which require implementation or a human
ruling. Recommend one next integration task with a clear file fence and
prerequisites. Keep the shared coverage ticket open until both sources and the
combined acceptance conditions are satisfied.

Run relevant checks and independent review through the pinned workflow, then
open a ready PR. Report its exact revision and tested scope. Do not install a
bridge, change launch behavior, merge the shared contract, or start the adapter
program merely because its planning document exists.

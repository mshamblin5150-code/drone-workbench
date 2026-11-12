# Read-only Claude slice verification

Date: 2026-11-12. Scope: [the authorized read-only slice](https://github.com/mshamblin5150-code/drone-workbench/issues/20).
Independent full verification covered implementation commit
`b8f8b9e024a024ec23d6e9bc5fd386f331c67f6d` on Windows x64. This implements a
bounded diagnostic capture, not the six-stage adapter program or PC-wide support.

## Observed checks

| Check | Result |
| --- | --- |
| Complete pinned engineering stack | Verifier passed for four packages and 103 skills |
| Reconciled adapter plan | Pinned plan checker reports six sections and zero problems; execution stays held |
| Exact SDK and package closure | SDK 11.0.100, seven direct package hashes, locked restore passed |
| Release build and formatting | Zero warnings, zero errors, formatting verification passed |
| MTP suite | 60 passed, none failed or skipped, including 50 Host tests |
| Direct VSTest | Five CLI tests passed |
| Windows x64 publish | Host, CLI, and Supervisor passed |
| Synthetic observation guard | Source unavailable, no snapshot, no exact process total |
| Published Host guard | Same unavailable contract and bounded exit |
| Live read-only capture | Source 2.1.322, partial capture, three Desktop rows |
| Incomplete process inventory | Known unmatched lower bound 11; exact total remained unknown |
| Supervisor and control claims | Supervisor unknown, controls unavailable |
| Environment and working directory | Preserved through full verification and observation checks |
| Settings and certificates | Audited settings unchanged; no new development certificates |

The live counts are one observation, not a standing census. Unmatched processes
can include UI, supervisor, or helper processes. Native PID/creation-time checks
support point-in-time associations; they do not prove atomic source ownership or
exclude stale provider metadata.

## Compatibility and failure behavior

The installed CLI changed from the earlier research version `2.1.312` to
`2.1.322` without this task installing or updating it. The original guard refused
the new version before agent listing. Installed help and an in-memory shape/type
check confirmed the read-only array interface, then that exact version was added.
No version range was enabled. `2.1.312` retains historical evidence and synthetic
fixture coverage; this implementation's live run used `2.1.322`.

The parser and runner tests cover malformed roots and types, duplicate properties
and identities, arbitrary enum values, contradictory stopped/busy fields, source
errors, timeouts, oversized output, privacy canaries, closed command dispatch,
and child-only configuration. Process fixtures cover denied or incomplete reads,
PID replacement, ancestry ambiguity, and idle unmatched processes. A failed
capture carries no successful snapshot and cannot mark prior work complete.

The default verification script hides the Claude executable from the child. It
therefore checks the failure boundary without observing real sessions. Explicit
`-Live` mode reads the existing selected root through the same production path.
It starts no session or model turn and exposes no target selection or cleanup
operation. Missing, empty, and relative root directories are refused by the
source runner. Parent environment variables are never changed by that runner.

## Review

Independent Spec review found no blocking findings. Standards review found no
blocking findings and one stale verification allowlist value, `Starting`, which
the producer cannot emit. That value was removed after the full verdict. The
synthetic guard and whitespace checks passed again. Application source, build
settings, dependencies, and the admitted live states did not change after the
independent full run.

Comment review found zero deletion or reshape findings. The additional StyleCop
exclusions are documentation-only rules for
[partial elements](https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/master/documentation/SA1601.md)
and [enum members](https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/master/documentation/SA1602.md).
They match the existing XML-documentation convention. Correctness analyzers and
warnings-as-errors remain enabled.

Pocock's issue and two-axis review workflows and PStack's feature/architecture
workflow guided the work. Two independent designs were compared, production and
tooling writers had disjoint fences, and a separate non-author ran full runtime
verification. The default cross-model panel and Cursor cloud verifier were
unavailable. Supported local contexts supplied the disclosed equivalents.

## Evidence and remaining scope

The maintained commands are:

```powershell
pwsh -NoProfile -File tools/verify-dotnet.ps1
pwsh -NoProfile -File tools/verify-claude-observation.ps1
pwsh -NoProfile -File tools/verify-claude-observation.ps1 -Live
```

Private execution logs and count-only receipts remain in the implementation
worktree's ignored `.local/verification` directory. No raw source output, session
identifiers, PIDs, names, or paths are emitted by the one-shot result. Existing
untracked session reports and pinned source packages were preserved.

The prior unsafe experimental harness was superseded, not rerun. No supervisor,
session, or real workload was stopped. Timeout cleanup is confined to an exact
observation-command process launched by the runner. No hooks, daemon changes,
source roots, startup task, collector loop, authenticated data endpoint, or
dashboard were activated. The underlying Host health mode remains unchanged.

Existing Codex owner access, other roots, post-prompt CLI, headless work,
subagents, and other untested entrypoints remain gaps. The shared coverage and
lifecycle-budget tickets remain open. The operator-only merge policy proposed
for the six-stage program has not been activated by this slice.

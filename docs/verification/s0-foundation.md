# S0 foundation verification

Verified on Windows x64 on 2026-11-12 against implementation commit
`3fb1216019748a27cf93acefa70ba54d75d6fba5`. This records the build and runtime
portion of [issue 16](https://github.com/mshamblin5150-code/drone-workbench/issues/16).
It does not complete the source-coverage gate.

## Repeated checks

An independent verifier ran `pwsh -NoProfile -File tools/verify-dotnet.ps1`.
The command exited successfully and established these results:

| Check | Observed result |
| --- | --- |
| Repository-local SDK | Exact pinned version selected after archive SHA-512 check |
| Direct packages | All seven downloaded and restored package hashes match their pins |
| Dependency closure | Eight committed lock files accepted by locked restore |
| Release build | Zero warnings and zero errors with analyzers enabled |
| Formatting | Verification passed without changes |
| MTP tests | 13 passed, none failed or skipped |
| Direct VSTest | Five CLI tests passed with the pinned adapter and Test SDK |
| Windows x64 publish | Host, CLI, and Supervisor published successfully |

The SDK bootstrap also passed fresh extraction, cached reuse, and deliberately
incorrect hash rejection. A review found that PowerShell directory publication
could nest a duplicate SDK when installers raced. Atomic directory publication
now rejects an existing destination. A synthetic competing-publication check
preserved both the winner and the losing stage without nesting.

The pinned xUnit package uses MTP v2. Its MSBuild bridge rejects the older test
route on this SDK. The solution explicitly selects MTP and separately runs direct
VSTest to exercise the retained compatibility packages. See the primary
[xUnit runner guidance](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)
and [SDK runner selection](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test).

## Review and scope

Independent comment and standards reviews found no blocking findings. Spec review
found the SDK publication race described above; it was fixed and checked before
the final full run. The documentation-only StyleCop exclusions cover file
headers, missing public XML prose, and disabled XML-document analysis. Other
analyzer rules and warnings-as-errors remain enabled.

A separate native Claude read-only review ran with `claude-opus-5-5`. It found no
blocking defect and prompted stronger environment isolation, build-server
isolation, and bounded checks of every host listener. Other notes were either
already addressed by LF checkout attributes and atomic publication, intentional
fail-closed cache behavior, or future verification improvements. This review read
source only; the independent runtime checks establish execution results.

The final inherited-environment experiment initially failed because assigning an
empty SDK-path variable kept that variable defined. The script now removes
variables that must be unset and restores their exact original state afterward.
The focused locked-restore experiment and the final full run passed with hostile
SDK path, runtime root, runner, and roll-forward overrides present on entry.
All 20 managed variables and the working directory were restored exactly.

The final independent verdict was PASS+NOTES. Published CLI help, diagnostics,
and argument errors passed. Supervisor diagnostics reported unavailable lifecycle
features. The published Host exposed one IPv4 loopback listener and returned HTTP
200 for health. Its exact owned process exited after test teardown. This proves
owned termination, not a graceful-shutdown contract. No new development
certificate appeared during verification.

The pinned engineering source verifier passed for the complete four-package
closure. Supported local helpers provided design, implementation, and independent
review contexts. The default cross-model panel and Cursor cloud verifier were
unavailable. The independent local runtime verifier is the disclosed equivalent,
not evidence that those unavailable services ran.

## Hygiene and remaining work

The first SDK invocation created a development certificate. The exact certificate
created by this run was removed after identity checks; the older certificate was
preserved. Verification disables further certificate generation and restores its
process environment, including variables that were originally unset.

Automatic approval review rejected removal of the owned `.local/bootstrap-proof`
test directory as "blocked by policy". It remains under the implementation
worktree. The shared SDK archive and package caches are intentional reusable
build artifacts. Public source contains only synthetic fixtures and sanitized
results. Both original untracked session-interface reports were preserved.

The collector, dashboard, source adapters, cleanup inventory and executor,
supervisor recovery, and startup registration remain unimplemented. The existing
Claude adapter proposal remains separate. Required existing-owner coverage and
lifecycle budgets stay open in the planning map.

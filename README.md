# Drone Workbench

A Windows-first hub for observing drone work across projects, Codex, and Claude,
reviewing file hygiene, and controlling work through verified ownership and
supported interfaces.

## Current stage

The foundation provides a loopback health host, CLI diagnostics, and supervisor
diagnostics. While running, the Host polls read-only Claude metadata and process
coverage and keeps the latest snapshot in memory. The CLI reads that snapshot
over a channel limited to the current Windows user. Persistence, the dashboard,
cleanup, background supervision, startup registration, and source controls
remain unimplemented. Existing Codex owner access remains unresolved.

The canonical map is [Plan Drone Workbench before implementation](https://github.com/mshamblin5150-code/drone-workbench/issues/1).
Its child issues hold decision tickets. The map links to the ticket that owns
each resolution.

## Verify the foundation

From a Windows x64 checkout with PowerShell 7 and Git, run:

```powershell
pwsh -NoProfile -File tools/verify-dotnet.ps1
```

The script downloads and verifies the pinned SDK in repository-local storage.
It checks package hashes, restores locked dependencies, builds with analyzers,
checks formatting, runs synthetic tests, and publishes Windows x64 artifacts.
It restores its process environment on exit. SDK and package caches, publish
output, and private verification records stay under ignored `.local` directories.
No global SDK installation is required.

To inspect the CLI after verification, run:

```powershell
$dotnet = & ./tools/bootstrap-dotnet.ps1
& $dotnet .local/publish/Cli/Workbench.Cli.dll --help
& $dotnet .local/publish/Cli/Workbench.Cli.dll diagnostics --json
```

Diagnostics describe the foundation executable. They do not observe drone work.
See [the foundation design](docs/plan/s0-foundation.md) for scope and workflow limits.

## Capture Claude metadata once

After the build verification above, run the synthetic observation guard:

```powershell
pwsh -NoProfile -File tools/verify-claude-observation.ps1
```

To perform one read-only capture from the installed Claude CLI, run:

```powershell
pwsh -NoProfile -File tools/verify-claude-observation.ps1 -Live
```

Live mode requires the tested source version and an existing configuration root.
It reports counts, enum states, process coverage, unknown supervisor health, and
disabled controls. It starts no sessions and writes no source configuration.
Process counts can include UI and helper processes; they are not session counts.
Other roots and untested entrypoints remain explicit coverage gaps.

The underlying command is `Workbench.Host observe-claude --json`. It captures
once and exits without opening an HTTP listener. It does not start background
collection. See [the read-only slice design](docs/plan/claude-readonly-slice.md).
The [verification report](docs/verification/claude-readonly.md) records the tested
versions, live results, privacy boundary, and remaining coverage gaps.

## Run the host and read its snapshot

```powershell
$dotnet = & ./tools/bootstrap-dotnet.ps1
& $dotnet .local/publish/Host/Workbench.Host.dll
```

In another terminal, as the same Windows user:

```powershell
$dotnet = & ./tools/bootstrap-dotnet.ps1
& $dotnet .local/publish/Cli/Workbench.Cli.dll sources --json
```

The Host polls the Claude source every 15 seconds and keeps only the latest
result in memory. The snapshot marks a source `Fresh`, `Stale`, `None` or
`Pending`. A failed poll keeps the last good observation and marks it stale. If
no host answers, the CLI prints a reason and exits 3. Only one Host can run per
Windows user. See [the loop design](docs/plan/claude-observation-loop.md) and
[its verification record](docs/verification/claude-observation-loop.md).
`tools/measure-claude-polling.ps1` measures the per-poll cost on this PC.

## Chosen direction

- C#/.NET core: CLI, background collector, and local dashboard server.
- Blazor dashboard with server-side interactivity.
- Collector runs independently of the browser and starts at Windows sign-in in
  the current user's context.
- Cross-project and PC-wide observation, including externally started Codex and
  Claude work. Explicitly report unsupported sources and incomplete coverage.
- Control external work only through verified ownership and a supported control
  route. Observation does not grant control authority.
- Operational metadata and intentionally published structured work summaries;
  filter protected information before persistence. Full prompts and transcripts
  are outside collection.
- Dashboard job controls and approve-and-run of freshly checked exact cleanup
  proposals. Preserve unique/required material; recovery copies have a minimum
  30-day hold before review, with no automatic deletion at that date.
- Ordinary filtered dashboard history remains searchable for 30 days and then
  expires automatically, separately from cleanup evidence and recovery material.

Confirmed decisions are authoritative in their resolved GitHub decision ticket;
this overview is a navigation aid.

## Engineering and research

- [Research findings](docs/research/findings.md)
- [Codex readiness after the foundation](docs/research/codex-post-foundation-readiness.md)
- [Claude coverage handoff](docs/plan/claude-coverage-handoff.md)
- [Confirmed build plan](docs/plan/build-plan.md)
- [Architecture comparison](docs/plan/architecture-rationale.md)
- [Acceptance and evidence matrix](docs/plan/acceptance-matrix.md)
- [Pinned source inventories and proposed tooling baseline](docs/toolchain/baseline.json)
- [Project engineering workflow](docs/agents/engineering-stack.md)
- [Engineering environment design](docs/toolchain/engineering-stack-design.md)
- [Install, verify and use the pinned stack](docs/toolchain/use-engineering-stack.md)
- [Observed setup verification](docs/toolchain/engineering-stack-verification.json)

The original source baseline covers 757 Pocock and PStack files. The project
engineering lock adds the generated Claude harness and Team Kit dependency,
covering 1,346 files in total. The source verifier and native CLI discovery checks
have passed. Codex uses generated project entrypoints. Claude uses complete native
plugins through a session launcher. Capability limits, including desktop plugin
adoption and unavailable review models, remain explicit in the setup report.

Machine observations, private path mappings, credentials, raw sessions, and
clinical material belong in local runtime storage rather than source control.
Runtime storage locations and enforcement are still design decisions.

## License

Original project code and documentation are licensed under [MIT](LICENSE).
See [third-party notices](THIRD_PARTY_NOTICES.md) for upstream material.

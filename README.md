# Drone Workbench

A Windows-first hub for observing drone work across projects, Codex, and Claude,
reviewing file hygiene, and controlling work through verified ownership and
supported interfaces.

## Current stage

Wayfinding: produce a decision-backed build plan before implementation. No
collector, CLI, dashboard, cleanup executor, or startup task has been built or
installed.

The canonical map is [Plan Drone Workbench before implementation](https://github.com/mshamblin5150-code/drone-workbench/issues/1).
Its child issues hold decision tickets. The map links to the ticket that owns
each resolution.

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
- Dashboard job controls and approval of specific cleanup proposals. Cleanup
  eligibility, retention, recovery, and execution policy are still open.

Confirmed decisions are authoritative in their resolved GitHub decision ticket;
this overview is a navigation aid.

## Engineering and research

- [Research findings](docs/research/findings.md)
- [Pinned source inventories and proposed tooling baseline](docs/toolchain/baseline.json)

The source baseline identifies complete upstream Pocock and PStack packages,
including support files, rather than only skill entrypoints. Download integrity,
local compatibility, reference closure, and activation checks remain planning
work. A version pin does not establish an installed or tested dependency.

Machine observations, private path mappings, credentials, raw sessions, and
clinical material belong in local runtime storage rather than source control.
Runtime storage locations and enforcement are still design decisions.

## License

Original project code and documentation are licensed under [MIT](LICENSE).
See [third-party notices](THIRD_PARTY_NOTICES.md) for upstream material.

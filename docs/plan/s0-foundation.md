# S0 foundation design

Issue [Build the S0 .NET foundation](https://github.com/mshamblin5150-code/drone-workbench/issues/16)
owns this implementation. The owner authorized it from refreshed main. Existing
session coverage and control decisions remain in the planning map.

## Grounding

The repository contained plans, source locks, engineering tools, and synthetic
adapter experiments. It contained no product projects. The engineering stack
verifier passed before this work. The pinned SDK archive passed its SHA-512
check and runs from repository-local storage.

## Design choice

Two independent design contexts proposed four production projects. One proposed
four test projects and a production SQLite probe. The other proposed one test
project and SQLite compatibility tests without a production storage abstraction.
The synthesis keeps four test projects to preserve the build plan's ownership
boundaries and keeps SQLite in compatibility tests until persistence has a user.

| Project | Foundation responsibility |
| --- | --- |
| Core | Shared foundation identity without platform or persistence dependencies |
| Host | Owned loopback HTTP health endpoint |
| CLI | Parsed diagnostics, help, version, and argument errors |
| Supervisor | One-shot diagnostics that report supervision as unavailable |

The host does not collect work or serve a dashboard. Diagnostics report available
foundation behavior separately from future monitoring capabilities. Tests use
owned processes, ephemeral ports, and synthetic SQLite data.

## Throughput checkpoint

- [x] Blocking first steps. Refresh main, verify pinned sources, inspect existing
  plans, check the SDK archive, and compare two designs before implementation.
- [x] Independent workstreams. One owner implements projects and build settings.
  Another owns SDK bootstrap and verification scripts. The lead owns instructions,
  documentation, review, and integration.
- [x] Shared mutable state. Writers have disjoint file fences. One implementation
  owner controls the solution, package configuration, and lock files.
- [x] Smallest safe decomposition. Keep the coupled project code with one owner;
  tooling can proceed independently against agreed solution and project names.

## Workflow limits

The pinned PStack feature and architecture workflows guide this task. Pocock's
issue workflow tracks scope and acceptance. Supported helpers provide independent
contexts. The default Grok and Claude collaboration model panel is unavailable,
so these design contexts do not establish cross-model agreement. The lead chose
the synthesis without that panel's cross-judge. Global model settings stay intact.

## Acceptance

Completion requires exact SDK and direct package checks, committed transitive
locks, locked restore, analyzer-enforced builds, formatting, meaningful tests,
and Windows x64 publish checks. Record observed results separately from this
design. Actual agent adapters, cleanup, startup registration, and the dashboard
belong to later stages.

# Decision research

Research supports the Wayfinder decisions; it is not implementation evidence.
No cross-harness attachment, process-control, cleanup, or capacity prototype has
run. There are no performance or reclaimable-storage guarantees.

## Language and Windows behavior

C#/.NET was selected for the persistent Windows collector, CLI, and local server.
Blazor was selected for the human dashboard. Windows process ownership and file
identity still require explicit implementation; language choice does not provide
them automatically.

[Windows Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)
support process grouping, accounting, and control, with child-membership,
breakaway, and nested-job constraints. Ordinary process-tree termination is not
equivalent to proof that all owned work ended. Test launch/containment races,
process identity reuse, shared services, and supervisor failure.

[.NET file enumeration options](https://learn.microsoft.com/en-us/dotnet/api/system.io.enumerationoptions)
need deliberate treatment of hidden/system entries and reparse points. Logical
length, allocated bytes, and reclaimable space are different measurements.
[Windows reparse points](https://learn.microsoft.com/en-us/windows/win32/fileio/reparse-points)
and [links](https://learn.microsoft.com/en-us/windows/win32/fileio/hard-links-and-junctions)
need explicit coverage and containment rules.

## Cross-harness observation and control

Count conversations, turns, subagents, jobs, and processes separately. They do
not have one-to-one identities. Every observation needs a source, scope, time,
and explicit unavailable/stale behavior.

The documented [Claude agent view](https://code.claude.com/docs/en/agent-view)
provides JSON status for interactive/background sessions and supported controls
for background sessions. Schema, configuration-root coverage, and cancellation
effects must be verified against supported installed versions. Version/help
inspection confirmed that the JSON status option is exposed; it did not query
real sessions.

[Codex app-server](https://learn.chatgpt.com/docs/app-server) documents thread and
turn lifecycle operations. Whether a separate observer can attach to an existing
Windows desktop-owned live instance remains unverified. A new server's stored
thread history must not be mistaken for current state of another server.

[Claude hooks](https://code.claude.com/docs/en/hooks) and
[Codex hooks](https://learn.chatgpt.com/docs/hooks) are candidate prospective
metadata sources. Incoming payloads can contain content: filter allowed fields
before persistence. Do not fill coverage gaps by scraping raw transcripts.

[Windows process metadata](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-process)
provides broad execution observations but cannot independently prove agent,
project, or artifact ownership. Shared servers and detached work need explicit
associations. WSL, containers, elevated processes, and remote work require named
coverage boundaries.

## Hygiene and preservation

Inventory plus preservation review is the smallest coherent first-scope
candidate; the first release is still a human decision. Names, age, clean Git
status, and completed agent turns do not establish disposal permission.

[Git worktree documentation](https://git-scm.com/docs/git-worktree) distinguishes
registered/locked worktrees and removal behavior. Review unique commits,
untracked/ignored artifacts, required evidence, active use, and unavailable scope
before proposing removal. Project-specific preservation checks remain binding.

The completed cleanup-policy ticket now owns the human rulings: completed linked
worktrees and proven regenerable material may be proposed; unique/required
material is preserved; recovery copies have a minimum 30 days before review,
without timed automatic deletion; approval means approve-and-run after fresh
checks. Ordinary filtered observation history has its own 30-day automatic expiry.
The build plan carries the verification design; no cleanup has been exercised.

## Discriminating evidence still needed

1. Synthetic observation across each supported Codex/Claude source, including
   sessions started outside Workbench and missing/disconnected sources.
2. Process identity and shared-service cases; source-native interruption versus
   confirmed workload exit.
3. Synthetic file traversal with links, hidden entries, concurrent changes,
   denied access, and private metadata.
4. Cleanup proposal invalidation after target changes and preservation of unique
   work and required evidence.
5. Independent queue clients and crash recovery, if queueing enters the selected
   scope; a local SQLite WAL file is not multi-machine coordination.
   [SQLite WAL documentation](https://sqlite.org/wal.html).

The implementation plan should select these experiments based on the first
release scope, rather than assume every proposed build belongs in that release.

# Acceptance and evidence matrix

This is an implementation verification contract. No row is marked passed merely
because a design, help command, issue resolution or source inventory exists.

| Requirement | Slice | Demonstration required |
| --- | --- | --- |
| C#/.NET + Blazor, exact tooling | S0 | Fresh Windows x64 environment uses pinned SDK/packages, locked transitive restore, build/format/tests; upstream source blobs and compatibility/discovery readback verified |
| PC-wide and cross-project view | S0-S2 | Inventory the actual Codex/Claude entrypoints; externally launch synthetic work in multiple projects; show supported semantic coverage and explicit gaps, not only Workbench jobs |
| Existing Codex live owner | S0 | Connect through a supported Windows route to the actual owner; distinguish loaded/current work from another server's stored history; failure blocks a coverage claim |
| Claude state and control | S0, S2, S4 | Synthetic interactive/background sessions and source roots; supported state schemas and exact background stop effects; no transcript API fallback |
| Distinct identities | S1-S2 | Multi-agent host, shared helpers, PID reuse, exited parent, source restart and late messages do not merge jobs, sessions, subagents and processes |
| Unknown and stale behavior | S1-S2 | Denied access, missing source, malformed output, disconnect and dropped refresh retain source/time/reason; no invented zero, idle or completed state |
| Same truth for CLI and dashboard | S1 | Both use the authoritative host; clients cannot share-write its store or bypass module decisions; reconnect/resnapshot preserves operation identity |
| Browser-independent collection | S1, S5 | Close and reopen the browser during observation and a fixture operation; collection and accepted operation continue |
| Accepted-run lifetime | S1, S4 | Close browser/cancel request after durable acceptance, lose the acknowledgement and retry the same request; exactly the same run is returned and no second execution starts |
| Structured summaries and privacy | S1-S2 | Scoped publishers and project rules; synthetic protected strings in rejected fields never reach store, index, logs, errors, live messages or UI; no raw envelopes or transcript reads |
| Verified external controls | S0, S4 | Exact source/work identity and permissions checked; unrelated work stays untouched; request/acknowledgement/outcome/process-exit are separate evidence |
| Human cleanup review | S3-S4 | Exact immutable manifest/actions/recovery terms visible; drone route cannot approve using ID/Boolean/actor flags; authenticated dashboard action is bound and consumed once |
| Freshness and race handling | S3-S4 | Replace/rename/change targets or policy after review and near mutation; either tested identity guarantees hold or candidate is blocked/needs review; no substitute target |
| File scan correctness | S3 | Hidden/system files, junction/loop, reparse tag, hard link, sparse/compressed file, long/Unicode path, concurrent mutation, denied root and interrupted scan have declared accounting and coverage |
| Worktree preservation | S3-S4 | Active/locked/dirty/unique-commit/ignored/untracked/evidence cases; primary checkout protected; required Git objects and project material genuinely reconstruct a fixture worktree |
| Selective copying | S3-S4 | Omit only proven regenerable material; unknown classification or failed preservation blocks action; copy/manifest verification precedes destruction |
| Crash and partial cleanup | S4 | Inject failure before/after every external action and lost receipt; reconcile actual state, show per-target partial/unknown results, never blindly replay deletion |
| Recovery | S4 | Restore synthetic unique bytes and Git worktree state into a collision-free location; verify hashes, object availability and required evidence; refuse overwrite |
| Recovery retention | S4-S5 | With a controllable clock, fewer than 30 days is held; at/after 30 days becomes review-eligible only; no timer deletes copies or required evidence |
| Ordinary history retention | S2, S5 | Searchable for 30 days then unavailable through supported views; remove index/cache/ordinary update payload copies; stopped worker/resnapshot cannot resurrect expired history |
| Retention separation | S2-S5 | Ordinary expiry cannot cascade into approval/execution receipts, preserved files, reconstruction objects or recovery material; stricter project rules remain effective |
| Sign-in and explicit stop | S5 | User-context launch; one intended collector; stop intent persists across supervisor failure/sign-in and stays stopped until resume |
| Out-of-host resume and stop in flight | S1, S4-S5 | Host/web endpoint stopped: surviving supervisor reports status and accepts authorized operator resume; drone reporting scope cannot resume; active cleanup reaches a safe checkpoint or reports pending/unknown and requires reconciliation |
| Unexpected exit recovery | S1, S5 | Bounded delayed retries, source/host failure distinction and visible retry exhaustion; interrupted mutation enters reconciliation first |
| Responsiveness | S1-S5 | Reproducible idle, busy and unavailable-source measurements; expensive scans yield while lightweight observation remains useful; quantify settings after measurement |
| Local control endpoint | S1, S4 | Loopback-only web and local user-scoped CLI transport; authentication/origin/anti-forgery/route restrictions; no arbitrary shell dispatch or executable result links |
| Packaging/update | S5 | Pinned artifact installs without changing unrelated projects; update preserves state, retention and stopped intent; uninstall/export behavior is documented and approved separately |
| Public source boundary | Every slice | Only source, synthetic fixtures, sanitized evidence and pins are committed; private runtime roots remain outside the checkout and public CI |

## Test organization

- Test domain outcomes through module interfaces with controlled clocks and
  fixture adapters. Avoid tests that only mirror routing or implementation calls.
- Use isolated synthetic filesystem/Git trees and disposable workload identities.
  Tests never use this PC's real clinical records, sessions or live worktrees as
  destructive fixtures.
- Run focused checks after each slice and the full project verification at the
  release boundary. Review failure/coverage receipts as well as clean exits.
- Keep the independent design/implementation review separate from the author and
  record exactly what was actually exercised.

## Evidence provenance

Already observed: complete pinned Pocock/PStack blob verification; published SDK
release metadata; direct package identities/hashes; installed CLI version/help;
one filtered Claude status query. Not observed: .NET SDK installation or full
locked restore; native discovery of updated engineering adapters; existing Codex
desktop attachment; synthetic cancellation, cleanup, restoration or performance.

The implementation handoff must preserve that distinction. A failed required
coverage gate returns a concrete product trade-off to the human before narrowing
scope. It does not authorize private DB inspection, global hooks, new privileges
or forceful process/file operations.

# Cross-review of the Codex coverage evidence

Date: 2026-11-12. Reviewer: the Claude session that ran the
[Claude coverage readiness](claude-coverage-readiness.md) work. Baseline:
`main` at `07bfd63`. This is an independent read of the Codex reports and
receipts, plus one filtered read-only Windows snapshot. It does not rerun the
Codex transport prototype, connect to any Codex endpoint, or read Codex
history.

Reviewed:

- [Codex readiness after the foundation](codex-post-foundation-readiness.md)
- [Codex existing-owner route review](codex-owner-route-review.md)
- [current-owner receipt](codex-current-owner-receipt.json) and
  [.NET 11 transport receipt](codex-net11-transport-receipt.json)
- The earlier [Codex results](codex-ticket7-results.md),
  [connection diagnosis](codex-connection-diagnosis.md) and
  [combined assessment](ticket7-combined-assessment.md)

## Verdict

**The Codex conclusions hold. No supported route reaches the existing desktop
owner, and the owned-server transport proof does not substitute for it.** The
receipts support each claim the readiness report makes. Four findings below
narrow or extend that evidence. None changes the open status of the
existing-owner gate.

## Independent snapshot

Process names, parent names, path classes, the subcommand token, option names
and pipe-name shapes were kept. Command-line values, PIDs and paths were not.

| Check | Result | Agrees with Codex evidence |
| --- | --- | --- |
| App-server child of `ChatGPT.exe` | 1, options `--analytics-default-enabled` only | Yes |
| TCP listeners owned by any Codex process | 0 | Yes |
| Desktop-bundled `codex.exe --version` | `codex-cli 0.167.0-alpha.4.1` | Yes. The owner's binary matches the cited CLI version |
| Other `codex.exe` child of `ChatGPT.exe` | 1, with `--remote`, `--environment-id` and `--concurrent-requests` | Not mentioned. See finding 1 |
| Named pipes containing `codex` | `codex-ipc`, `codex-app-tools-*`, `codex-browser-use-*`, `codex-computer-use-*` and a sandbox pipe | Not mentioned. See finding 2 |
| `codex` on `PATH` in this session's shell | No | Environment difference only. The Codex reports used their own shell |

## Findings

1. **A second desktop-owned Codex process appears to serve a remote
   environment.** It uses the same binary as the app-server and carries
   `--remote` and `--environment-id`. Its role is inferred from those option
   names and was not otherwise checked. The current-owner receipt counts app-servers only, so
   its count of 1 is correct. The coverage table has no row for desktop-managed
   remote or cloud environment work. Add it as a separate untested entrypoint.
   Process presence is not semantic coverage of that work.
2. **Undocumented named pipes exist.** "Default control socket absent" stays
   true, because none of these pipes is the documented daemon socket. They are
   not a supported attachment route, and connecting to them would be private
   integration rather than a documented client. Record them in the next owner
   inventory so a later reader does not take "no default socket" to mean "no
   local IPC endpoints".
3. **The owned server's exit code is consistent with forced termination.** Both
   transport runs record `owned_server_exit_code = -1`. The readiness report says
   the server's exit was confirmed after teardown, which is accurate. It does not
   say the exit code was -1. Graceful server shutdown is unverified, just as graceful
   WebSocket closure is.
4. **The two isolation props files share one hash.** `Directory.Build.props`
   and `Directory.Packages.props` both hash to `17284cb7...`. That fits identical
   stub files that block inherited repository settings. The receipt should say
   so, because the harness is not public and a reader cannot check.

## Points confirmed without change

- An owned server with a fresh home is a different owner from the desktop
  app-server, even when it shares saved history. The proposed contract keeps
  them as separate source instances.
- Command handles are connection scoped and expire on reconnect. The repeated
  empty snapshots do not prove active-work replay or reconciliation.
- The route review's bounded claim is correct. It covers the inspected
  documentation and installed help, not every version or future owner
  integration.
- The prototype harness is not in the public checkout, which the report
  discloses. The receipt is therefore not a fresh-clone rerun command.
- No independent Codex CLI app-server was running during this snapshot, so the
  "existing independent CLI work" row could not be checked here.

## Effect on the shared ticket

Claude's source coverage does not establish Codex owner access. The next Codex
step is unchanged. It needs a supported owner-side exposure or a reporting
design that reaches the desktop owner, the independent CLI owners and the
remote-environment process from finding 1. Choosing between those, or changing
the coverage promise, is an owner decision.

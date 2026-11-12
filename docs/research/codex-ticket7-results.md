# Ticket 7: Codex feasibility evidence

Date: 2026-11-12. Installed CLI: `0.167.0-alpha.4.1`.
Scope: Codex half of [the source-coverage experiment](https://github.com/mshamblin5150-code/drone-workbench/issues/7).
Claude's half remains for its independently assigned task.

## Result

**Partial feasibility demonstrated, existing desktop coverage unverified.**
An isolated, unauthenticated-to-model-services app-server accepted the documented
stdio initialization sequence and safely controlled a synthetic process. This
does not demonstrate observation or control of desktop-owned Codex work, a model
turn, subagents, or externally launched CLI sessions. Ticket 7 must remain open.

## What was exercised

The CLI generated its non-experimental JSON schema bundle locally. The receipt
records its file count and a combined inventory hash, plus the executable hash.
No SDK/package/global skill update was needed for this experiment. A disposable
standard-library Python client drove the protocol; it is research tooling, not a
selected Python product component or a production adapter.

Each test server used a dedicated `CODEX_HOME`, no copied credentials/config,
removed API-key environment variables, and a private probe working directory.
No `turn/start`, model generation, real thread-history query, real job control,
cleanup, or global hook/config installation was performed. Explicit gateway OAuth
was selected during initialization to avoid automatic authorization. This is not
a network-isolation test or proof of zero background network traffic.

| Check | Observed result | Meaning |
| --- | --- | --- |
| Method before initialize | `Not initialized`, code -32600 | Client handshake is enforced |
| Initialize then initialized notification | Initialize returned platform/user-agent/home fields | Stdio protocol works against this newly owned server; actual local values were discarded |
| Duplicate initialize | `Already initialized`, code -32600 | Initialization is connection-specific and single-use |
| Loaded-thread snapshot | Empty list | Isolated server has no loaded jobs; does not establish access to desktop jobs |
| Interrupt invented thread/turn IDs | `thread not found`, code -32600 | Unknown target rejected; not evidence of successful turn interruption |
| `command/exec` synthetic sleeper | Streamed PID observed; read-only sandbox policy requested | A model-free command can be observed on this server; effective sandbox enforcement was not audited |
| Terminate unrelated command ID | Rejected; actual sleeper remained running | Wrong command identity did not stop this fixture |
| Terminate correct command ID | Empty success response, followed by terminal exec response | Acceptance and terminal response were recorded separately |
| Independent Windows process handle | Unsignaled before, signaled after termination | The exact observed sleeper process exited; no descendant-tree claim |
| Repeat terminate | No active command for the ID | Repeat request was rejected after exit; no durable exactly-once/retry guarantee established |
| Stdio-server teardown | Exit 0 | Owned stdio fixture shut down after the command had ended |

The synthetic process printed its PID and slept; it performed no user-file
mutation. Its PID was consumed in memory and omitted from published evidence.
The command protocol's `processId` is a **connection-scoped client identifier**,
not a Windows PID, thread ID, or durable Workbench job identity.

## Existing owner and additional transport attempts

- The installed daemon version diagnostic failed to connect to its documented
  default control socket, reporting Windows OS error 10050. The expected socket
  path was absent at observation time. The direct proxy initialization attempt
  also ended without a response. This does not prove that the desktop app has no
  internal server or that every Windows installation has the same limitation.
- A filtered process-metadata observation found an app-server invocation without
  an advertised `--listen` or `--sock` argument. No raw command line, process ID,
  private path or session content was retained. That observation is not discovery
  of a supported external connection endpoint.
- An isolated custom Unix listener initially rejected a directory that was not
  private to the current user. A separate probe-only directory was created with
  a protected current-user ACL. No existing user/project directory permissions
  were changed. The listener then stayed running and created its socket.
- A proxy against that custom socket did not answer initialize within the
  20-second bound. The fixture proxy and listener were then stopped. Reconnection
  and cross-connection controls were not exercised. A socket file and running
  listener alone do not establish a usable protocol connection.
- No TCP/WebSocket listener was used as a fallback. Official documentation marks
  that transport experimental and unsupported. No desktop restart, daemon
  bootstrap, remote-control enablement, socket guessing or private database/
  rollout inspection was attempted.

Codex desktop tools exposed to this chat are a different integration surface.
Their ability to list/wait on chats is not proof a standalone C# process can call
them. A second server's history likewise cannot establish live owner access.

## Workbench consequences and remaining Codex checks

1. Keep source connection, session, turn, subagent, Workbench job and OS process
   identities separate. A connection-scoped command ID must not survive as an
   executable control target after reconnect without reconciliation.
2. Supported observation of existing desktop/CLI owners is still a blocking
   coverage gate. Do not narrow PC-wide scope to only Workbench-launched jobs.
3. A synthetic `command/exec/terminate` result is not `turn/interrupt` proof.
   Matching/mismatched turn steering, actual turn interruption, subagents and
   descendant lifetime need a reachable owning harness and a separate synthetic
   model-turn experiment. No model-account credential change was made here.
4. Lost acknowledgements/events, source restart, disconnect during active work,
   stale-state projection, resnapshot and reconnect authorization remain untested.
   Closing the fixture after completed work does not exercise these cases.
5. Workbench publication permissions, field filtering, cleanup-approval isolation
   and lifecycle-stop persistence are acceptance requirements, not features
   demonstrated by this protocol probe.
6. Combine Claude's independently observed results with these findings before
   deciding whether more supported endpoint discovery, a vendor integration, or
   an explicitly approved scope change is needed. Do not grant new control powers
   or change the product promise to make this report appear complete.

## Sources and receipt

- [Official app-server protocol and transport](https://learn.chatgpt.com/docs/app-server#protocol).
- [Official initialization sequence](https://learn.chatgpt.com/docs/app-server#initialization).
- [Official API overview](https://learn.chatgpt.com/docs/app-server#api-overview).
- Installed CLI help and its generated parameter/notification schemas; runtime
  JSON-RPC replies and Windows process-handle observations described above.
- [Sanitized probe receipt](codex-ticket7-receipt.json). Receipt contains selected
  outcomes, hashes and synthetic target labels; no raw protocol or source logs.

The earlier local file `codex-session-interface.md` remains an independent
self-description and is not published by this task. This report adds observed
experiments rather than retroactively turning all its protocol candidates into
verified features.

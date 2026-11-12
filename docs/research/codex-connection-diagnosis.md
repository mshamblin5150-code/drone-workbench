# Codex connection diagnosis

Date: 2026-11-12. Follow-up to [ticket 7](https://github.com/mshamblin5150-code/drone-workbench/issues/7).

**Resolved: the isolated Unix-listener handshake failure. Still unresolved:
supported external access to the existing desktop-owned server.** These are two
different problems; fixing one does not establish the other.

## Reproduced failure and cause

A bounded reproducer started an isolated app-server with `--listen unix://PATH`
in a protected current-user socket directory. Sending JSONL initialize through
`codex app-server proxy --sock PATH` received no response within three seconds.
The listener remained running and its socket existed. This reproduced the
earlier failure without a model turn, real session or SDK installation.

Three hypotheses were considered: a protocol mismatch, blocked Windows client
socket support, or rejection/stalling of app-server initialization.

A controlled Unix-socket peer then inspected the actual installed proxy's first
bytes, without retaining their contents. The proxy forwarded the supplied JSON
directly, **without an HTTP Upgrade handshake**. A synthetic JSON response from
that peer came back through the proxy's stdout. Thus the proxy is a byte relay;
it does not by itself convert a JSONL client into a WebSocket client.

The documented Unix listener expects a WebSocket HTTP Upgrade followed by
WebSocket frames. The original probe combined incompatible wire protocols. The
timeout was not evidence that this listener cannot work on Windows.

## Corrected connection and verification

A small C# client compiled inside PowerShell used:

- `Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)`;
- a `UnixDomainSocketEndPoint` for the probe-owned socket;
- `SocketsHttpHandler.ConnectCallback` returning an owning `NetworkStream`;
- `ClientWebSocket.ConnectAsync` with that handler, performing the WebSocket
  Upgrade over the Unix socket;
- one initialize request, initialized notification and `thread/loaded/list`
  request per connection.

Both connections to the same isolated server completed initialization and read
an empty loaded-thread snapshot. The first client disconnected before the second
connected. The differential reproducer was run again: the JSONL/proxy path still
timed out and the .NET/WebSocket path passed on both connections. A separate
wire-format test confirmed the proxy's raw-JSON behavior.

This establishes Windows Unix-socket transport and repeated empty-state metadata
connections for an explicitly exposed, newly owned server. It does not establish
event replay, active-work recovery, preserved subscriptions, cross-client command
ownership, or any real-session observation.

The tested C# compilation/runtime was PowerShell 7.6.9 on .NET 10.0.14. It is
not a build with the planned .NET 11 SDK and does not satisfy that separate
reproducibility gate. The installed Python runtime lacks `socket.AF_UNIX`; an
attempt using it failed locally before connection. That limitation must not be
generalized to the Windows OS or the selected C#/.NET stack.

## Existing desktop owner: separate findings

A filtered Windows process observation found one existing app-server owned by
`ChatGPT.exe`. Its only observed long option was
`--analytics-default-enabled`; it advertised neither `--listen` nor `--stdio`.
The documented default is stdio. No TCP listening endpoint was found for that
process. Raw command lines, PIDs, session content and private paths were not
retained or published.

The documented default control socket was absent. A direct .NET WebSocket client
attempt at that exact path failed at the **connect** stage with Windows socket
error 10050, before initialization. This is a local observation with an absent
endpoint, not proof that Windows sockets generally fail or that no other
configuration could expose a server.

No documented external attach route to this currently running stdio-owned
desktop server was identified in the inspected official guidance and installed
CLI help. A new socket listener must be exposed by its owning server; a client
cannot turn another process's private stdio into that listener merely by knowing
its PID. Starting a separate daemon would establish a different owner and would
not resolve this live-owner evidence requirement.

No desktop/server restart, launch-argument modification, daemon bootstrap,
remote-control enablement, process injection, private database inspection or
TCP-port probing was performed. Only owned fixture processes were stopped.

## Effect on the plan

- Use the demonstrated .NET WebSocket-over-Unix approach for explicitly exposed
  Codex sources. Do not use the bare JSONL proxy as a transport conversion layer.
- Keep the existing-desktop Codex source marked unavailable/unverified for
  semantic observation and controls. OS process presence is useful but does not
  establish thread/turn/subagent state.
- Existing desktop coverage now needs a supported owner-side exposure or an
  explicitly designed reporting integration. Neither has been configured or
  proved by this diagnosis. A reporting integration would also need to prove
  liveness and external-session coverage; voluntary model summaries alone do not
  satisfy those requirements.
- Preserve the confirmed PC-wide goal. Changing desktop launch behavior, adding
  a bridge, or narrowing coverage is a separate concrete implementation/policy
  choice, not an implicit consequence of this transport fix.
- Keep ticket 7 open. Active-work, model-turn/subagent, failure-recovery and
  existing-owner coverage gates remain as listed in the combined assessment.

## Evidence

- [Sanitized diagnosis receipt](codex-connection-diagnosis-receipt.json).
- [Official protocol and transports](https://learn.chatgpt.com/docs/app-server#protocol).
- [Official developer commands](https://learn.chatgpt.com/docs/developer-commands#codex-app-server).
- Installed CLI help, a controlled proxy wire-format experiment, the actual
  server/client responses, and filtered process/listener metadata.

This follow-up supersedes the transport uncertainty in the historical
[Codex experiment](codex-ticket7-results.md); its original receipt is preserved.

# Codex existing-owner route review

Date: 2026-11-12. Installed CLI checked with `codex --version`:
`0.167.0-alpha.4.1`. Scope: documentation and installed help only.

## Overview

No supported external attachment route to the previously observed desktop
stdio owner was identified. A standalone C# client can use an endpoint that the
owning server exposes. A successful connection to a newly launched fixture does
not establish access to that desktop owner. The PC-wide observation requirement
remains unchanged.

This review adds no live source observation, connection attempt, model turn,
control operation, daemon configuration, or desktop restart. The separate .NET 11
transport experiment owns its runtime evidence.

## Key concepts

The owner is the app-server instance holding live work. A transport is a route
into that instance. Another server with the same saved history is still another
owner. A process association does not create protocol access or control authority.
The proposed [source contract at the reviewed proposal revision](https://github.com/mshamblin5150-code/drone-workbench/blob/5dad039194ff54ddc4e39e833007f6f91eda37b6/docs/plan/source-contract.md)
preserves these distinctions. That proposal remains separate from production code.

## How connection works

The official protocol defines JSONL over default stdio, WebSocket framing over
an exposed Unix socket, and an optional TCP WebSocket listener. The Unix route
requires an HTTP Upgrade handshake. TCP WebSocket transport is explicitly
experimental and unsupported. These are server launch choices, not a documented
operation that converts an existing private stdio connection into an endpoint.
[Official protocol](https://learn.chatgpt.com/docs/app-server#protocol).

The inspected client commands do not remove the endpoint requirement:

| Candidate | Documented role | Consequence for this gap |
| --- | --- | --- |
| Interactive `--remote` and `codex agents --remote` | Connect to an exposed local or remote server | Require a reachable owner endpoint first |
| `codex remote-control` | Managed background-server remote access and pairing | Not a replacement for the app-server listener when building a local protocol client |
| `codex queue` | Deliver a message and optionally resume work | Not a read-only semantic observation channel; delivery and resume can change work |

[Official developer commands](https://learn.chatgpt.com/docs/developer-commands#command-details).
Installed `app-server proxy --help` describes a byte proxy to a control socket.
Installed daemon help lists lifecycle and configuration operations; it advertises
no private-stdio adoption command. This help review did not execute those operations.

The conclusion is bounded: the reviewed documentation and installed help identify
no permitted attachment method under the current constraints. They do not prove
that every version or future owner integration lacks one.

## Where the evidence lives

The earlier [connection diagnosis](codex-connection-diagnosis.md) observed a
desktop-owned server without an exposed listener argument, no TCP listener for
that process, and an absent documented default socket. Its successful Unix
connections targeted an owned fixture. Those are historical observations from
the same CLI version, not a fresh inventory by this review. The
[combined assessment](ticket7-combined-assessment.md) retains the separate
existing-owner, active-work, recovery, and Workbench acceptance gates.

## Remaining decision

The next integration design must identify a supported owner-side exposure or
reporting bridge and show that it reaches the actual desktop and independent CLI
owners. Any proposed launch change, listener, bridge, or hook needs its concrete
scope reviewed before activation. Intentional agent summaries alone do not prove
continuous liveness or complete external-work coverage.

Until that route is established, desktop semantic observation and controls remain
unverified. An explicit product decision would be required to change the coverage
promise. Claude's independent evidence can complete its own source assessment,
but cannot establish Codex owner access.

Throughput checkpoint: n/a, read-only investigation. The parent verified the pinned
engineering closure before this review. This supported helper provides a fresh
research context, not the unavailable default cross-model panel.

# Ticket 7: Claude feasibility evidence

Date: 2026-11-12. Installed Claude Code CLI: `2.1.312`. Claude desktop app:
`5.1231.0`. Scope: the Claude half of
[the source-coverage experiment](https://github.com/mshamblin5150-code/drone-workbench/issues/7).
The Codex half is recorded in [codex-ticket7-results.md](codex-ticket7-results.md).

## Result

**Supported observation and control of background sessions were demonstrated.
Interactive and desktop sessions can be observed but were not controlled.**

The documented agent view (`claude agents --json`) listed externally started
interactive sessions, including desktop-owned ones, alongside
background sessions. The agent view is the documented status route; the CLI
background commands are its control route. Through them, a synthetic background
session was started, observed, stopped (with the exit of its exact process tree
confirmed independently) and removed. Unknown targets were rejected. Stopping a
session is idempotent. Killing a session process externally made the background
supervisor restart it, and the agent view's `state` field went stale during the
restart.

This does not demonstrate control of interactive or desktop sessions, model
turns, subagents, or turn-level interruption. Ticket 7 stays open.

## Bounds that were kept

- No model turns. Each synthetic background session was started without a
  prompt, so it stayed idle waiting for input, and was stopped before it was
  ever given a prompt.
- Real sessions were left alone. Two real interactive desktop sessions were
  running, including the one doing this work. They were only observed, and only
  through filtered metadata. No stop, attach, logs or rm command was sent to
  them.
- No global harness changes. The user settings file, which holds an existing
  user `Stop` hook, was neither modified nor loaded by the hook experiment.
  The hook recorder was supplied only through a per-session `--settings` file,
  with `--setting-sources ""` and `--strict-mcp-config`.
- Nothing raw was kept. No transcripts or prompts were read. The recorded
  values were limited to agent-view field names, kinds, statuses and states,
  hook event names, key names, and the `source`/`reason` enum values. PIDs,
  paths, names and session IDs were compared in memory and left out of this
  report.
- Everything was cleaned up. All three synthetic sessions were removed with
  `claude rm`, and the background supervisor exited by itself once none were
  left.

## Entrypoint matrix

| Entrypoint | Present | Observed via agent view | Control route |
|---|---|---|---|
| Desktop app Code sessions | Yes | Yes, as `interactive` | Owned by the app over stdio stream-json; no external stop route tested |
| Native CLI interactive | Installed | Expected as `interactive` (no separate synthetic run) | Not tested |
| Native CLI background (`--bg`) | Yes | Yes, as `background` | `claude stop` / `rm`, verified on synthetic sessions |
| IDE extensions (VS Code, Cursor) | None found | — | — |
| Cloud / remote-control sessions | Not inventoried | Not tested | Not tested |

The desktop app runs each Code session as a child `claude.exe` with the
stream-json formats and a stdio permission-prompt tool. The agent-view PID of
the session doing this work matched that child process. A second desktop-owned
session also matched its own child process.

## What was exercised

| Check | Observed result | Meaning |
|---|---|---|
| Agent-view schema (interactive) | `cwd, kind, name, pid, sessionId, startedAt, status` | Externally started and desktop-owned sessions are observable. `cwd` and `name` are private fields to filter before persistence |
| Agent-view schema (background) | Adds `id` (short ID) and `state` | The short ID equals the ID printed by `--bg` and prefixes `sessionId` |
| `--bg` with no prompt | Printed `idle — send a prompt to start`. Status `idle`, state `blocked` | Gives a model-free synthetic target. A per-user background supervisor process starts as well |
| Just after start | Entry present but without `pid`/`status` for a few seconds | Workbench needs a "starting" projection, not "dead" |
| Stop with an unknown ID | `No job matching …`, exit 1. The synthetic session stayed alive | Unknown target rejected |
| Stop with the correct ID | `stopped <id>`, exit 0. Session process exited within about 0.85 s of the stop. In a later run, process handles held by the probe confirmed the exit of both the session and its PTY host | Acceptance and the actual exit of the process tree observed separately |
| Agent view after stop | Gone from the default list. With `--all`: `state=stopped`, no `pid`/`status` | Stopped sessions are visible only with `--all` |
| Repeat stop | `stopped <id>`, exit 0 | **Idempotent success, not rejection** (unlike Codex). Workbench cannot use the stop result to tell "I stopped it" from "it was already stopped" |
| `rm`, then repeat `rm` | `removed`, then `No job matching`, exit 1 | Removal is final |
| External kill of the session process | Supervisor restarted it under the same short ID with a new PID within about 15 s. During the gap: no `pid`/`status`, but `state` still read `blocked` | **`state` is stale during a crash or restart window.** PID is not a stable identity. A crash is not an end |
| Supervisor lifetime | Exited within about 20 s after the last session stopped | No lingering daemon after cleanup |
| SessionStart hook | Fired on start (`source=startup`). Keys: `cwd, hook_event_name, model, scratchpad_dir, session_id, session_title, source, transcript_path` | Works for prospective metadata. Includes the transcript path and title, so filter them |
| Hooks after an external kill | **No SessionEnd.** The restart emitted another SessionStart with `source=startup`, not `resume` | A crash is invisible to hooks except as a second start. Pair hooks with agent-view polling |
| SessionEnd hook on `claude stop` | Fired with `reason=other` | A clean stop is detectable, but the reason does not identify Workbench as the stopper |

## Coverage gaps (not passes)

1. **Controlling interactive and desktop sessions.** Not tested, to avoid
   touching real work. Desktop sessions are driven by the app over stdio, and
   no supported external interrupt or stop for them was identified. In-app
   session-management tools exposed to this chat are a different integration
   surface, and are not proof that a standalone C# process can use them.
2. **Model turns, subagents and turn interruption.** Not exercised. Agent view
   lists sessions only. It did not list subagents, and none existed.
3. **Busy-session stop.** `stop` was only applied to idle synthetic sessions.
   Stopping mid-turn, the effect on tools and background commands the session
   had started, and descendant-tree cleanup under load are untested.
4. **Observer disconnect.** Agent view is a one-shot pull with no connection
   to lose. Workbench restart therefore means a fresh re-query, which was
   exercised implicitly by every poll. A desktop-app restart and a session
   being resumed elsewhere were not tested.
5. **Name-based targeting.** `attach` and `logs` accept part of a session name.
   Workbench must target exact short IDs only. This hazard was not
   exercised.
6. **Other configuration roots, cloud sessions, WSL and elevated contexts.**
   Not inventoried.

## Workbench consequences

1. **Identity.** Keep the short ID or `sessionId` as the source identity. PID
   changes when a session restarts. A desktop session's identity is shared with
   the app that owns it.
2. **Liveness.** Combine agent-view polls, matching against Windows processes,
   and hook events. None of them is enough alone: `state` is stale during a
   restart, hooks miss crashes, and a stop result is not proof of who stopped
   the session.
3. **Control authority.** The only verified external control is for
   `background` sessions. Do not extend stop controls to interactive or desktop
   sessions, or to name matches, without a supported route.
4. **Filtering.** Before persistence, drop `cwd`, `name`, `session_title` and
   `transcript_path`, or map them through private local storage.
5. **Before claiming PC-wide coverage,** reconcile these findings with the Codex
   gaps (existing desktop-owner reach is unverified on both sides for
   control).

## Receipt

[claude-ticket7-receipt.json](claude-ticket7-receipt.json) contains sanitized
outcomes only. The earlier `claude-session-interface.md` is an independent
self-description. It was not verified by this experiment and remains
unpublished.

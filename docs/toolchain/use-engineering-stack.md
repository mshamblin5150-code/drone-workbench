# Use the pinned engineering stack

Run these commands from the repository root in PowerShell. The installer changes
only this checkout and its ignored `.local/engineering-stack` directory.

## Check prerequisites

Use PowerShell 7.4 or later and Git with `clone --revision` support. Verification
was exercised with PowerShell 7.6.9 and Git 2.54.0.windows.1. Put `git.exe`,
`codex` and `claude` on `PATH` for the native inspection steps. The observed
harness versions are Codex CLI 0.167.0-alpha.4.1 and Claude Code 2.1.312. Parser
changes in another version can make inspection fail rather than certify it.

Installation uses public HTTPS Git sources at exact commits. It does not need
an API key or run a model turn. Native metadata inspection also runs no model
turn. A real Claude task uses your existing Claude authentication. No SDK 11
installation is required for this tooling, and this is not an SDK 11 build test.

## Install and verify

```powershell
pwsh -NoProfile -File tools/engineering-stack.ps1 -Action Install
pwsh -NoProfile -File tools/engineering-stack.ps1 -Action Verify
```

Installation clones the locked revisions, retains complete package resources,
and generates namespaced Codex skills plus a catalog. Verification must report
`verified: true`, four packages and 103 skills for this lock. Source presence
alone enables no automation or hook.

If you already have fully hydrated copies of both public source repositories,
you can import their Git objects instead of downloading them. The directory must
contain `pocock` and `pstack` checkouts with all locked package objects present.
The importer copies public Git objects into fresh owned repositories. It does
not inherit source hooks, configuration, worktree state or object alternates.
A commit-only partial clone is insufficient. Omit `SourceRoot` to use the tested
fresh HTTPS installation path.

```powershell
pwsh -NoProfile -File tools/engineering-stack.ps1 -Action Install -SourceRoot C:\path\to\source-pins
```

If verification finds drift, inspect it before repair. Installation refuses to
overwrite modified generated files or unrelated skill content. It does not use
reset or cleanup to make verification green.

The installer supports a first installation and repeated runs of the same lock.
It does not upgrade an existing environment to a different lock. Version changes
need a separate reviewed migration of sources and generated artifacts.
New source checkouts are verified in staging before publication. Failed staging
directories and attempt receipts remain under `.local/engineering-stack` for
inspection. A retry can proceed without treating them as complete installations.
An incomplete final checkout made by an older tool still requires explicit
inspection and repair. No source directory is removed automatically.

## Inspect both native harnesses

```powershell
pwsh -NoProfile -File tools/engineering-stack.ps1 -Action InspectCodex
pwsh -NoProfile -File tools/engineering-stack.ps1 -Action InspectClaude
```

Codex starts an owned temporary stdio server with an isolated Codex home. It
queries project skill metadata and checks the expected entrypoint names and
paths. It never queries real thread history or starts a model turn.

Claude loads the exact plugin directories for each metadata command. It verifies
the native inline plugin source, skill names and agent names. Its expected
component sets come from the catalog and retained agent definitions.

Private inspection receipts live under `.local/engineering-stack/inspection`.
Metadata inspection does not prove that every workflow or model action works.
Read [the capability record](engineering-capabilities.json) for unsupported and
dormant steps.

## Start Claude with the pinned plugins

```powershell
pwsh -NoProfile -File tools/engineering-stack.ps1 -Action StartClaude
```

The launcher verifies sources first and then loads the complete Pocock, generated
PStack Claude, and Team Kit plugins for that session. Use native namespaces such
as `/pstack:poteto-mode` and `/mattpocock-skills:wayfinder`. Global installations
and update settings stay unchanged. Do not add a second set of Claude wrappers.

This launcher proves a CLI loading route. An already open Claude desktop session
does not automatically acquire these plugin arguments. The repository's
`CLAUDE.md` points to `AGENTS.md` for project workflow instructions, but that is
separate from native desktop plugin registration.

## Check the verifier and source helper

```powershell
pwsh -NoProfile -File tools/engineering/test-stack.ps1
node .local/engineering-stack/sources/pocock/scripts/sync-plugin-version.mjs --check
```

The verifier test checks repeated installation and verification. It temporarily
changes one generated fixture, confirms detection and refusal to overwrite, then
restores the exact original bytes in `finally`. Run it in an isolated setup
worktree with no other writer. The Node helper checks source/plugin version
agreement without writing. Node 26.13.0 was used for that optional helper.

Native Codex inspections retain their isolated homes under the private inspection
directory. These are identified test artifacts, not production Workbench history
or untracked source. Review them with the setup receipts before disposal.

## Exercise one harmless Claude workflow

This optional check uses one model turn with your existing Claude account. It
provides no file or command tools and does not save a resumable session.

```powershell
$sources = Join-Path (Get-Location) '.local/engineering-stack/sources'
$plugins = @('--plugin-dir', (Join-Path $sources 'pocock'),
             '--plugin-dir', (Join-Path $sources 'pstack/harness/pstack/claude-code'),
             '--plugin-dir', (Join-Path $sources 'pstack/cursor-team-kit'))
claude --setting-sources project --settings '{"disableAllHooks":true,"autoMemoryEnabled":false}' `
  --strict-mcp-config --permission-mode plan --tools "" --no-session-persistence `
  --output-format json @plugins -p '/pstack:unslop Rewrite this synthetic fixture in plain words. Output only the rewritten sentence. In order to utilize this tool, it is important to note that you should simply click Start.'
```

The recorded check succeeded and returned `To use this tool, click Start.`
Another wording can also pass. Check successful execution and removal of the
fixture's filler, not byte equality of model prose. Hook disabling is a
[session-only settings override](https://code.claude.com/docs/en/hooks#disable-or-remove-hooks).
No hook configuration file is edited. `--bare` is not used because its minimal
mode did not expose the existing OAuth login in this environment.

For a real task, follow [the project workflow](../agents/engineering-stack.md).
The selected playbook governs design, delegation, checks and delivery. Preserve
the one-question human interview format and report unavailable review roles.

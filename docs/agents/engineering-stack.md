# Pinned engineering workflow

For this project, use the exact sources selected by
`docs/toolchain/engineering-sources.lock.json`. Read the installed package's
pristine entrypoint and its references. Project instructions and the user's
current authorization govern execution.

For a fresh checkout, follow
`docs/toolchain/use-engineering-stack.md` to install, verify and inspect the stack.
If pristine sources are missing, run that setup before following a wrapper.

## Choose and follow the workflow

For planning, read Pocock's Wayfinder and the current map. Select one frontier
decision, claim it, and follow its labelled type. The map indexes decisions and
their resolution comments. Separate a bounded research answer from subsequent
implementation acceptance. Preserve the one-question interview format in
`docs/agents/grilling.md` and the existing glossary convention.

For nontrivial engineering, read the pinned PStack `poteto-mode` entrypoint and
its matching playbook. Keep the playbook steps in the task checklist. Record
skipped or unsupported steps with their reason. Read every leaf skill used by a
step. Broad authorization to use these packages does not authorize unrelated
automations, external messages, credentials, model changes or destructive work.

Use the generated `docs/toolchain/engineering-catalog.json` to resolve names.
Codex project entrypoints begin `dwb-pocock-`, `dwb-pstack-`, or `dwb-kit-`.
They point to full pristine packages rather than copying skill bodies.
Resolve relative resources beside the pristine file, not beside the wrapper.
An unqualified upstream skill name resolves within its package first.
PStack's `deslop`, `control-cli`, and `control-ui` resolve to Team Kit.
Keep Pocock and PStack versions of `tdd`, `teach`, and other overlapping names
distinct.

Claude's launcher loads native session plugins. Their namespaces are
`mattpocock-skills`, `pstack`, and `cursor-team-kit`. Read PStack's pinned Claude
adapter for tool mappings. The generated Claude tree rewrites some omitted-file
links to GitHub `main`. Resolve those to the retained canonical package at the
locked commit. Fetching `main` is not a pinned dependency resolution.

## Use supported harness tools

In Codex, read a named entrypoint with the filesystem tool. `Skill` is not an
available tool in this session. Use `collaboration.spawn_agent` for bounded
delegation when the selected workflow calls for it. Give helpers fresh context,
the original goal, source paths, write fences and completion criteria. Tell
writers they share the codebase. The parent owns verification and integration.

Native Claude plugin loading registers the packaged agent definitions. In Codex,
those agent files are reference prompts for supported generic helpers. Their
presence does not register a new native agent type. Read the relevant definition
before adapting it. Preserve role scope and do not claim unsupported agent tools.

Use only model families exposed by the active harness and authorized role
configuration. Preserve existing Claude/Codex assignments. The default PStack
Grok/Claude panel is unavailable in this Codex session. Supported inherited-model
helpers provide separate contexts, not multi-model verification. A cross-family
judge that cannot run is reported as skipped. Never substitute an unavailable
family silently or rewrite global model rules.

Use native tools in place of tool names specific to another editor. A checklist
file can replace a missing todo tool. Use supported shell/PTY tools for CLI checks
and available browser tools for Workbench web checks. A Cursor cloud VM, sticky
mode, Bugbot, `/loop`, or automation endpoint is not emulated by a local helper.
An unsupported step stays visible in the receipt. Installing the source of
`control-ui` does not grant permission to automate the ChatGPT desktop UI.
Team Kit's Cursor `.mdc` rules remain retained reference material. They do not
become native Codex or Claude rules merely because their package is loaded.

## Verify and deliver

Run the source and generated-artifact verifier before using this stack. Native
metadata inspection proves discovery separately from successful model execution,
workflow compliance and product behavior. Include the exact runtime, source lock,
artifact revision and tested outcome in each report. Report coverage gaps as gaps.

Before committing, apply Team Kit's pinned `deslop` to the actual diff. Before
review, apply PStack's `no-comments` through its supplied agent prompt with a
supported fresh helper. Apply technical-writing and unslop to prose. Keep reviews
independent of the author and report which required roles actually ran.

Use an isolated branch/worktree and reviewable commits. Open a ready pull request
through the available GitHub tool. A tool's absence can justify a disclosed
equivalent, but a source download or agent report cannot stand in for a passing
verification gate. Global user installations, hooks, automations and live work
remain outside this project setup.

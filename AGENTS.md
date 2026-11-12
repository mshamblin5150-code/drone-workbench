# Drone Workbench

Read [the planning map](https://github.com/mshamblin5150-code/drone-workbench/issues/1)
before project work; its child tickets own decisions. The owner separately
authorized [the S0 .NET foundation](https://github.com/mshamblin5150-code/drone-workbench/issues/16).
That task covers reproducible builds and synthetic runtime checks. Remaining
source-coverage decisions stay open. Implement later stages only after their
scope and authority are resolved.

During human decision interviews, follow `docs/agents/grilling.md`.

## Agent skills

### Issue tracker

Use this repository's GitHub Issues. See `docs/agents/issue-tracker.md`.

### Triage labels

Use the default Pocock labels. See `docs/agents/triage-labels.md`.

### Domain docs

Use a single root glossary and relevant decision records. See `docs/agents/domain.md`.

### Toolchain

Before source-package activation or dependency changes, read
`docs/toolchain/baseline.json`. Preserve local compatibility rules and verify the
complete pinned source and reference closure. Proposed versions are not evidence
of successful installation or activation.

For engineering tasks, use the pinned project workflow in
`docs/agents/engineering-stack.md`. It maps Pocock's decision workflow and
PStack's playbooks to the verified project entrypoints and actual harness tools.
Run the engineering-stack verifier before use. Preserve source resources and
record unsupported steps rather than substituting older global skills.

### Runtime material

Keep machine observations, credentials, raw sessions and clinical material in
local runtime storage. Public source and issues contain synthetic or deliberately
sanitized evidence.

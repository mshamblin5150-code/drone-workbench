# Issue tracker: GitHub

Repository: `mshamblin5150-code/drone-workbench` (public).

Specs, decision tickets and implementation tickets live in GitHub Issues. Use
structured connector arguments or the GitHub CLI. For multiline CLI bodies, write
UTF-8 text to a temporary file and use `--body-file`; keep temporary files outside
the published tree.

Read both an issue and its comments before working it. Refer to issues by linked
title in human-facing prose. Claim an open, unblocked ticket before working it.

## Pull requests as a triage surface

PRs as a request surface: no.

## Wayfinding operations

The map is the issue labelled `wayfinder:map`. Its native sub-issues hold tickets,
labelled `wayfinder:research`, `wayfinder:prototype`, `wayfinder:grilling`, or
`wayfinder:task`. Native issue dependencies identify blockers.

Query the map's open children, discard those with open blockers or an assignee,
and choose the next frontier ticket. An already assigned active ticket resumes
with its driving session. Claim before work.

Use the numeric database issue ID, rather than the displayed issue number, for
GitHub sub-issue and dependency API writes. Verify relationships after writing.

Resolve a ticket by posting its answer in a resolution comment, closing it, and
adding a gist and title link to the map's Decisions-so-far. The map is an index:
the detailed ruling stays in the ticket. Human decisions require the human's
answer; researchers establish facts rather than choose for them.

Publishing to the issue tracker means creating or updating a GitHub issue. A
local draft is working material until posted and read back.

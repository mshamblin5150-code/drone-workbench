# Domain documentation

Use one `GLOSSARY.md` at the repository root as the domain glossary, and
`docs/adr/` for decisions that need durable rationale. Read the glossary and
relevant ADRs before exploring the corresponding area.

Create these files when a term or qualifying decision is resolved. Missing files
do not by themselves require scaffolding. `GLOSSARY.md` contains vocabulary,
not implementation plans. ADRs record meaningful trade-offs; the Wayfinder map
and its tickets continue to own active decision work.

Use the glossary's terms consistently. Surface conflicts with applicable ADRs
before proposing a change.

The pinned Pocock 1.3.2 source uses `GLOSSARY.md`. Older installed skills may
mention `CONTEXT.md`; for this repository resolve that legacy instruction to
`GLOSSARY.md` rather than creating a parallel glossary. Keep other projects'
existing conventions unchanged.

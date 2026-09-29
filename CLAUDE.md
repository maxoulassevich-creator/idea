# CLAUDE.md

This is a **claude-novel-writer** project. The `claude-novel-writer` plugin is installed and provides the `novel-writer` CLI and `/novel` slash command.

## Project

- **Title**: Опись. Том 1: Мундус
- **Author**: Ded Jawerssa, 666SWISH
- **Genre**: историческое мистическое фэнтези, реалРПГ

## Editing mode preference

**Default: deterministic**

Many editing features come in two flavours. When the user asks for editing help,
prefer the **deterministic** tools below unless they ask otherwise — the other
mode stays available.

- **deterministic** — fast, local, no API key. Prose/structure analysis and
  checks: `novel-writer analyze prose|pacing|style|sentences`, `novel-writer check`,
  `novel-writer analyze copy`.
- **ai** — Claude-generated drafting and suggestions: `novel-writer generate ...`,
  `novel-writer craft ...` (requires `ANTHROPIC_API_KEY`).

## Project structure

```
.novel/data.db                 ← SQLite database (do not edit directly)
style-targets.yml              ← numeric prose targets (drives `analyze style`)
STRUCTURAL_STYLE_GUIDE.md      ← technical style rules (Copy Editor agent reads this)
COMPOSITIONAL_STYLE_GUIDE.md   ← voice/imagery/theme (Developmental/Line Editor agents)
templates/                     ← starter templates to copy into the content dirs
characters/                    ← YAML character profiles
locations/                     ← YAML location files
plots/                         ← YAML plot threads
world-rules/                   ← YAML world rules
timeline/                      ← YAML story events
chapters/                      ← Markdown chapter files (YAML frontmatter)
research/                      ← Research notes
revisions/                     ← Snapshot archives
export/                        ← Exported manuscripts
```

## Style files & templates

- **`style-targets.yml`** holds this project's numeric prose targets. Tune it,
  then run `novel-writer analyze style --chapter N` (or `--all`) to grade prose
  against it. Edit this file to change what the deterministic analyzers flag.
- **`STRUCTURAL_STYLE_GUIDE.md`** and **`COMPOSITIONAL_STYLE_GUIDE.md`** hold the
  qualitative voice/punctuation/theme rules. The Copy / Line / Developmental
  Editor agents read them by name from the project root — keep them here. Fill in
  the `<placeholders>` to make the agents enforce your conventions.
- **`templates/`** is a reference library, not synced. To add an entity: copy the
  matching template into its content dir, rename it, edit it, then sync. e.g.
  `cp templates/character.yml characters/ada-vex.yml` → edit → `novel-writer sync all`.
  (Files left in `templates/` are never imported; only files inside the content
  dirs are synced.)

## When to use novel commands (proactive guidance)

Use the `novel-writer` CLI (via Bash) or the skills loaded by the plugin. Invoke proactively without waiting for the user to ask:

| Situation | Command |
|---|---|
| User asks about characters, plot, or story | `novel-writer list characters` / `novel-writer list chapters` |
| User asks to check for problems or contradictions | `novel-writer check` |
| User asks for prose feedback or style analysis | `novel-writer analyze prose` |
| User asks about pacing or tension | `novel-writer analyze pacing` |
| User is about to do major revisions | Suggest `novel-writer revision snapshot --label "before-x"` first |
| User asks for what still needs work | `novel-writer draft scan` (finds [TK] markers) |
| User asks to write or continue a scene | `novel-writer generate scene --scene-id N` |
| User asks for a synopsis or pitch | `novel-writer generate synopsis` / `generate pitch` |
| User asks about unverified facts in the manuscript | `novel-writer research verify` |
| User wants to export the manuscript | `novel-writer export markdown` |

## Key conventions

- Chapter files: `chapters/chapter-NN-title.md` with YAML frontmatter
- Character files: `characters/name.yml`
- After editing YAML files by hand, run `novel-writer sync` to push to the database
- All AI generation requires `ANTHROPIC_API_KEY` to be set
- Use `novel-writer help` for the full command reference

---
name: novel-generate
description: >
  Use this skill when the user wants AI help generating content for their novel:
  continuing a scene, writing dialogue, describing a location, developing a
  character, writing a synopsis, query letter, pitch, or finding comparable
  titles. Trigger phrases include: "continue this scene", "what happens next",
  "write a synopsis", "help me with my query letter", "generate a pitch",
  "what books are similar to mine", "describe this location", "develop this
  character", "next sentence", "I'm stuck", "summarize the book I'm planning",
  "what's this book about", "overview of the intended story".
version: 0.1.0
---

# Novel AI Generation

Most generation uses `ANTHROPIC_API_KEY` when it is set. Two commands also work
**without** a key — `generate summary` and `generate overview` fall back to
"passthrough" mode, printing an assembled prompt that the current Claude Code
session fulfils directly.

## Continue a scene (3 alternatives)
```bash
novel-writer generate scene --scene-id <N>
novel-writer generate scene --scene-id <N> --count 5   # more alternatives
```
Loads full scene context (characters, location, world rules, recent chapters) before generating.

## One-true-sentence mode (Hemingway)
```bash
novel-writer generate next-sentence --scene-id <N>
```
Generates the single most true next sentence. Good for writer's block.

## Intended-book overview (planning summary, pre-draft)
```bash
novel-writer generate overview                      # ~350-word standard overview
novel-writer generate overview --length brief       # ~150 words
novel-writer generate overview --length full --save # ~700-word treatment → export/overview.md
```
Summarizes the book the author **means to write**, assembled from the planned
outline (plot threads + their beats) and the character roster — unlike
`synopsis`, which summarizes *drafted* chapters. Works before any chapter is
written; needs at least one character or plot thread. If `characters/` and
`plots/` are empty, bootstrap them first with `novel-writer extract --file outline.md`.

## Submission materials
```bash
novel-writer generate synopsis          # full synopsis (from drafted chapters)
novel-writer generate pitch             # one-paragraph pitch
novel-writer generate query             # query letter draft
novel-writer generate comps             # comparable titles
```

## Character & world building
```bash
novel-writer generate character --id <N>          # full character profile
novel-writer generate name --role villain         # name suggestions
novel-writer generate name --place city           # place name suggestions
```

## Development tools
```bash
novel-writer generate premise           # workshop your premise
novel-writer generate sketch --name "Ada"   # quick character sketch
```

## Opening lines workshop
```bash
novel-writer generate opening-lines     # 5 alternative opening lines for the manuscript
```

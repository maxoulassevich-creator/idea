# Third-party skills for fiction writing

Both sources are MIT-licensed. Files were copied unmodified except where noted.

## ThomasHoussin/Claude-Book — MIT, © 2025 Thomas HOUSSIN
Source: https://github.com/ThomasHoussin/Claude-Book (commit 3fdebbb)

- `.claude/skills/{book-analyzer,bible-merger,story-ideator,perplexity-improver}/`
- `.claude/agents/{chapter-planner,chapter-writer,style-linter,character-reviewer,continuity-reviewer,state-updater}.md`
- `scripts/detection/` (used by perplexity-improver), `scripts/style/` (style checker)
- `bible/*.example`, `bible/{characters,universe}/_template.md` — empty templates only.
  The upstream sample content (Club des Cinq bible, chapters, state) was NOT copied.
- The upstream orchestrator `CLAUDE.md` was NOT copied (it hardcodes French output and its own project layout).

Note: perplexity-improver needs `uv` and an NVIDIA GPU (~16 GB VRAM) to run its local model.

## mrskwiw/claude-novel-writer — MIT, © 2026 claude-novel-writer contributors
Source: https://github.com/mrskwiw/claude-novel-writer (commit 8307d47), package `claude-novel-writer` on npm

- `.claude/skills/novel-{setup,context,generate,analyze,check}/SKILL.md` — upstream ships these as flat
  `skills/*.md` files; here each is moved into its own folder as `SKILL.md` so Claude Code discovers it.
- `.claude/agents/{character-developer,consistency-checker,copy-editor,developmental-editor,line-editor,novel-writing-assistant,plot-analyzer}.md` and `agents/README.md`
- `.claude/commands/novel.md` — the `/novel` slash command

These skills only wrap the `novel-writer` CLI. Install it once per machine/session:

    npm install -g claude-novel-writer     # Node >= 18

Then start a book in an empty folder with `/novel init` (or just say "I want to write a novel").

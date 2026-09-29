# Content Templates

Reference starter files for a `claude-novel-writer` project. Each template documents
the exact YAML keys (and, for chapters, the Markdown frontmatter and scene markers)
that the sync layer parses. Copy one into the matching content directory, edit it,
then sync.

## Templates and where they belong

| Template | Copy into | Schema |
|---|---|---|
| `character.yml`  | `characters/`  | `CharacterYAML` |
| `location.yml`   | `locations/`   | `LocationYAML` |
| `plot.yml`       | `plots/`       | `PlotYAML` |
| `world-rule.yml` | `world-rules/` | `WorldRuleYAML` |
| `timeline.yml`   | `timeline/`    | `TimelineYAML` |
| `chapter.md`     | `chapters/`    | Markdown + YAML frontmatter |

## Workflow

1. Copy a template into the matching content directory.
2. Rename it to something descriptive (e.g. `characters/ada-vex.yml`,
   `chapters/01-the-salons-shadow.md`).
3. Edit it — keep the keys spelled exactly as the template shows; the sync layer
   matches on them.
4. Load it into the database:

   ```bash
   novel-writer sync all
   ```

## Notes

- **Files left here in `templates/` are NOT synced.** Only files inside the content
  directories (`characters/`, `locations/`, `plots/`, `world-rules/`, `timeline/`,
  `chapters/`) are read by `novel-writer sync`. This directory is reference only.
- Required keys are marked `REQUIRED` in each template; everything else is optional.
- Chapter files must keep their `<!-- ... -->` scene markers — tools such as the
  draft scanner and scene-context assembler read them. The `chapter.md` template is
  pre-filled so it passes the draft-scanner readiness check (purpose, conflict/tension,
  and POV are all present).
- After editing files by hand, always run `novel-writer sync` to push changes to the
  database before running analysis commands.

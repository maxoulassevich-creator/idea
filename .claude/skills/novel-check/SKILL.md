---
name: novel-check
description: >
  Use this skill when the user wants to check their manuscript for problems,
  inconsistencies, continuity errors, plot holes, timeline issues, unresolved
  plot threads, world rule violations, or character contradictions. Trigger
  phrases include: "check my novel", "are there any inconsistencies", "does
  anything contradict", "what's wrong with my story", "check continuity",
  "any timeline problems", "unresolved threads", "plot holes".
version: 0.1.0
---

# Novel Consistency Check

Run the full consistency check suite on the current novel project.

```bash
novel-writer check
```

This runs four checks in parallel:
1. **Character attributes** — same attribute with conflicting values across chapters (e.g., eye color changes)
2. **Timeline** — events with timestamps that violate causal order
3. **World rules** — hard world rules (e.g., "no electricity") violated in chapter text
4. **Plot threads** — high-priority threads still open past their expected payoff chapter

For targeted checks:
```bash
novel-writer check characters    # character contradictions only
novel-writer check timeline      # timeline violations only
novel-writer check world-rules   # world rule violations only
novel-writer check plot-threads  # unresolved plot threads only
```

To list previously found issues:
```bash
novel-writer check list
```

To resolve an issue (after fixing it in your manuscript):
```bash
novel-writer check resolve --id <issue-id> --notes "Fixed in chapter 12"
```

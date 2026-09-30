---
name: novel-analyze
description: >
  Use this skill when the user wants feedback on their writing quality, prose
  style, pacing, show-vs-tell ratio, POV consistency, sentence variety, sensory
  balance, dialogue attribution, or wants to hear their work read aloud. Trigger
  phrases include: "analyze my writing", "is my pacing good", "too much telling",
  "check my prose", "how's my style", "POV slips", "tense consistency", "read
  aloud prep", "tension arc", "chapter lengths".
version: 0.1.0
---

# Novel Analysis Tools

## Prose analysis (sentence-level quality)
```bash
novel-writer analyze prose
novel-writer analyze prose --chapter 3       # single chapter
```
Reports: economy score, show/tell ratio, sensory balance (visual/auditory/tactile/taste/smell), character voice distinctiveness, dialogue breakdown by character.

## Pacing analysis (story-level structure)
```bash
novel-writer analyze pacing
```
Reports: tension arc across chapters (ASCII chart), POV balance per character, chapter length distribution, pacing anomalies (flat tension, sudden spikes).

## Copy editing (mechanical errors)
```bash
novel-writer analyze copy
novel-writer analyze copy --chapter 3
```
Checks: POV slips (head-hopping within a scene), tense shifts (> 20% inconsistency), name variants (possible misspellings of character names).

## Developmental editing (structure)
```bash
novel-writer analyze developmental
```
Audits: scene purpose (does each scene advance plot or character?), subplot balance (neglected or overdone threads), plot holes (promised setups without payoff).

## Style targets (quantitative grading)
```bash
novel-writer analyze style --chapter 3
novel-writer analyze style --all
```
Measures ~12 prose metrics (sentence length, adjective/adverb density, passive %, show/tell, sensory coverage, em-dash/semicolon cadence, fragments) against the project's `style-targets.yml` and grades each ✓ / ⚠ low / ⚠ high. Falls back to general-fiction defaults when no file is present.

## Opening-line hook strength
```bash
novel-writer analyze hook --chapter 1
```
Deterministic 0–100 score for a chapter's opening across six signals (poses a
question, introduces a character, place/atmosphere, in-medias-res, tension,
avoids throat-clearing) with advisory suggestions.

## Advisory vs strict grading
`analyze prose|sentences|dialogue` grade flags as info/suggestion/warning relative
to `style-targets.yml` (Le Guin: "suggest, don't dictate"). Add `--strict` for hard
flagging, or an `allow:` list in `style-targets.yml` to silence specific words.

## Character voice (manuscript-wide)
```bash
novel-writer analyze voice
novel-writer analyze voice --character "Ada"
```
Flags character voices that sound too alike (may be indistinguishable to a reader) and voices that drift across chapters.

## Read-aloud preparation (text)
```bash
novel-writer draft readaloud --chapter 3
```
Strips markdown, flags rhythm anomalies, detects accidental rhymes, highlights long sentences that may trip readers. Produces text, not audio.

## Read aloud (actual audio)
```bash
novel-writer readaloud --chapter 3
novel-writer readaloud --chapter 3 --out chapter3.wav
```
Speaks the chapter through the OS text-to-speech engine (Windows SAPI, macOS `say`, Linux espeak/spd-say). `--out` writes an audio file instead of playing.

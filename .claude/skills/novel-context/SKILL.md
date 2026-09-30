---
name: novel-context
description: >
  Use this skill when working in a directory that contains a .novel/ folder or
  data.db file, or when the user is asking about their novel, story, manuscript,
  characters, chapters, plot threads, world building, or any creative fiction
  writing task. Also use when the user says things like "check my manuscript",
  "what's wrong with my story", "I'm writing a book", or mentions novel-specific
  concepts like pacing, POV, consistency, or scene context. Provides an overview
  of what tools are available and how to use them.
version: 0.1.0
---

# Novel Writer — Available Tools

You are working with the **claude-novel-writer** plugin. The following tools are available via the `novel-writer` CLI (invoke via Bash) or as the `/novel` slash command.

## Detect the novel project

First check if a novel project exists in the current directory:

```bash
ls .novel/data.db 2>/dev/null && echo "NOVEL PROJECT FOUND" || echo "NOT A NOVEL PROJECT"
```

If found, the user is working on a novel. Proactively suggest relevant commands.

## Common user intents → commands

| When the user says... | Run |
|---|---|
| "check for inconsistencies / plot holes / contradictions" | `novel-writer check` |
| "what chapters do I have / how far am I" | `novel-writer list chapters` |
| "who are my characters" | `novel-writer list characters` |
| "analyze my prose / writing style" | `novel-writer analyze prose` |
| "check my pacing / tension arc" | `novel-writer analyze pacing` |
| "look for things I still need to verify or research" | `novel-writer research verify` |
| "scan for TK / TODO markers in my manuscript" | `novel-writer draft scan` |
| "export / compile my manuscript" | `novel-writer export markdown` |
| "save a draft snapshot before I revise" | `novel-writer revision snapshot --label "before-revisions"` |
| "help me start a new chapter" | `novel-writer create chapter --number N --title "..."` |
| "add a character" | `novel-writer create character --name "..." --role protagonist` |
| "what plot threads are unresolved" | `novel-writer list plot` |
| "check my timeline" | `novel-writer check timeline` |
| "generate the next scene / continue writing" | `novel-writer generate scene --scene-id N` |
| "write a synopsis / pitch / query letter" | `novel-writer generate synopsis` / `pitch` / `query` |

## Proactive behaviors

- If the user pastes a chapter or scene and asks for feedback → run `novel-writer analyze prose` and `novel-writer analyze pacing`
- If the user mentions a character inconsistency → run `novel-writer check characters`  
- If the user is about to do major revisions → suggest `novel-writer revision snapshot` first
- If the user asks "what should I work on next" → run `novel-writer draft scan` to surface [TK] markers and `novel-writer check` to surface issues

## Getting help

```bash
novel-writer help
novel-writer help <command>
```

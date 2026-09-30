---
name: novel-setup
description: >
  Use this skill to initialize a new novel project in the current directory.
  Trigger when the user says things like: "I want to write a novel", "set this
  up as a novel project", "I'm starting a new book", "help me start writing",
  "initialize this for my novel", "I want to use this folder for my story",
  "let's set up a writing project", "new novel", "start a novel", or when the
  user is in an empty or near-empty directory and expresses intent to write
  fiction. Also triggers if the user asks what novel-writer commands are
  available but no .novel/ directory exists yet.
version: 0.1.0
---

# Set Up a New Novel Project

You are initializing a new novel project in the current directory.

## Step 1 — Check current state

```bash
ls .novel/data.db 2>/dev/null && echo "ALREADY_INITIALIZED" || echo "BLANK"
```

If `ALREADY_INITIALIZED`: tell the user the project already exists and offer to run `novel-writer help` instead. Stop here.

If `BLANK`: continue to Step 2.

## Step 2 — Collect project details

Ask the user for the following (you can ask all at once):

1. **Title** — what is the working title of the novel?
2. **Author name** — what name should appear as the author?
3. **Genre** — e.g. literary fiction, fantasy, thriller, romance, sci-fi, horror, mystery (optional)
4. **Target word count** — e.g. 80,000 for a standard novel, 50,000 for YA, 100,000+ for epic fantasy (optional, defaults to 80,000)

If the user has already mentioned any of these in the conversation, use those values without re-asking.

## Step 3 — Initialize

Run (add `--json` so you get a machine-readable result you can parse):

```bash
novel-writer init --json --title "<title>" --author "<author>" --genre "<genre>" --words <target-word-count>
```

Omit `--genre` if the user didn't provide one. Omit `--words` if using the default.

`init` is safe to run non-interactively — it never prompts when stdin is not a
terminal (as in the Bash tool). If you omit `--title`/`--author`, it will
auto-derive them (title from the directory name, author from `git config
user.name`) and report what it chose in the `derived` field rather than
hanging. Still, prefer passing real values from Step 2 when you have them.

The JSON result looks like:

```json
{"status":"ok","projectId":1,"path":".novel/data.db",
 "metadata":{"title":"...","author":"...","targetWordCount":80000,"currentPhase":"ideation"},
 "created":{"directories":["characters","locations","..."],"claudeMd":true},
 "nextSteps":["..."]}
```

If `status` is `exists`, the project was already initialized — tell the user and
offer `novel-writer help` (or re-run with `--force` to start over).

## Step 4 — Orient the user

After successful init, tell the user:

- A `CLAUDE.md` has been written to this directory — Claude will use it to understand the project on future sessions
- **First steps**: create a character (`novel-writer create character`), then a chapter (`novel-writer create chapter --number 1 --title "..."`)
- They can type `/novel help` at any time for the full command reference
- You (Claude) will proactively suggest novel commands as they work — they don't need to remember command names

## Step 5 — Offer to continue

Ask: "Would you like to create your first character now, or start with an outline?"

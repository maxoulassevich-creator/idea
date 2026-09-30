# Claude Novel Writer - Subagents

This directory contains specialized subagents for the Claude Novel Writer project. These agents are designed to assist with specific aspects of novel writing while respecting craft principles and author voice.

## Available Subagents

### Creative Writing Agents

#### 1. Novel Writing Assistant
**File**: `novel-writing-assistant.md`
**Purpose**: General creative fiction writing assistance
**Use for**:
- Generating character profiles and locations
- Scene continuation suggestions
- Dialogue enhancement
- Description expansion
- Plot brainstorming
- Writing craft guidance

**Example**:
```
@novel-writing-assistant Can you suggest 3 ways to continue this scene?
[paste scene text]
```

---

#### 2. Character Developer
**File**: `character-developer.md`
**Purpose**: Deep character creation and consistency
**Use for**:
- Creating complex character profiles
- Checking character consistency across chapters
- Developing character arcs
- Ensuring distinct character voices
- Identifying character contradictions

**Example**:
```
@character-developer Create a character profile for:
"A brilliant but isolated astrophysicist who discovers an alien signal"
```

---

### Analysis & Review Agents

#### 3. Consistency Checker
**File**: `consistency-checker.md`
**Purpose**: Detecting contradictions and continuity errors
**Use for**:
- Finding character attribute conflicts (eye color, age, etc.)
- Identifying timeline inconsistencies
- Catching world rule violations
- Detecting plot thread issues
- Distinguishing errors from intentional complexity

**Example**:
```
@consistency-checker Review chapters 1-10 for character consistency,
especially Sarah Chen's physical description and personality traits.
```

---

#### 4. Plot Analyzer
**File**: `plot-analyzer.md`
**Purpose**: Story structure and pacing analysis
**Use for**:
- Analyzing story structure (three-act, hero's journey, etc.)
- Identifying pacing issues
- Tracking plot threads
- Checking stakes escalation
- Suggesting plot improvements

**Example**:
```
@plot-analyzer Analyze the pacing and structure of Act 2 (chapters 8-18).
```

---

### Editing Agents

**Important**: All editing agents consult your project style guides:
- `COMPOSITIONAL_STYLE_GUIDE.md` - Voice, POV, themes, dialogue style
- `STRUCTURAL_STYLE_GUIDE.md` - Technical patterns, punctuation, formatting

#### 5. Developmental Editor
**File**: `developmental-editor.md`
**Purpose**: Big-picture structural editing
**Use for**:
- Story structure analysis
- Character arc evaluation
- Pacing assessment
- Theme integration review
- POV and narrative voice consistency

**Consults**: Both style guides for author's vision and narrative commitments

**Example**:
```
@developmental-editor Analyze chapters 1-10 for:
- Story structure alignment with three-act format
- Character arc progress
- Pacing appropriateness for Act I
```

---

#### 6. Line Editor
**File**: `line-editor.md`
**Purpose**: Sentence-level prose refinement
**Use for**:
- Improving sentence clarity and rhythm
- Strengthening word choice
- Reducing wordiness
- Enhancing dialogue
- Fixing show-vs-tell balance

**Consults**: Both style guides for voice, modifier limits, filter words, etc.

**Example**:
```
@line-editor Polish this paragraph while maintaining my voice:
[paste paragraph]
```

---

#### 7. Copy Editor
**File**: `copy-editor.md`
**Purpose**: Technical correctness and style consistency
**Use for**:
- Grammar and spelling errors
- Punctuation consistency
- Style guide compliance
- Typo detection
- Consistency checks (names, formatting, numbers)

**Consults**: Both style guides for punctuation style, formatting rules, technical preferences

**Example**:
```
@copy-editor Check chapter 5 for:
- Style guide compliance
- Grammar errors
- Consistency issues
```

## How to Use Subagents

### In Claude Code

**Option 1: @ Mention**
```
@character-developer [your request]
```

**Option 2: /agent command**
```
/agent character-developer
```

Then ask your question in the conversation.

### Best Practices

1. **Be Specific**: Give the agent context
   ```
   Good: @consistency-checker Check Sarah Chen's eye color across all chapters
   Bad: @consistency-checker check my character
   ```

2. **Provide Context**: Share relevant files
   ```
   @character-developer Review characters/sarah-chen.yml and suggest
   improvements to her character arc based on chapters/01-*.md
   ```

3. **Combine Agents**: Use different agents for different tasks
   ```
   # First, develop character
   @character-developer Create profile for villain

   # Then, check consistency
   @consistency-checker Verify villain appears consistently in chapters 5,10,15
   ```

4. **Edit AI Output**: Always review and edit suggestions to match your voice

## When to Use Which Agent

**Starting a new novel?**
→ `@novel-writing-assistant` for general guidance
→ `@character-developer` for creating your cast

**Mid-draft?**
→ `@novel-writing-assistant` when stuck on a scene
→ `@plot-analyzer` to check story structure
→ `@consistency-checker` for quick consistency checks

**Revising?**
→ `@consistency-checker` for full manuscript review
→ `@character-developer` for character arc review
→ `@plot-analyzer` for pacing and structure analysis

**Need creative ideas?**
→ `@novel-writing-assistant` for brainstorming
→ `@plot-analyzer` for plot development suggestions

## Agent Philosophy

All agents follow these principles:

✅ **Suggest, don't dictate** - Provide options, not mandates
✅ **Respect author voice** - Match existing style and tone
✅ **Support discovery writing** - Embrace "pantsing" and exploration
✅ **Encourage completion** - Progress over perfection
✅ **Based on master novelist wisdom** - Grounded in craft principles

## Customizing Agents

These agents are project-level (in `.claude/agents/`), so they're specific to this novel writing project.

**To modify an agent:**
1. Edit the `.md` file
2. Change the system prompt (below the YAML frontmatter)
3. Save - changes apply immediately

**To create a new agent:**
1. Create `agent-name.md` in this directory
2. Add YAML frontmatter with `name`, `description`, `tools`, `model`
3. Write the system prompt

See [Claude Code Agent Documentation](https://docs.claude.com/en/docs/claude-code/sub-agents) for details.

## Tools Available to Agents

Each agent has access to specific tools defined in their YAML frontmatter:

- **Read** - Read files from the project
- **Write** - Create new files
- **Edit** - Modify existing files
- **Grep** - Search file contents
- **Glob** - Find files by pattern

The tools are restricted to what each agent needs for its specialized purpose.

## Example Workflows

### Character Development Workflow

```bash
# 1. Create character with AI assistance
@character-developer Create a character: "mysterious government agent"

# 2. Review and edit the generated YAML
# Edit characters/agent-name.yml

# 3. Sync to database
/novel sync characters

# 4. Use character in chapters
# Write your scenes...

# 5. Check consistency after writing several chapters
@consistency-checker Check Agent Smith's consistency in chapters 5-12
```

### Writing Session Workflow

```bash
# 1. Start session
/novel session start

# 2. Get continuation suggestions
@novel-writing-assistant Suggest 3 ways to continue chapter 8, scene 2
[Current text: Sarah stared at the signal pattern...]

# 3. Pick a direction and write
# [Write in your editor]

# 4. Check pacing
@plot-analyzer Review the pacing of chapter 8

# 5. End session
/novel session end --words 734
```

### Revision Workflow

```bash
# 1. Character consistency check
@character-developer Review all characters for consistency

# 2. Plot structure analysis
@plot-analyzer Analyze overall story structure (chapters 1-24)

# 3. Detailed consistency check
@consistency-checker Full manuscript consistency check

# 4. Address issues
# [Fix problems identified]

# 5. Final verification
@consistency-checker Re-check chapters I just revised
```

## Tips

- **Don't use agents for every sentence** - Write first, use agents when stuck
- **Edit everything** - AI suggestions are starting points, not final copy
- **Combine with CLI tools** - Use `/novel` commands alongside agents
- **Trust your instincts** - If an agent suggestion feels wrong, ignore it
- **Experiment** - Try different agents for the same problem

## Support

For issues or questions about agents:
- See main project documentation in `../README.md`
- Read [USER_GUIDE.md](../../USER_GUIDE.md) for comprehensive guidance
- Check [NOVEL_CRAFT_PRINCIPLES.md](../../NOVEL_CRAFT_PRINCIPLES.md) for philosophy

---

**Remember**: Agents are tools to support your writing, not replace it. The story belongs to you. 📖✍️

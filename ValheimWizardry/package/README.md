# Wizardry — Harry Potter magic for Valheim

Become a wizard in Valheim. You build the Codex, craft a wand and get sorted into a house. You learn spells from the
books, the films and the wider lore, and you take an exam at the Codex for every boss you defeat. A guided **wizard's
path** shows you the next step all the way to the Elder Wand.

*Русская инструкция лежит в архиве: `УСТАНОВКА.md`.*

## How it plays

1. **Build the Codex.** Hammer → Crafting → Codex of Eternal Shadows.
2. **Craft a wand** at the Codex and equip it.
3. **Press K.** The Sorting Hat picks your house, which gives your first spell and a lasting bonus.
4. **Cast.** LMB casts the chosen spell, MMB the free Basic Cast, RMB raises a Protego shield.
5. **Grow.** Every Wizardry level, path step and exam gives a **magic point ✦**. Spend points on new spells at the Codex
   and on stars of the talent constellation.
6. **Pass exams.** Show a boss trophy to the Codex to move up a year. Each year opens new spells, talents and wands.

The tracker under the minimap always shows the current step, and Hugin the raven explains the important ones.

## Features

* **The grimoire (K).** An open leather book with three bookmarks.
  * **Path:** the current step, the years and their exams, books to read.
  * **Spells:** learn spells, put them in the bar and choose rank upgrades.
  * **Talents:** six branches of eight stars, from small bonuses to keystones like *Obscurus* or *Healer's Oath*. Resets
    are free at a Codex.
* **330+ castable spells** across 13 schools. Each spell has its own colour, sign and effect: bolts, beams, cones, novas,
  shields, wards, domes, walls, summons (the Patronus included), polymorph, Apparition, flight, weather, revealing charms,
  rituals and grand spells.
* **Ranks I–X from use.** At ranks III, VI and IX a spell asks you to choose one of two upgrades, or a variant such as
  *Flipendo Tria*.
* **Nine wands and ten cores.** Woods run from beech to the Elder Wand; cores include a phoenix-like feather, a dragon
  heartstring, a Morgen heart and more.
* **Six Codex extensions:** a wand stand, a cauldron, a crystal ball, the Pensieve, an astrolabe and a Time-Turner. They
  raise the Codex's level like workbench extensions do.
* **Potions, the Deathly Hallows, Chocolate Frog cards and boss memories.**
* **Effects.** Spells use the game's own magic effects (Dvergr staffs, lightning, frost, summons), plus rune circles on
  the ground, light trails and sounds.
* **Multiplayer.** Statuses run on the creature's owner and effects are drawn by every client. Balance and world rules
  come from the server.

## Controls

| Key | Action |
|-----|--------|
| LMB / MMB / RMB (wand in hand) | chosen spell / Basic Cast / Protego |
| Left Alt + 1…8, Alt + wheel | choose a spell slot |
| Z | grand spell (when the Focus bar is full) |
| K | the grimoire (also from the Codex's crafting window) |

Every key can be changed in `BepInEx/config/com.oulassevich.wizardry.cfg`.

## Compatibility

* **Epic Loot.** Checked against the patches of Epic Loot 0.15.4:
  * no shared keys (Epic Loot's ability keys are G/H/J);
  * Wizardry's Luck bonus never copies Epic Loot's magic items;
  * chest loot from both mods adds up.
* Every player and the server need the mod (Jötunn checks the version).
* Coming from 1.x: your learned spells, ranks, house and years stay, and the points of the old tree come back.

## Requirements

* BepInExPack_Valheim
* Jötunn

## Settings

Balance and world rules are admin-only and synced from the server. Client settings:

* the tracker and Hugin's tips;
* the game's effects on spells;
* particle density, camera shake, blood;
* language (`auto` / `ru` / `en`).

---

*A fan-made mod. Not affiliated with or endorsed by Warner Bros., Wizarding World or Iron Gate. Harry Potter names
belong to their owners.*

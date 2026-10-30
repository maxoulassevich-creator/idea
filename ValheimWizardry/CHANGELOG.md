# Changelog

## 2.0.0 — 2026-10-31

A rework for clarity and looks. 1.x saves carry over: learned spells, ranks, the house and the years stay, and the points of
the old tree come back.

### Progression, simplified

* **The wizard's path.** 40 guided steps, from building the Codex to the Elder Wand. Each step says what to do and how,
  shows its progress, and gives a magic point. The current step sits on screen under the minimap.
* **Hugin explains.** The raven explains the important steps once; this can be turned off.
* **One currency.** Magic points ✦ come from Wizardry skill levels, path steps, exams, cards and textbooks. They pay for
  spells and talents.
* **Learning spells.** Spells are learned at a Codex, one at a time, for points (✦1–3 by year). Each exam opens the next
  year's spells.
* **Exams.** A boss trophy shown to the Codex passes the year. The ladder of years is in the grimoire.
* **The talent constellation.** It replaces the 457-node tree. There are 6 branches (Duelling, Protection, Elements,
  Healing, Dark Arts, Charms), each of 8 stars ending in a keystone. A free reset at a Codex.
* **Rank upgrades.** At ranks III, VI and IX a spell asks you to choose one of two upgrades, right in its card.
* **Removed.** Sockets, masteries, callings, metamagic slots, the bestiary and the ledger: they were too much.

### Looks

* **The grimoire** is an open leather book with three bookmarks: Path, Spells, Talents. It has Sorting Hat cards with house
  crests, tooltips and parchment pages, and Russian text in a bookish font with Cyrillic.
* **Spell icons** combine the school's colour with the archetype's sign. The spell bar uses bronze slots, cooldown shading
  and a Focus bar.
* **57 item icons** rendered in Blender: wands in their woods, cores, textbooks with numerals, potions, the Hallows, memories.
* **New Codex extensions:** a wand stand, a cauldron, a crystal ball, the Pensieve, an astrolabe and a Time-Turner, with
  baked textures.
* **The Codex model is fixed.** The 1.0 material inherited a metallic map with no texture, so the book turned into a white
  chrome blob.

### Spell effects

* **The game's own magic effects** play under ours: Dvergr fire, frost and support, lightning, the staff shield, summons,
  Odin's smoke and the Forsaken power. Turn them off with `UseGameEffects`.
* **Rune circles** glow on the ground for areas, summons, healing, rituals and grand spells.
* **Trails** of light follow bolts.
* **Particle materials** fall back to the game's own particle shaders.

### Compatibility

* **Epic Loot.** The grand spell key moved from G (Epic Loot's ability key, and the game's radial menu) to **Z**. The
  Luck bonus copies only plain materials and trophies, before Epic Loot adds its magic items.
* **Alt + 1…8** no longer also uses the item in that hotbar slot.
* **The Codex hover** shows "[K] Grimoire", and K works from the Codex crafting window too.

## 1.0.1 — 2026-10-30

### Fixed

* `FieldAccessException: Field 'Character:m_nview' is inaccessible` logged every frame.
  * Valheim 1.0.21 runs on Unity 6, whose Mono enforces member visibility at run time. The mod was compiled against
    publicized references, so it touched private and protected game members directly.
  * All of them (about 40 members in 170 places) now go through public game methods, Harmony `___field` parameters or
    reflection (`Access.cs`).
* One failing subsystem (Lumos, weather, flight, zones…) no longer stops the others.
* A repeated error is logged once with its stack trace and then only counted.

### Added

* `tools/check_access.sh` compiles the mod against the original (non-publicized) Valheim assemblies, so a direct use
  of a non-public game member fails the check.

## 1.0.0 — 2026-10-30

The first release, for Valheim 1.0.21 (BepInExPack_Valheim 5.4.2360, Jötunn 2.30.2).

### Stations and items

* **The Codex** with six extensions.
* Nine wand woods and ten wand cores.
* Books for years 1–7, the Weasley kit, the Book of Rituals, the Grimoire of Grand Spells and the Practice book.
* The Obliviate scroll and the Pensieve cup.
* Chocolate Frog cards and memories (with a memory dropped by each of the eight bosses).
* The Deathly Hallows.
* Six potions.

### Progression

* The Wizardry skill.
* Years from boss trophies.
* The Tree of Charms (457 nodes) with four houses, masteries and sockets.
* Eight callings.
* Spell ranks, patterns at ranks 3/6/9 and metamagic.
* The bestiary and Darkness.

### Spells

* 341 castable spells driven by `tools/spell_fx.txt`.
* Zones, domes, walls, bridges, summons, transfiguration, Apparition, flight, weather and revealing charms.
* Rituals and grand spells.

### Interface

* The spell bar with cooldowns, the cast bar, and Focus and Darkness.
* The grimoire window (K).

### Multiplayer

* Statuses run on the creature's owner, effects are broadcast, and anchors are network objects.
* Balance and world rules sync from the server.

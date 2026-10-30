# Changelog

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

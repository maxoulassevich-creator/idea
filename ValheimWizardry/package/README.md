# Wizardry — Harry Potter magic for Valheim 1.0.21

Wands, a Codex table, and a Path of Exile–style **Tree of Charms**. The mod has 382 spells from the books, the films,
the games, the HP wiki and fan fiction, and 341 of them can be cast in Valheim.

*Русская инструкция по установке лежит в архиве: `УСТАНОВКА.md`.*

> Version 1.0.0 is compiled against the Valheim 1.0.21 reference assemblies but has **not been played yet**.
> Back up your world and character first, and please send `BepInEx/LogOutput.log` if something breaks.

## Features

* **The Codex**: a crafting station (hammer → Crafting) with six extensions. It makes wands, cores, books, scrolls
  and potions. Boss trophies are shown to it to advance your year, and rituals are cast beside it.
* **Wands**: nine woods (beech to the Elder Wand) with tiers, power, eitr and school affinity, plus ten cores
  (raven feather, Fenris hair, dragon heartstring…).
  * LMB casts the chosen spell.
  * MMB casts the Basic Cast.
  * RMB is Protego, a wand block that costs eitr instead of stamina.
* **Growth**:
  * The Wizardry skill gives a tree point per level. Houses, boss trophies and Chocolate Frog cards give more.
  * Years 1–9 come from boss trophies, which you keep.
  * The Tree of Charms has 457 nodes: small nodes, notables, keystones, masteries and Pensieve sockets for memories.
  * Four houses. Eight callings from year 5.
* **Spells**:
  * Ranks 1–10 from use.
  * At ranks 3, 6 and 9 you choose a pattern: an archetype option or the spell's own variant (Flipendo → Tria → Maxima).
  * Metamagic slots open at ranks 2, 5 and 8: Nonverbal, Maxima, Duo, Cascading, Homing, Totalum, Delayed…
* **Kinds of magic**:
  * bolts, rays, channels, cones, novas and blasts;
  * statuses: stun, root, slow, fear, charm, polymorph into a hen, a goblet or a matchbox;
  * zones, domes, walls and bridges;
  * summons: Patronus, flocks of birds, golems, Inferi;
  * Apparition, flight and the Time-Turner;
  * weather, revealing charms, the Marauder's-map charm;
  * Portkeys between Codexes, the Vanishing Cabinet and extended chests;
  * grand spells for full Focus: Fiendfyre, Protego Diabolica, Piertotum Locomotor…
* **Darkness**: dark spells feed it, and at 100 wraiths hunt you at night. The Deathly Hallows, the bestiary and potions.
* **Multiplayer**:
  * Statuses go to the creature's owner, and effects are drawn by every client.
  * Zones are network objects.
  * Balance and world rules come from the server.

## Requirements

* BepInExPack_Valheim
* Jötunn

Every player and the server need the mod.

## Keys

| Key | Action |
|-----|--------|
| LMB / MMB / RMB with a wand | chosen spell / Basic Cast / Protego |
| Left Alt + 1…8 or wheel | choose a spell slot |
| G | grand spell (full Focus) |
| K | the grimoire |

Keys, balance and world rules are in `BepInEx/config/com.oulassevich.wizardry.cfg`.

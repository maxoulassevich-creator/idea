# -*- coding: utf-8 -*-
"""Собирает дизайн-данные мода «Волшебство».

Читает tools/spells/*.txt и tools/tree_data.py и пишет:
  design/data/spells.json, design/data/tree.json,
  design/02_SPELLS.md, design/TREE.md, design/VFX_TABLE.md

Запуск: python3 tools/build_design.py
"""
import glob
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
DESIGN = os.path.join(ROOT, "design")
sys.path.insert(0, HERE)
import tree_data as T  # noqa: E402

# ---------------------------------------------------------------------------------------------- справочники
SCHOOL_RU = {c: ru for c, ru, *_ in T.SCHOOLS}
SCHOOL_RU["PRK"] = "Розыгрыши (набор Уизли)"
SCHOOL_ORDER = [c for c, *_ in T.SCHOOLS] + ["PRK"]

ROLES = {
    "basic": "Базовое", "tree": "Древо", "book": "Книга", "var": "Вариант", "meta": "Метамагия",
    "ritual": "Ритуал", "grand": "Великие чары", "prank": "Розыгрыш", "system": "Система", "lore": "Лор",
}
ARCH = {
    # код: (рус., коэфф. урона, база МС, перезарядка по умолчанию)
    "proj": ("снаряд", 1.0, 8, 1.0), "aoe": ("площадь", 0.8, 16, 6), "beam": ("луч", 0.45, 12, 0),
    "ctrl": ("контроль", 0.5, 14, 8), "summ": ("призыв", 0.6, 30, 30), "buff": ("бафф", 0.0, 20, 30),
    "heal": ("лечение", 1.4, 12, 4), "move": ("перемещение", 0.5, 15, 8), "util": ("утилита", 0.4, 6, 5),
    "ritual": ("ритуал", 0.0, 60, 600), "-": ("—", 0.0, 0, 0),
}
SCALES = {"L0": "личный", "L1": "локальный", "L2": "площадной", "L3": "массивный", "L4": "мировой"}
DMG_RU = {"blunt": "дробящий", "slash": "режущий", "pierce": "колющий", "fire": "огонь", "frost": "мороз",
          "lightning": "молния", "poison": "яд", "spirit": "дух", "chop": "рубка", "pickaxe": "кирка"}
SRC_RU = {"B": "книги", "F": "фильмы", "G": "игры", "W": "доп. канон", "N": "фанон", "M": "мод"}
ANIMS = {
    "flick": "Выпад", "slash": "Росчерк", "point": "Указание", "circle": "Круг", "lift": "Подъём",
    "pull": "Рывок к себе", "push": "Толчок", "stab": "Удар вниз", "raise": "Воздеть", "figure": "Узор",
    "touch": "Касание", "self": "К себе", "ritual": "Ритуал", "whip": "Хлыст", "snap": "Щелчок",
    "spiral": "Спираль", "strain": "Напряжение", "turn": "Разворот", "-": "—",
}
YEAR_RU = {1: "1", 2: "2", 3: "3", 4: "4", 5: "5 (СОВ)", 6: "6", 7: "7 (ЖАБА)", 8: "Мастер", 9: "Орден Мерлина"}


def year_factor(y):
    return 1 + 0.35 * (y - 1)


# ---------------------------------------------------------------------------------------------- заклинания
def load_spells():
    spells = []
    for f in sorted(glob.glob(os.path.join(HERE, "spells", "*.txt"))):
        for ln, line in enumerate(open(f, encoding="utf-8"), 1):
            line = line.rstrip("\n")
            if not line.strip() or line.startswith("#"):
                continue
            p = [x.strip() for x in line.split(" | ")]
            if len(p) != 16:
                raise SystemExit("%s:%d: %d полей вместо 16" % (f, ln, len(p)))
            (sid, inc, ru, en, src, sch, yr, role, arch, dmg, scale, anim, cd, effect, special, vfx) = p
            s = dict(id=sid, incantation=None if inc == "-" else inc, name_ru=ru, name_en=en,
                     sources=src.split(","), school=sch, year=int(yr), role=role, archetype=arch,
                     damage=[] if dmg == "-" else dmg.split(","), scale=scale, anim=anim,
                     effect=effect, vfx=None if vfx in ("-", "—") else vfx)
            assert sch in SCHOOL_ORDER, (sid, sch)
            assert role in ROLES, (sid, role)
            assert arch in ARCH, (sid, arch)
            assert scale in SCALES or scale == "-", (sid, scale)
            assert anim in ANIMS, (sid, anim)
            assert all(d in DMG_RU for d in s["damage"]), (sid, dmg)
            assert all(x in SRC_RU for x in s["sources"]), (sid, src)
            s["parent"] = None
            s["augments"] = {}
            if special.startswith("parent="):
                s["parent"] = special[len("parent="):]
            elif special not in ("-", ""):
                for part in special.split("; "):
                    if part[:3] in ("r3:", "r6:", "r9:"):
                        s["augments"][part[:2]] = part[3:].strip()
                    else:
                        s["augments"]["note"] = part
            s["cooldown"] = None if cd == "-" else float(cd)
            compute_numbers(s)
            spells.append(s)
    ids = {s["id"] for s in spells}
    assert len(ids) == len(spells), "повтор id"
    for s in spells:
        assert s["parent"] is None or s["parent"] in ids, (s["id"], s["parent"])
    return spells


def compute_numbers(s):
    """База на ранге 1 с палочкой силы 1.0 и навыком 100; ранг 10 — ×2.08 к эффекту и ×1.45 к стоимости."""
    name, coef, base_cost, base_cd = ARCH[s["archetype"]]
    y = s["year"]
    role = s["role"]
    s["numbers"] = None
    if role in ("meta", "system", "lore") or s["archetype"] == "-":
        return
    n = {}
    if s["damage"] and coef > 0:
        d1 = 12 * year_factor(y) * coef
        n["damage_r1"] = round(d1)
        n["damage_r10"] = round(d1 * 2.08)
        if s["archetype"] == "beam":
            n["per"] = "за тик 0.25 с"
    if s["archetype"] == "heal":
        h1 = 12 * year_factor(y) * coef
        n["heal_r1"] = round(h1)
        n["heal_r10"] = round(h1 * 2.08)
    if role == "grand":
        n["cost"] = "Средоточие 100 + %d МС" % round(30 * (1 + 0.25 * (y - 1)))
        n["cooldown"] = "по Средоточию"
    elif role == "basic" and s["id"] in ("basic_cast", "lumos", "nox"):
        n["cost"] = "0 МС"
        n["cooldown"] = "%g с" % (s["cooldown"] or 0) if s["cooldown"] else "—"
    elif s["id"] == "protego":
        n["cost"] = "МС вместо выносливости при блоке"
        n["cooldown"] = "—"
    else:
        c = base_cost * (1 + 0.25 * (y - 1))
        n["cost"] = ("%d МС/с" % round(c)) if s["archetype"] == "beam" else ("%d МС" % round(c))
        cd = s["cooldown"] if s["cooldown"] is not None else base_cd
        n["cooldown"] = ("поддерживаемое" if s["archetype"] == "beam" and not cd
                         else ("%g с" % cd if cd < 60 else "%g мин" % (cd / 60)))
        if s["role"] == "ritual":
            n["cost"] += " + материалы"
    s["numbers"] = n


# ---------------------------------------------------------------------------------------------- древо
def P(r, deg):
    a = math.radians(deg)
    return (round(r * math.cos(a), 3), round(r * math.sin(a), 3))


def norm(a):
    return ((a + 180) % 360) - 180


def hour_angle(h):
    return norm(90 - 30 * (h % 12))


class Tree:
    def __init__(self):
        self.nodes = []
        self.index = {}
        self.edges = []

    def add(self, nid, kind, pos, **kw):
        assert nid not in self.index, nid
        node = dict(id=nid, type=kind, x=pos[0], y=pos[1], **kw)
        self.index[nid] = node
        self.nodes.append(node)
        return node

    def link(self, a, b):
        self.edges.append([a, b])


def stat_text(school, k, ring):
    tmpl, base = T.SMALL_STATS[school][k % len(T.SMALL_STATS[school])]
    mult = (1.0, 1.25, 1.5)[ring - 1]
    return tmpl.format(v=int(base * mult + 0.5))


def build_tree(spells):
    t = Tree()
    by_school = {}
    for s in spells:
        if s["role"] == "tree":
            by_school.setdefault(s["school"], []).append(s)
    house_spells = {h["spell"]: hid for hid, h in T.HOUSES.items()}
    angle = {c: hour_angle(h) for c, _, _, h, _, _ in T.SCHOOLS}
    forbidden = {"DRK", "MND"}

    # центр: Большой зал
    for i, (c, *_rest) in enumerate(T.SCHOOLS):
        t.add("hub_" + c, "small", P(1.2, angle[c]), ring=0, year=1, school=None,
              name="Большой зал", text=("+3 МС" if i % 2 == 0 else "+2% урона чар"))
    for i in range(12):
        t.link("hub_" + T.SCHOOLS[i][0], "hub_" + T.SCHOOLS[(i + 1) % 12][0])

    # школы
    small_counter = {}
    for c, ru, en, hour, house, color in T.SCHOOLS:
        th = angle[c]
        notes = list(T.NOTABLES[c])
        k = 0

        def small(nid, pos, ring, year):
            nonlocal k
            n = t.add(nid, "small", pos, ring=ring, year=year, school=c, name=ru, text=stat_text(c, k, ring))
            k += 1
            return n

        def notable(nid, pos, ring, year):
            nm, tx = notes.pop(0)
            return t.add(nid, "notable", pos, ring=ring, year=year, school=c, name=nm, text=tx)

        # кольцо I
        small(c + "_e", P(2.6, th), 1, 1)
        small(c + "_a1", P(3.05, th - 7), 1, 1)
        small(c + "_a2", P(3.05, th + 7), 1, 1)
        notable(c + "_n1", P(3.45, th - 12), 1, 1)
        notable(c + "_n2", P(3.45, th + 12), 1, 1)
        small(c + "_b0", P(3.55, th), 1, 1)
        for a, b in [("e", "a1"), ("e", "a2"), ("a1", "n1"), ("a2", "n2"), ("a1", "b0"), ("a2", "b0")]:
            t.link(c + "_" + a, c + "_" + b)

        # кольцо II: колесо вокруг мастерства
        M = P(5.35, th)
        u = (math.cos(math.radians(th)), math.sin(math.radians(th)))
        v = (-u[1], u[0])

        def local(phi, d):
            a = math.radians(phi)
            return (round(M[0] + d * (math.cos(a) * u[0] + math.sin(a) * v[0]), 3),
                    round(M[1] + d * (math.cos(a) * u[1] + math.sin(a) * v[1]), 3))

        ry2 = 3
        for i, phi in enumerate([180, 135, 90, 45, 0, -45, -90, -135]):
            nid = "%s_w%d" % (c, i)
            if i == 2 or i == 6:
                notable(nid, local(phi, 0.6), 2, ry2)
            else:
                small(nid, local(phi, 0.6), 2, ry2)
        for i in range(8):
            t.link("%s_w%d" % (c, i), "%s_w%d" % (c, (i + 1) % 8))
        t.link(c + "_b0", c + "_w0")
        t.add(c + "_m", "mastery", M, ring=2, year=ry2, school=c, name="Мастерство: " + ru,
              text="Выберите одно:", options=T.MASTERIES[c])
        t.link(c + "_m", c + "_w0")
        notable(c + "_n5", local(130, 1.1), 2, ry2)
        t.link(c + "_w1", c + "_n5")
        t.add(c + "_s1", "socket", local(-130, 1.1), ring=2, year=ry2, school=c, name="Гнездо Омута",
              text="Гнездо для Воспоминания.")
        t.link(c + "_w7", c + "_s1")

        # кольцо III
        ry3 = 6 if c in forbidden else 5
        small(c + "_t0", P(6.9, th), 3, ry3)
        small(c + "_t1", P(7.35, th - 6), 3, ry3)
        small(c + "_t2", P(7.85, th - 10), 3, ry3)
        small(c + "_t3", P(7.35, th + 6), 3, ry3)
        small(c + "_t4", P(7.85, th + 10), 3, ry3)
        small(c + "_t5", P(7.55, th), 3, ry3)
        notable(c + "_n6", P(8.3, th - 12.5), 3, ry3)
        notable(c + "_n7", P(8.3, th + 12.5), 3, ry3)
        notable(c + "_n8", P(8.15, th), 3, ry3)
        t.add(c + "_s2", "socket", P(7.75, th - 3.6), ring=3, year=ry3, school=c, name="Гнездо Омута",
              text="Гнездо для Воспоминания.")
        for a, b in [("w4", "t0"), ("t0", "t1"), ("t1", "t2"), ("t2", "n6"), ("t0", "t3"), ("t3", "t4"),
                     ("t4", "n7"), ("t0", "t5"), ("t5", "n8"), ("t5", "s2")]:
            t.link(c + "_" + a, c + "_" + b)

        # обод: обет
        kn, kt = T.KEYSTONES[c]
        t.add(c + "_key", "keystone", P(9.3, th), ring=4, year=7, school=c, name=kn, text=kt)
        t.link(c + "_n8", c + "_key")
        assert not notes, c

        # узлы заклинаний: дуги у каждого кольца, связь с ближайшим узлом своего кольца
        rings = {1: [], 2: [], 3: []}
        for s in sorted(by_school.get(c, []), key=lambda s: (s["year"], s["id"])):
            rings[1 if s["year"] <= 2 else (2 if s["year"] <= 4 else 3)].append(s)
        arcs = {
            1: (4.05, [-11, 11, -5.5, 5.5], [(3.0, 0)], ["n1", "a1", "b0", "a2", "n2", "e"], 1),
            2: (6.4, [-11, 11, -5, 5], [], ["w3", "w4", "w5", "w2", "w6"], ry2),
            3: (8.85, [-12, 12, -6, 6, -3], [], ["n6", "t2", "t5", "n8", "t4", "n7"], ry3),
        }
        for ring, lst in rings.items():
            r, degs, extra, anchors, ry = arcs[ring]
            positions = [P(r, th + d) for d in degs] + [P(rr, th + dd) for rr, dd in extra]
            assert len(lst) <= len(positions), (c, ring, len(lst))
            for s, pos in zip(lst, positions):
                nid = "sp_" + s["id"]
                given = house_spells.get(s["id"])
                t.add(nid, "spell", pos, ring=ring, year=max(ry, s["year"]), school=c, name=s["name_ru"],
                      text=s["effect"], spell=s["id"],
                      house_free=(T.HOUSES[given]["ru"] if given else None))
                best = min(anchors, key=lambda a: dist(t.index[c + "_" + a], pos))
                t.link(c + "_" + best, nid)
        small_counter[c] = k

    # гибриды на границах
    for i, (c, *_rest) in enumerate(T.SCHOOLS):
        d = T.SCHOOLS[(i + 1) % 12][0]
        a_ang = angle[c]
        # следующая школа по часовой стрелке имеет угол на 30° меньше
        nm, tx = T.HYBRIDS[(c, d)]
        nid = "hy_%s_%s" % (c, d)
        t.add(nid, "notable", P(5.35, a_ang - 15), ring=2, year=3, school=None, hybrid=[c, d], name=nm, text=tx)
        t.link(c + "_w6", nid)   # сторона -90° (по часовой) у школы c
        t.link(nid, d + "_w2")   # сторона +90° у школы d

    # факультеты
    for hid, h in T.HOUSES.items():
        th = hour_angle(h["hour"])
        center = [c for c, *_r in T.SCHOOLS if angle[c] == th][0]
        t.add("house_" + hid, "house", P(1.9, th), ring=0, year=0, school=None, house=hid, name=h["ru"],
              text="%s. %s Первое заклинание: %s." % (h["motto"], h["start"], h["spell"]))
        t.link("hub_" + center, "house_" + hid)
        t.link("house_" + hid, center + "_e")
        for sign in (+1, -1):
            fl = [c for c, *_r in T.SCHOOLS if angle[c] == norm(th + sign * 30)][0]
            lid = "yard_%s_%s" % (hid, fl)
            t.add(lid, "small", P(2.25, th + sign * 15), ring=0, year=1, school=None, name="Двор " + h["ru"],
                  text="+4 ОЗ")
            t.link("house_" + hid, lid)
            t.link(lid, fl + "_e")
        rn, rt = h["relic"]
        t.add("relic_" + hid, "relic", P(1.65, th + 11), ring=0, year=8, school=None, house=hid, name=rn, text=rt)
        t.link("house_" + hid, "relic_" + hid)
    for c, *_r in T.SCHOOLS:
        if not any(e == ["house_" + hid, c + "_e"] for hid in T.HOUSES for e in t.edges):
            t.link("hub_" + c, c + "_e")

    # призвания — отдельные мини-древа
    specs = []
    for sid, ru, role, smalls, notables, caps in T.SPECS:
        sn = []

        def sp(nid, kind, x, y, name, text):
            sn.append(dict(id="%s_%s" % (sid, nid), type=kind, x=x, y=y, name=name, text=text))

        sp("root", "house", 0, 0, ru, role)
        sp("s1", "small", -0.8, 0.9, ru, smalls[0])
        sp("s2", "small", 0.8, 0.9, ru, smalls[1])
        pos = [(-1.8, 1.9), (-0.9, 2.15), (0.9, 2.15), (1.8, 1.9), (-2.1, 3.0), (-0.8, 3.25), (0.8, 3.25), (2.1, 3.0)]
        for i, ((nm, tx), (x, y)) in enumerate(zip(notables, pos)):
            sp("n%d" % (i + 1), "notable", x, y, nm, tx)
        sp("c1", "keystone", -0.6, 4.35, *caps[0])
        sp("c2", "keystone", 0.6, 4.35, *caps[1])
        e = [("root", "s1"), ("root", "s2"), ("s1", "n1"), ("s1", "n2"), ("s2", "n3"), ("s2", "n4"),
             ("n1", "n5"), ("n2", "n6"), ("n3", "n7"), ("n4", "n8"), ("n6", "c1"), ("n7", "c2")]
        specs.append(dict(id=sid, name=ru, role=role, nodes=sn,
                          edges=[["%s_%s" % (sid, a), "%s_%s" % (sid, b)] for a, b in e]))

    tree = dict(
        version=1, game="Valheim 1.0.21",
        schools=[dict(code=c, name=ru, name_en=en, hour=h, house=hs, color=col, angle=angle[c])
                 for c, ru, en, h, hs, col in T.SCHOOLS],
        houses=[dict(id=k, **{kk: vv for kk, vv in v.items() if kk != "relic"}) for k, v in T.HOUSES.items()],
        nodes=t.nodes, edges=t.edges, specializations=specs,
        hallows=dict(name=T.HALLOWS[0], text=T.HALLOWS[1]),
    )
    check_connected(tree)
    return tree


def dist(n, pos):
    return math.hypot(n["x"] - pos[0], n["y"] - pos[1])


def check_connected(tree):
    adj = {n["id"]: set() for n in tree["nodes"]}
    for a, b in tree["edges"]:
        assert a in adj and b in adj, (a, b)
        adj[a].add(b)
        adj[b].add(a)
    seen = set()
    stack = ["house_gryffindor"]
    while stack:
        x = stack.pop()
        if x in seen:
            continue
        seen.add(x)
        stack.extend(adj[x] - seen)
    lost = [n for n in adj if n not in seen]
    assert not lost, lost
    # слишком близкие узлы
    pts = [(n["id"], n["x"], n["y"]) for n in tree["nodes"]]
    close = []
    for i in range(len(pts)):
        for j in range(i + 1, len(pts)):
            d = math.hypot(pts[i][1] - pts[j][1], pts[i][2] - pts[j][2])
            if d < 0.3:
                close.append((pts[i][0], pts[j][0], round(d, 2)))
    if close:
        print("ВНИМАНИЕ: близкие узлы:", close)


# ---------------------------------------------------------------------------------------------- markdown
def md_escape(s):
    return (s or "").replace("|", "\\|")


def spell_title(s):
    t = "**%s**" % s["name_ru"]
    if s["incantation"] and s["incantation"] != s["name_ru"]:
        t += " · *%s*" % s["incantation"]
    t += "<br><sub>%s</sub>" % md_escape(s["name_en"])
    return t


def numbers_cell(s):
    n = s["numbers"]
    if not n:
        return "—"
    parts = []
    if "damage_r1" in n:
        parts.append("урон %g→%g%s" % (n["damage_r1"], n["damage_r10"], (" " + n["per"]) if "per" in n else ""))
    if "heal_r1" in n:
        parts.append("лечение %d→%d" % (n["heal_r1"], n["heal_r10"]))
    if n.get("cost"):
        parts.append(n["cost"])
    if n.get("cooldown"):
        parts.append("КД " + n["cooldown"])
    return "<br>".join(parts)


def augments_cell(s, by_id):
    if s["parent"]:
        return "← %s" % by_id[s["parent"]]["name_ru"]
    a = s["augments"]
    out = []
    for k in ("r3", "r6", "r9", "note"):
        if k in a:
            out.append(("р%s: " % k[1:] if k != "note" else "") + a[k])
    return "<br>".join(out) or "—"


def write_spells_md(spells):
    by_id = {s["id"]: s for s in spells}
    from collections import Counter
    roles = Counter(s["role"] for s in spells)
    srcs = Counter(x for s in spells for x in s["sources"])
    L = []
    L.append("# Каталог заклинаний\n")
    L.append("> Файл собирается скриптом `tools/build_design.py` из `tools/spells/*.txt` — правьте исходники, а не этот файл.\n")
    L.append("Всего **%d** заклинаний: все 345 записей HP-вики (в выгрузке PotterDB) — книги, фильмы, игры, "
             "Wizards Unite и Hogwarts Mystery — плюс заклинания из фанфиков и придуманные для мода по мотивам канона.\n" % len(spells))
    L.append("| Роль | Сколько | Что значит |\n|---|---|---|")
    meaning = {
        "basic": "есть у всех с самого начала", "tree": "изучается узлом Древа (1 очко)",
        "book": "изучается книгой у Кодекса", "var": "вариант/форма родителя, выбирается узором на ранге 3/6/9",
        "meta": "метамагия: вставляется в слоты заклинаний", "ritual": "ритуал за материалы, обычно у Кодекса",
        "grand": "тратит полную шкалу Средоточия", "prank": "случайный сглаз из «Набора Уизли»",
        "system": "встроено в механику (обеты, анимагия, крестраж)", "lore": "лор или пасхалка",
    }
    for r in ROLES:
        L.append("| %s | %d | %s |" % (ROLES[r], roles.get(r, 0), meaning[r]))
    L.append("")
    L.append("Источники: " + ", ".join("%s — %d" % (SRC_RU[k], srcs[k]) for k in SRC_RU) + ".\n")
    L.append("## Как читать таблицы\n")
    L.append("* **Курс** — с какого курса (боссы, см. [01_PROGRESSION.md](01_PROGRESSION.md#2-курсы-прогресс-по-боссам)) "
             "заклинание можно изучить.")
    L.append("* **Числа** — база на ранге 1 → ранге 10 при палочке силы 1.0 и навыке 100. Формулы — в "
             "[01_PROGRESSION.md](01_PROGRESSION.md#12-формулы-и-цели-баланса). МС — стоимость в магической силе "
             "(эйтре), КД — перезарядка.")
    L.append("* **Развилки** — особые узоры заклинания на рангах 3/6/9; вторая опция берётся из общего пула архетипа. "
             "«← Родитель» — это вариант, который открывается узором родителя.")
    L.append("* Анимация и визуальный эффект каждого заклинания — в [VFX_TABLE.md](VFX_TABLE.md).\n")
    for sch in SCHOOL_ORDER:
        lst = [s for s in spells if s["school"] == sch]
        L.append("## %s (%d)\n" % (SCHOOL_RU[sch], len(lst)))
        if sch == "PRK":
            L.append("Шуточные сглазы из канона почти не влияют на бой, поэтому они собраны в одну книгу "
                     "«Набор Уизли». Слот «Розыгрыш» накладывает случайный изученный сглаз; у каждого есть "
                     "небольшой игровой смысл.\n")
        L.append("| Заклинание | Курс | Роль · тип | Урон · числа | Эффект в Valheim | Развилки | Источник |")
        L.append("|---|---|---|---|---|---|---|")
        order = {"basic": 0, "tree": 1, "grand": 2, "ritual": 3, "book": 4, "meta": 5, "var": 6, "prank": 7, "system": 8, "lore": 9}
        for s in sorted(lst, key=lambda s: (order[s["role"]], s["year"], s["name_ru"])):
            dm = ", ".join(DMG_RU[d] for d in s["damage"])
            typ = ARCH[s["archetype"]][0]
            L.append("| %s | %s | %s%s%s | %s | %s | %s | %s |" % (
                spell_title(s), YEAR_RU[s["year"]], ROLES[s["role"]],
                (" · " + typ) if typ != "—" else "", (" · " + dm) if dm else "",
                numbers_cell(s), md_escape(s["effect"]), md_escape(augments_cell(s, by_id)),
                ", ".join(SRC_RU[x] for x in s["sources"])))
        L.append("")
    open(os.path.join(DESIGN, "02_SPELLS.md"), "w", encoding="utf-8").write("\n".join(L) + "\n")


def write_vfx_md(spells):
    L = ["# Анимации и эффекты по заклинаниям\n",
         "> Собирается скриптом `tools/build_design.py`. Словарь анимаций, масштабов и цветов школ — в "
         "[03_ANIMATION_VFX.md](03_ANIMATION_VFX.md).\n"]
    for sch in SCHOOL_ORDER:
        lst = [s for s in spells if s["school"] == sch and s["role"] not in ("lore", "system", "meta")]
        if not lst:
            continue
        L.append("## %s\n" % SCHOOL_RU[sch])
        L.append("| Заклинание | Анимация | Масштаб | Визуальный эффект |\n|---|---|---|---|")
        for s in sorted(lst, key=lambda s: (s["year"], s["name_ru"])):
            L.append("| %s | %s | %s | %s |" % (
                s["name_ru"], ANIMS[s["anim"]], SCALES.get(s["scale"], "—"), md_escape(s["vfx"] or "—")))
        L.append("")
    meta = [s for s in spells if s["role"] == "meta"]
    L.append("## Метамагия\n\n| Метамагия | Как выглядит |\n|---|---|")
    for s in meta:
        L.append("| %s | %s |" % (s["name_ru"], md_escape(s["vfx"] or "—")))
    open(os.path.join(DESIGN, "VFX_TABLE.md"), "w", encoding="utf-8").write("\n".join(L) + "\n")


def write_tree_md(tree, spells):
    by_id = {s["id"]: s for s in spells}
    nodes = tree["nodes"]
    from collections import Counter
    cnt = Counter(n["type"] for n in nodes)
    L = ["# Древо Чар: все узлы\n",
         "> Собирается скриптом `tools/build_design.py` из `tools/tree_data.py`. Правила — в "
         "[01_PROGRESSION.md](01_PROGRESSION.md#3-древо-чар).\n",
         "Всего узлов: **%d** (связей: %d)." % (len(nodes), len(tree["edges"])),
         "",
         "| Тип | Сколько |", "|---|---|"]
    names = {"small": "малые", "notable": "значимые (вкл. 12 гибридов)", "spell": "заклинания",
             "mastery": "мастерства", "socket": "гнёзда Омута", "keystone": "Непреложные обеты",
             "house": "старты факультетов", "relic": "реликвии основателей"}
    for k, v in names.items():
        L.append("| %s | %d |" % (v, cnt.get(k, 0)))
    L.append("\nКольца: I — с 1 курса, II — с 3, III — с 5 (Тёмные искусства и Разум — с 6, Запретная секция), "
             "обод (обеты) — с 7.\n")

    L.append("## Факультеты\n")
    L.append("| Факультет | Врождённое | Первое заклинание | Реликвия основателя (Мастер) |\n|---|---|---|---|")
    for hid, h in T.HOUSES.items():
        L.append("| **%s** — %s | %s | %s | **%s**: %s |" % (h["ru"], h["motto"], h["start"],
                                                          by_id[h["spell"]]["name_ru"], h["relic"][0], h["relic"][1]))
    L.append("")
    for c, ru, en, hour, house, color in T.SCHOOLS:
        L.append("## %s\n" % ru)
        L.append("Факультет рядом: %s. Параметры малых узлов: %s.\n" % (
            T.HOUSES[house]["ru"], "; ".join(tm.format(v=b) for tm, b in T.SMALL_STATS[c])))
        L.append("| Кольцо | Значимый узел | Эффект |\n|---|---|---|")
        for n in nodes:
            if n.get("school") == c and n["type"] == "notable":
                L.append("| %s | **%s** | %s |" % ("I II III".split()[n["ring"] - 1], n["name"], md_escape(n["text"])))
        L.append("")
        L.append("**Мастерство** (одно на выбор): " + " · ".join(T.MASTERIES[c]) + ".\n")
        kn, kt = T.KEYSTONES[c]
        L.append("**Непреложный обет — %s.** %s\n" % (kn, kt))
        sp = [n for n in nodes if n.get("school") == c and n["type"] == "spell"]
        if sp:
            L.append("**Заклинания на Древе:** " + ", ".join(
                "%s (курс %s%s)" % (n["name"], YEAR_RU[by_id[n["spell"]]["year"]],
                                     ", даром для факультета " + n["house_free"] if n.get("house_free") else "")
                for n in sorted(sp, key=lambda n: (n["year"], n["name"]))) + ".\n")
    L.append("## Гибридные узлы\n")
    L.append("| Между школами | Узел | Эффект |\n|---|---|---|")
    for (a, b), (nm, tx) in T.HYBRIDS.items():
        L.append("| %s — %s | **%s** | %s |" % (SCHOOL_RU[a], SCHOOL_RU[b], nm, tx))
    L.append("\n## Призвания\n")
    L.append("Восемь очков призвания: Модер, Яглут, Королева, Фейдер — по 2. В каждом призвании 12 узлов; "
             "до обеих вершин ведут по 4 узла, поэтому взять можно либо обе вершины, либо одну вершину и 4 других узла.\n")
    for sp in tree["specializations"]:
        L.append("### %s — %s\n" % (sp["name"], sp["role"]))
        L.append("| Узел | Эффект |\n|---|---|")
        for n in sp["nodes"][1:]:
            mark = {"small": "", "notable": "**", "keystone": "★ **"}[n["type"]]
            end = {"small": "", "notable": "**", "keystone": "**"}[n["type"]]
            L.append("| %s%s%s | %s |" % (mark, n["name"] if n["type"] != "small" else "малый", end, md_escape(n["text"])))
        L.append("")
    L.append("## Дары Смерти\n")
    L.append("**%s.** %s\n" % (tree["hallows"]["name"], tree["hallows"]["text"]))
    open(os.path.join(DESIGN, "TREE.md"), "w", encoding="utf-8").write("\n".join(L) + "\n")


def main():
    spells = load_spells()
    tree = build_tree(spells)
    os.makedirs(os.path.join(DESIGN, "data"), exist_ok=True)
    with open(os.path.join(DESIGN, "data", "spells.json"), "w", encoding="utf-8") as f:
        json.dump(dict(version=1, game="Valheim 1.0.21", spells=spells), f, ensure_ascii=False, indent=1)
    with open(os.path.join(DESIGN, "data", "tree.json"), "w", encoding="utf-8") as f:
        json.dump(tree, f, ensure_ascii=False, indent=1)
    write_spells_md(spells)
    write_vfx_md(spells)
    write_tree_md(tree, spells)
    from collections import Counter
    print("заклинаний:", len(spells), dict(Counter(s["role"] for s in spells)))
    print("узлов:", len(tree["nodes"]), dict(Counter(n["type"] for n in tree["nodes"])), "связей:", len(tree["edges"]))


if __name__ == "__main__":
    main()

# -*- coding: utf-8 -*-
"""Собирает интерактивное превью Древа (design/tree_preview.html) из шаблона и design/data/*.json.

Запуск после build_design.py: python3 tools/build_preview.py
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DESIGN = os.path.join(os.path.dirname(HERE), "design")

tree = json.load(open(os.path.join(DESIGN, "data", "tree.json"), encoding="utf-8"))
spells = json.load(open(os.path.join(DESIGN, "data", "spells.json"), encoding="utf-8"))["spells"]
on_tree = {n["spell"] for n in tree["nodes"] if n["type"] == "spell"}
compact = {s["id"]: dict(incantation=s["incantation"], numbers=s["numbers"], augments=s["augments"], vfx=s["vfx"])
           for s in spells if s["id"] in on_tree}

page = open(os.path.join(HERE, "preview_template.html"), encoding="utf-8").read()
dump = lambda o: json.dumps(o, ensure_ascii=False, separators=(",", ":")).replace("</", "<\\/")
page = page.replace("/*DATA*/null", dump(tree)).replace("/*SPELLS*/null", dump(compact))
# Файл в репозитории — самостоятельная страница; без обёртки (аргумент) — тело для публикации как Artifact.
standalone = ('<!doctype html>\n<html lang="ru">\n<head>\n<meta charset="utf-8">\n'
              '<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">\n'
              '<style>:root{padding-top:env(safe-area-inset-top,0px);padding-bottom:env(safe-area-inset-bottom,0px)}'
              'body{margin:0}[hidden]{display:none!important}</style>\n</head>\n<body>\n' + page + '\n</body>\n</html>\n')
out = os.path.join(DESIGN, "tree_preview.html")
open(out, "w", encoding="utf-8").write(standalone)
print(out, len(standalone.encode("utf-8")), "байт")
if len(sys.argv) > 1:
    open(sys.argv[1], "w", encoding="utf-8").write(page)
    print(sys.argv[1])

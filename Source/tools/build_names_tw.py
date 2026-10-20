#!/usr/bin/env python3
"""Adds the TwoWorlds developers' spreadsheet (GameInterface.xls) to interfacemap_names.json.

    python build_names_tw.py GameInterface.xls _GameInterface.txt _GameInterfaceX.txt interfacemap_names.json

Needs xlrd. The item names of the original definition files are taken from the spreadsheet
(the .txt files were generated from it), so this adds what the .txt files do not contain:
item groups, the Xbox file version, the TwoWorlds Light / FaceColor presets (different values
than in Earth 2160), special state sets ($Special_*), fonts with their descriptions and <F#>
tags, developers' notes on items and dialog controls, and CustomData descriptions."""
import json
import re
import sys

import xlrd

import ifacedb

TR = {
    # groups
    "Itemy zdefiniowane w kodzie": "Items defined in the game code (fixed numbers)",
    "Możliwość zmiany numerów": "Numbers can be changed",
    "Dialogi F1-F5": "Dialogs F1-F5",
    # item names (Polish in the spreadsheet)
    "Fonty do taga <F#>": "Fonts for the <F#> text tag",
    "Dialog Commands (pasek)": "Dialog Commands (bar)",
    "Dialog XCommands (kółko graniaste)": "Dialog XCommands (radial menu)",
    "Ciemne tło": "Dark background",
    "Main Menu Button (tylko podswietlenie)": "Main Menu Button (highlight only)",
    "do magii potrzebny button który nie ma takiej wielkiej ramki": "Button for magic, without the big frame",
    # font comments
    "Description - był Arial (Index_0), bez cienia": "Description - no shadow (was Arial, Index_0)",
    # dialog / control notes
    "Dodać jakąś ikonkę (klucz?)": "To do: add some icon (a key?)",
    "Chyba lepiej wygląda z przyciemnieniem": "Probably looks better with darkening",
    "template statika ilosci": "Template of the quantity static",
    "Kursor dla prawego klawisza myszki": "Cursor for the right mouse button",
    "template buttona dla potionow i pulapek": "Template of the button for potions and traps",
    "template buttona dla kart magii": "Template of the button for magic cards",
    "template pustego buttona dla kart magii": "Template of the empty button for magic cards",
    "tlo dla dialogu auto": "Background of the auto dialog",
    "tlo dla dialogu custom": "Background of the custom dialog",
    "knife - nieuzywane, moze byc sword": "Knife - unused, can be the sword",
    "crossbow - nieuzywane, moze byc bow": "Crossbow - unused, can be the bow",
    "template'y pustych zdisablowanych buttonow": "Templates of the empty disabled buttons",
    "biezacy button wyswietlany pojedynczo": "Current button, shown on its own",
    "template'y podswietlenia wybranego buttona": "Templates of the highlight of the selected button",
    "Wywalić?": "To be removed?",
    "static selekcji": "Selection static",
    "Sloty czarów (0-2) i boostery (3-5, 6-8, 9-11)": "Spell slots (0-2) and boosters (3-5, 6-8, 9-11)",
    "Karty": "Cards",
    "Napis nad boosterami": "Label above the boosters",
    "Boostery": "Boosters",
    "przesuniecie w prawo polozenia tooltipsa dla skilli": "Moves the skill tooltip position to the right",
    "bylo 146": "Was item 146",
}
SKIP_NOTES = {"UWAGA: wyszukiwanie nie bierze pod uwagę zwiniętych linii!"}

CUSTOMDATA = {
    "messagebox": "Message box: background, texture, then 12 rectangles x1,y1,x2,y2: inside, corners "
                  "top-left, top-right, bottom-left, bottom-right, frame top, bottom, left, right, "
                  "corner 'centre' (TL/BR), shift and margins. Scale X/Y = base resolution of the coordinates",
    "mapmasktexture": "Texture of the map mask",
    "mapmaskcoordinates": "Map mask: width, height, midX, midY, rangeMask, rangeMapSign. The mask starts at 0,0 "
                          "of the map particle, scale 1 for 1024; map signs are shown within rangeMapSign "
                          "(a bit smaller than rangeMask)",
    "customwavexctrlselect": "Sound: changing the selected item with the gamepad",
    "customwavescrolllist": "Sound: scrolling a list box",
    "customwaveopenclosecombo": "Sound: opening / closing a combo box",
    "customwavesetslider": "Sound: moving a slider by one step (start of scrolling)",
    "customwavescrollslider": "Sound: continuous (looped) slider scrolling",
    "customwavecustom1": "Sound: scrolling the hero types in the hero creation dialog",
    "customwavecustom2": "Sound: opening / closing quests",
}

FACECOLOR_DESC = {"$DisabledColor": "Grey face colour of disabled controls",
                  "$PressedColor": "Slightly darker face colour of pressed buttons"}
STATE_SET_DESC = {"$Special_Skill": "Skill command: red-tinted copies of the states (0x20-0x30)",
                  "$Special_Inventory": "Inventory button (HERO dialog): red-tinted 0x20-0x30, "
                                        "green 0x40-0x50 and 0x60-0x70",
                  "$Special_ScrollInventory": "Inventory scroll buttons: tinted copies (0x20-0x2C)"}
SUSPECTED = "Developers' note: possibly used directly by the game code, or unused"


def tr(s: str) -> str:
    s = " ".join(str(s).split())
    return TR.get(s, s)


def num(v):
    if isinstance(v, float):
        return int(v)
    v = str(v).strip()
    return int(v, 0) if v else None


def sheet_rows(book, name):
    sh = book.sheet_by_name(name)
    return [[sh.cell_value(r, c) for c in range(sh.ncols)] for r in range(sh.nrows)]


def expand(name: str):
    m = re.match(r"(.*)\[(\d+)-(\d+)\](.*)$", name)
    if not m:
        return [name]
    return [f"{m.group(1)}{i}{m.group(4)}" for i in range(int(m.group(2)), int(m.group(3)) + 1)]


def main(xls, txt_pc, txt_x, out):
    book = xlrd.open_workbook(xls)
    db = json.load(open(out, encoding="utf-8"))
    tw = db["games"]["tw"]

    # ---------------------------------------------------------------- Defines
    defines = {}
    cur = None
    for r in sheet_rows(book, "Defines")[2:]:
        a, b = str(r[0]).strip(), str(r[1]).strip()
        c = str(r[2]).strip() if len(r) > 2 else ""
        if a.startswith("$"):
            cur = a
            defines[a] = {"value": b, "comment": c}
        elif not a and b and cur and b.startswith("State:"):
            defines[cur]["value"] += " " + b          # continued macro
            if c:
                defines[cur]["comment"] = c
        else:
            cur = None

    def resolve(v):
        for _ in range(5):
            v = re.sub(r"\$\w+", lambda m: defines[m.group(0)]["value"] if m.group(0) in defines else m.group(0), v)
        return v

    lights, facecolors, state_sets = [], [], []
    for name, d in defines.items():
        v = resolve(d["value"])
        if v.startswith("Light "):
            lights.append({"name": name, "desc": d["comment"],
                           "values": [int(x.strip(), 0) for x in v[6:].split(",")]})
        elif v.startswith("FaceColor ") and "|" not in v:
            facecolors.append({"name": name, "desc": FACECOLOR_DESC.get(name, ""), "value": int(v.split()[1], 0)})
        elif name.startswith("$Special_"):
            sets = []
            for sn, src, fc in re.findall(r"State:\s*(0x[0-9A-Fa-f]+)\s*\w*\s*\|\{\|\s*Identity\s*(0x[0-9A-Fa-f]+)"
                                          r"\s*\|\s*FaceColor\s*(0x[0-9A-Fa-f]+)", v):
                sets.append([int(sn, 16), int(src, 16), int(fc, 16)])
            state_sets.append({"name": name, "desc": STATE_SET_DESC.get(name, d["comment"]), "states": sets})
    lights.sort(key=lambda p: list(defines).index(p["name"]))
    tw["light_presets"] = lights
    tw["facecolor_presets"] = facecolors
    tw["state_sets"] = state_sets

    # ------------------------------------------------------------------ Items
    groups, platform, grp = {}, {}, None
    for r in sheet_rows(book, "Items")[2:]:
        a = str(r[0]).strip()
        n = None
        try:
            n = int(float(a))
        except ValueError:
            pass
        if n is None:
            if a:
                grp = tr(a)
            continue
        if grp:
            groups[str(n)] = grp
        platform.setdefault(n, set()).add(str(r[2]).strip())
    tw["groups"] = groups

    notes = {}
    for r in sheet_rows(book, "~")[2:8]:
        for v in r[1:]:
            s = str(v).strip()
            if not s:
                continue
            if re.match(r"^\d+-\d+$", s):
                a, b = map(int, s.split("-"))
                for i in range(a, b + 1):
                    notes[str(i)] = SUSPECTED
            else:
                notes[str(int(float(s)))] = SUSPECTED
    tw["item_notes"] = notes

    # states: drop the bare "Special" names (the generic "Special: Normal" etc. say more);
    # item 50: state N = font of the <FN> tag
    tw["states"] = {k: {s: n for s, n in v.items() if n not in ("Special", "Special2", "Special12")}
                    for k, v in tw["states"].items()}
    tw["states"] = {k: v for k, v in tw["states"].items() if v}

    # ------------------------------------------------------------------ Fonts
    fonts, prev_desc = [], ""
    rows = sheet_rows(book, "Fonts")[2:]
    for r in rows:
        if str(r[0]).strip() == "":
            prev_desc = ""
            continue
        nr = num(r[0])
        col = lambda i: (str(r[i]).strip() if i < len(r) else "")
        sc = col(8)
        scolor = 0xFF000000 if sc == "$black" else (int(sc, 0) if sc else 0xFF0F0F0F)
        desc = tr(col(9)) or prev_desc
        prev_desc = desc
        tags = [int(float(t)) for t in (r[10], r[11]) if str(t).strip() != ""] if len(r) > 11 else []
        fonts.append({"nr": nr, "name": col(1), "size": num(r[2]), "weight": num(r[3]), "italic": num(r[4]),
                      "color": int(col(5), 0), "sx": num(r[6]) or 0, "sy": num(r[7]) or 0, "scolor": scolor,
                      "desc": desc, "tag": tags or None})
    db["fonts"]["tw"] = fonts
    st50 = {}
    for f in fonts:
        for t in f["tag"] or []:
            st50[str(t)] = f"<F{t}> " + (f["desc"].split(" not ")[0].split(" - ")[0] if f["desc"] else f"font {f['nr']}")
    tw["states"]["50"] = dict(sorted(st50.items(), key=lambda kv: int(kv[0])))

    # ------------------------------------------------- dialog / control notes
    cnotes, dialog = [], None
    for r in sheet_rows(book, "Dialogs")[2:]:
        a, b, note = str(r[0]).strip(), str(r[1]).strip(), str(r[6]).strip() if len(r) > 6 else ""
        if a and not a.endswith(":"):
            dialog = a
        if not note or note in SKIP_NOTES or re.match(r"^[\d.\s\[\]]+$", note):
            continue                                  # empty, or only numbers (sizes, alternatives)
        if b.lower().startswith("force") or b == dialog:
            cnotes.append({"dialog": dialog, "control": None, "note": tr(note)})
        else:
            for c in expand(b):
                cnotes.append({"dialog": dialog, "control": c, "note": tr(note)})
    tw["control_notes"] = cnotes

    # ---------------------------------------------------------- Xbox version
    pc_items, pc_states = ifacedb.parse_names_txt(txt_pc)
    x_items, x_states = ifacedb.parse_names_txt(txt_x)
    xv = {"name": "TwoWorlds Xbox 360 (_GameInterfaceX.txt)", "present": sorted(x_items),
          "items": {}, "groups": {}, "states": {}}
    for n, name in sorted(x_items.items()):
        xv["items"][str(n)] = tw["items"][str(n)] if pc_items.get(n) == name else tr(name)
        if str(n) in groups:
            xv["groups"][str(n)] = groups[str(n)]
        if str(n) in tw["states"]:
            xv["states"][str(n)] = tw["states"][str(n)]
    tw["variants"] = [xv]
    tw["present"] = sorted(pc_items)
    tw["about"] = "TwoWorlds PC (_GameInterface.txt) and the developers' spreadsheet GameInterface.xls"

    # ------------------------------------------------------------- CustomData
    db["customdata"]["exact"].update(CUSTOMDATA)
    db["about"] = ("Names and descriptions for the InterfaceMap tools. Sources: the developers' spreadsheets "
                   "InterfaceEarth2160.xls and GameInterface.xls (TwoWorlds) and the original definition "
                   "files of Earth 2150, Earth 2160 and TwoWorlds.")

    with open(out, "w", encoding="utf-8") as f:
        json.dump(db, f, ensure_ascii=False, indent=1)
    print(f"lights {len(lights)}, face colours {len(facecolors)}, state sets {len(state_sets)}, "
          f"groups {len(groups)}, fonts {len(fonts)}, item notes {len(notes)}, control notes {len(cnotes)}, "
          f"Xbox items {len(x_items)}")


if __name__ == "__main__":
    if len(sys.argv) != 5:
        print(__doc__)
        sys.exit(1)
    main(*sys.argv[1:])

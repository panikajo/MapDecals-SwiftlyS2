#!/usr/bin/env python3
"""Static contract validation only; not a replacement for the Source 2 compiler."""
from pathlib import Path
import json
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
layout = root / "panorama/layout/custom_game/mapdecals_menu_ui.xml"
style = root / "panorama/styles/custom_game/mapdecals_menu_ui.css"
tree = ET.parse(layout)
ids = [node.attrib["id"] for node in tree.iter() if "id" in node.attrib]
assert len(ids) == len(set(ids)), "Duplicate panel IDs"
by_id = {node.attrib["id"]: node for node in tree.iter() if "id" in node.attrib}
for i in range(6):
    assert by_id[f"utils-item-{i}"].tag == "Button"
    assert f"utils-slot-{i}" in by_id
    assert any(child.attrib.get("text") == "{s:label}" for child in by_id[f"utils-item-{i}"].iter())
for control in ("utils-closeBtn", "utils-back", "utils-prev", "utils-next"):
    assert by_id[control].tag == "Button"
for panel in ("utils-title", "utils-desc", "utils-hint", "utils-page", "utils-pagination", "utils-back-anchor", "utils-dialog"):
    assert panel in by_id
assert any(node.attrib.get("src") == "s2r://panorama/styles/custom_game/mapdecals_menu_ui.vcss_c" for node in tree.iter("include"))
css = style.read_text()
for name in ("utils-dismissed", "utils-hide", "k-empty", "k-submenu", "k-toggle", "is-on"):
    assert f".{name}" in css, f"Missing CSS class: {name}"
en = json.loads((root / "src/resources/translations/en.jsonc").read_text())
ru = json.loads((root / "src/resources/translations/ru.jsonc").read_text())
assert en.keys() == ru.keys(), "Translation key mismatch"
assert "cc.command.mustbealive" in en
print(f"PASS: Panorama XML/CSS contract ({len(ids)} unique IDs); EN/RU translation keys match.")
print("Not covered: Source 2 resource compilation or in-game appearance.")

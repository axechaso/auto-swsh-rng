"""Presentation-only Chinese resources imported from axechaso/owoow.

Never use these labels as request identifiers. Unknown future entries retain their
original names, so a missing translation cannot select a different encounter.
"""
from __future__ import annotations

import json
import re
from pathlib import Path

RESOURCE_ROOT = Path(__file__).resolve().parents[1] / "resources"
BASELINE = json.loads((RESOURCE_ROOT / "upstream.json").read_text(encoding="utf-8"))
_RESOURCE = json.loads((RESOURCE_ROOT / "owoow.zh-Hans.json").read_text(encoding="utf-8"))
_MAPS = {name: {k.casefold(): v for k, v in entries.items()}
         for name, entries in _RESOURCE.items() if name not in ("schemaVersion", "source")}


def _lookup(kind: str, value: str) -> str:
    return _MAPS.get(kind, {}).get(value.casefold(), value)


def translate(value: str, kind: str) -> str:
    if not value:
        return value
    if kind == "species":
        form = _lookup("forms", value)
        if form != value:
            return form
        parts = re.fullmatch(r"(.+)-(\d+)", value)
        if parts:
            return f"{_lookup('species', parts[1])}-{parts[2]}"
    if kind == "area":
        exact = _lookup(kind, value)
        if exact != value:
            return exact
        # Replace the longest base location first, as in ChineseLocalizer.cs.
        result = value
        for key, label in sorted(_MAPS["area"].items(), key=lambda item: -len(item[0])):
            result = re.sub(re.escape(key), lambda _: label, result, flags=re.I)
        for key, label in _MAPS["areaSuffixes"].items():
            result = re.sub(re.escape(key), lambda _: label, result, flags=re.I)
        return result
    if kind == "ability":
        parts = re.fullmatch(r"(.+?) \(([12H])\)", value)
        if parts:
            return f"{_lookup(kind, parts[1])}（{parts[2]}）"
    localized = _lookup(kind, value)
    return localized if localized != value else _lookup("ui", value)


def nature_options() -> list[tuple[str, str]]:
    # Preserve the game's nature order for the existing filter control.
    names = "Hardy Lonely Brave Adamant Naughty Bold Docile Relaxed Impish Lax Timid Hasty Serious Jolly Naive Modest Mild Quiet Bashful Rash Calm Gentle Sassy Careful Quirky"
    return [(translate(name, "nature"), name) for name in names.split()]

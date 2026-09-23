"""Small versioned, atomically replaced desktop settings file."""
from __future__ import annotations

import csv
import json
import os
import tempfile
from pathlib import Path


NATURES = list(zip(
    "勤奋 怕寂寞 勇敢 固执 顽皮 大胆 坦率 悠闲 淘气 乐天 胆小 急躁 认真 爽朗 天真 内敛 慢吞吞 冷静 害羞 马虎 温和 温顺 自大 慎重 浮躁".split(),
    "Hardy Lonely Brave Adamant Naughty Bold Docile Relaxed Impish Lax Timid Hasty Serious Jolly Naive Modest Mild Quiet Bashful Rash Calm Gentle Sassy Careful Quirky".split(),
))
NATURE_ZH = {value: title for title, value in NATURES}
WEATHER_ZH = {"Normal": "晴天", "Normal Weather": "晴天", "Overcast": "阴天", "Raining": "雨天", "Thunderstorm": "雷雨", "Intense Sun": "大晴天", "Snowing": "下雪", "Snowstorm": "暴雪", "Sandstorm": "沙暴", "Heavy Fog": "大雾", "All Weather": "全天气"}
HEADERS = ["推进数", "宝可梦", "等级", "闪光", "性格", "特性", "性别", "HP", "攻击", "防御", "特攻", "特防", "速度", "证章", "气场", "身高", "EC", "PID", "Seed 0", "Seed 1"]


def seed_hex(value: str) -> str:
    value = value.strip().removeprefix("0x").removeprefix("0X")
    if not 1 <= len(value) <= 16 or any(c not in "0123456789abcdefABCDEF" for c in value):
        raise ValueError("Seed 必须是 1～16 位十六进制数，可带 0x 前缀。")
    return f"{int(value, 16):016X}"


def result_values(row: dict) -> list:
    return [row["advance"], row["species"], row["level"],
            {"None": "否", "No": "否", "Star": "星闪", "Square": "方闪"}.get(row["shiny"], row["shiny"]),
            NATURE_ZH.get(row["nature"], row["nature"]), row["ability"],
            {"Male": "♂", "Female": "♀", "Genderless": "—"}.get(row["gender"], row["gender"]),
            *row["ivs"], row["mark"], "有" if row["brilliantAura"] else "—", row["height"], row["ec"], row["pid"], row["seed0"], row["seed1"]]


def export_csv(path: Path, rows: list[dict]):
    with path.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(HEADERS)
        for row in rows:
            # Spreadsheet applications otherwise interpret leading '=' as a formula.
            writer.writerow(["'" + v if isinstance(v, str) and v.startswith(("=", "+", "-", "@")) else v for v in result_values(row)])


class SettingsStore:
    def __init__(self, directory: Path):
        self.path = directory / "settings.json"

    def load(self) -> dict:
        if not self.path.exists():
            return {"version": 1, "profiles": {}}
        data = json.loads(self.path.read_text(encoding="utf-8"))
        if not isinstance(data, dict) or data.get("version") != 1 or not isinstance(data.get("profiles"), dict):
            raise ValueError("存档配置格式不受支持。原文件保留，请检查 settings.json。")
        for key in ("backend", "port", "camera", "ocrPython", "ocrCache", "labelRoot"):
            if key in data and not isinstance(data[key], str):
                raise ValueError(f"配置字段 {key} 必须是文本。原文件保留。")
        if "ocrRegions" in data and not isinstance(data["ocrRegions"], dict):
            raise ValueError("OCR 区域配置格式错误。原文件保留。")
        if "ocrThreshold" in data and (type(data["ocrThreshold"]) is not int or not 0 <= data["ocrThreshold"] <= 100):
            raise ValueError("OCR 置信度必须在 0～100 之间。原文件保留。")
        for name, profile in data["profiles"].items():
            if not name.strip() or not isinstance(profile, dict) or profile.get("game") not in ("Sword", "Shield"):
                raise ValueError("存档配置无效。原文件保留。")
            for key in ("tid", "sid"):
                if type(profile.get(key)) is not int or not 0 <= profile[key] <= 65535:
                    raise ValueError("存档中的 TID / SID 必须在 0～65535 之间。原文件保留。")
            for key in ("shinyCharm", "markCharm"):
                if type(profile.get(key)) is not bool:
                    raise ValueError("存档中的护符设置无效。原文件保留。")
        return data

    def save(self, data: dict):
        self.path.parent.mkdir(parents=True, exist_ok=True)
        fd, temporary = tempfile.mkstemp(dir=self.path.parent, prefix="settings-", suffix=".tmp")
        try:
            with os.fdopen(fd, "w", encoding="utf-8") as handle:
                json.dump(data, handle, ensure_ascii=False, indent=2)
                handle.flush()
                os.fsync(handle.fileno())
            os.replace(temporary, self.path)
        finally:
            Path(temporary).unlink(missing_ok=True)

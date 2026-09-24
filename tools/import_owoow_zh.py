"""Import presentation resources from a pinned, clean Chinese owoow checkout.

No RNG code or encounter table is copied. Run explicitly when updating translations.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE_FILE = "owoow.WinForms/Localization/ChineseLocalizer.cs"
RESOURCE = ROOT / "desktop/resources/owoow.zh-Hans.json"
LOCK = ROOT / "desktop/resources/upstream.json"
STRING = r'"(?:[^"\\]|\\.)*"'


def git(repo: Path, *args: str) -> str:
    return subprocess.check_output(["git", "-C", str(repo), *args], encoding="utf-8").strip()


def dictionary(source: str, name: str) -> dict:
    block = re.search(rf'\b{name}\s*=\s*new\(StringComparer.OrdinalIgnoreCase\)\s*\{{(.*?)\n    \}};', source, re.S)
    if not block:
        raise ValueError(f"汉化源码结构已变更，需检查导入器：{name}")
    pair = rf'\[({STRING})\]\s*=\s*({STRING})\s*,?'
    values = {json.loads(a): json.loads(b) for a, b in re.findall(pair, block[1])}
    if re.sub(pair, "", block[1]).strip() or not values:
        raise ValueError(f"不支持的汉化条目，需检查导入器：{name}")
    return values


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path, help="汉化 owoow 仓库路径")
    parser.add_argument("--commit", required=True, help="经过审核的 40 位汉化提交 SHA")
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    repo = args.source.resolve()
    if not re.fullmatch(r"[0-9a-f]{40}", args.commit) or git(repo, "rev-parse", "HEAD") != args.commit:
        raise ValueError("汉化仓库 HEAD 与指定提交不一致。")
    paths = [SOURCE_FILE, "owoow.Core/owoow.Core.csproj", "owoow.Core/Resources/Personal/personal.json"]
    if git(repo, "status", "--porcelain", "--", *paths):
        raise ValueError("汉化资源存在未提交修改，请先审核并提交。")
    # Hash Git's canonical blob, independent of checkout CRLF settings.
    source_bytes = subprocess.check_output(["git", "-C", str(repo), "show", f"{args.commit}:{SOURCE_FILE}"])
    source = source_bytes.decode("utf-8-sig")
    version = next(p.attrib["Version"] for p in ET.parse(repo / paths[1]).iter("PackageReference") if p.attrib["Include"] == "PKHeX.Core")
    with tempfile.TemporaryDirectory(prefix="owoow-zh-") as tmp:
        output = Path(tmp) / "strings.json"
        subprocess.run([args.dotnet, "run", "--project", str(ROOT / "tools/ExportGameStrings"),
                        f"-p:PKHeXVersion={version}", "--", str(output)], check=True)
        values = json.loads(output.read_text(encoding="utf-8-sig"))
    names = values.pop("speciesById")
    for key, value in json.loads((repo / paths[2]).read_text(encoding="utf-8-sig")).items():
        if not re.search(r"-\d+$", key):
            values["species"][key] = names[value["DevId"]]
    values["forms"] = dictionary(source, "FormSpeciesTranslations")
    values["area"] = dictionary(source, "AreaTranslations")
    values["ui"] = dictionary(source, "UiTranslations")
    area_function = source.split("private static string TranslateArea(string value)", 1)[1].split("private static void AddGameStrings", 1)[0]
    suffixes = re.findall(rf'\.Replace\(({STRING}),\s*({STRING}),\s*StringComparison.OrdinalIgnoreCase\)', area_function)
    if not suffixes:
        raise ValueError("地区后缀翻译结构已变更，需检查导入器。")
    values["areaSuffixes"] = {json.loads(a): json.loads(b) for a, b in suffixes}
    source_hash = hashlib.sha256(source_bytes).hexdigest()
    data = {"schemaVersion": 1, "source": {"repository": "https://github.com/axechaso/owoow",
            "commit": args.commit, "path": SOURCE_FILE, "sha256": source_hash, "pkhexVersion": version}, **values}
    encoded = (json.dumps(data, ensure_ascii=False, indent=2, sort_keys=True) + "\n").encode("utf-8")
    RESOURCE.parent.mkdir(parents=True, exist_ok=True)
    RESOURCE.write_bytes(encoded)
    lock = json.loads(LOCK.read_text(encoding="utf-8"))
    lock["localization"] = {**data["source"], "resourceSha256": hashlib.sha256(encoded).hexdigest()}
    LOCK.write_text(json.dumps(lock, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"已导入汉化资源 {args.commit[:12]} / PKHeX {version}：{RESOURCE}")


if __name__ == "__main__":
    main()

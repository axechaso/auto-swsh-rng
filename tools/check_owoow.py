"""Read-only release gate for the pinned owoow core and Chinese resources."""
from __future__ import annotations

import hashlib
import json
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def git(repo: Path, *args: str) -> str:
    return subprocess.check_output(["git", "-C", str(repo), *args], encoding="utf-8").strip()


def validate(root: Path = ROOT):
    resources = root / "desktop/resources"
    lock = json.loads((resources / "upstream.json").read_text(encoding="utf-8"))
    if lock["schemaVersion"] != 1 or lock["protocolVersion"] != 1:
        raise ValueError("不支持的版本清单，请检查桌面桥接协议。")
    expected = lock["algorithm"]["commit"]
    if not re.fullmatch(r"[0-9a-f]{40}", expected):
        raise ValueError("owoow 算法必须固定为完整提交 SHA。")
    entry = git(root, "ls-files", "--stage", "third_party/owoow").split()
    if len(entry) != 4 or entry[:3] != ["160000", expected, "0"]:
        raise ValueError("owoow 的 Git 子模块版本与版本清单不同，请按 docs/owoow-updates.md 审核升级。")
    core = root / "third_party/owoow"
    if git(core, "rev-parse", "HEAD") != expected:
        raise ValueError("owoow 工作目录不是固定版本，请执行 git submodule update --init third_party/owoow。")
    if git(core, "status", "--porcelain", "--untracked-files=normal"):
        raise ValueError("owoow 子模块有本地修改，请先审核，不能作为已验证版本发布。")
    data = (resources / "owoow.zh-Hans.json").read_bytes()
    localized = json.loads(data)
    info = lock["localization"]
    if hashlib.sha256(data).hexdigest() != info["resourceSha256"] or localized["source"] != {k: v for k, v in info.items() if k != "resourceSha256"}:
        raise ValueError("汉化资源与来源清单不一致，请重新运行 tools/import_owoow_zh.py。")
    if localized["schemaVersion"] != 1 or not re.fullmatch(r"[0-9a-f]{40}", info["commit"]):
        raise ValueError("汉化资源版本无效。")
    print(f"owoow {expected[:12]}；汉化 {info['commit'][:12]}；协议 {lock['protocolVersion']}：校验通过")


if __name__ == "__main__":
    try:
        validate()
    except (KeyError, ValueError, OSError, subprocess.CalledProcessError) as exc:
        raise SystemExit(f"owoow 校验失败：{exc}")

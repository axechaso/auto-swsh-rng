"""Only used by manual Actions runs; never publishes an unreviewed core upgrade."""
import json
import os
import re
import subprocess
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[1]
commit = sys.argv[1]
if os.environ.get("GITHUB_ACTIONS") != "true" or not re.fullmatch(r"[0-9a-f]{40}", commit):
    raise SystemExit("仅可在手动 Actions 试跑中使用，必须提供官方 owoow 完整提交 SHA。")
core = root / "third_party/owoow"
subprocess.run(["git", "-C", str(core), "fetch", "https://github.com/LegoFigure11/owoow.git", commit], check=True)
subprocess.run(["git", "-C", str(core), "checkout", "--detach", commit], check=True)
subprocess.run(["git", "-C", str(root), "add", "third_party/owoow"], check=True)
path = root / "desktop/resources/upstream.json"
lock = json.loads(path.read_text(encoding="utf-8"))
lock["algorithm"]["commit"] = commit
path.write_text(json.dumps(lock, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(f"仅测试 owoow {commit}；保留原固定种子结果和汉化资源进行兼容性检查；不上传发布包。")

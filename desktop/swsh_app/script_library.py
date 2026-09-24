"""Parameter forms and preflight for user-supplied EasyCon scripts."""
from dataclasses import dataclass
from pathlib import Path
import re

from .vendor.easycon import EasyConScriptEngine
from .vendor.easycon.image_labels import load_image_labels
from .script_reachability import reachable_metadata
from . import unified_script

SCRIPT_DIR = Path(__file__).resolve().parents[1] / "scripts"
PARAMETER = re.compile(r"^(\s*)(_\w+)(\s*=\s*)([+-]?\d+|填入这里)(\s*(?:#.*)?)$", re.UNICODE)


@dataclass(frozen=True)
class Parameter:
    line: int
    name: str
    default: str
    description: str


def parameters(text):
    result = []
    for index, line in enumerate(text.splitlines()):
        match = PARAMETER.fullmatch(line)
        if match:
            result.append(Parameter(index, match[2], "" if match[4] == "填入这里" else match[4], match[5].strip().lstrip("#").strip()))
    return result


def apply_parameters(text, values):
    lines = text.splitlines()
    effective = {}
    for parameter in parameters(text):
        value = str(values.get(parameter.name, parameter.default)).strip()
        if not re.fullmatch(r"[+-]?\d+", value):
            raise ValueError(f"请填写「{parameter.name[1:]}」的整数值。")
        number = int(value)
        if abs(number) > 2_147_483_647:
            raise ValueError(f"「{parameter.name[1:]}」超出整数范围。")
        if parameter.default == "" and number <= 0:
            raise ValueError(f"「{parameter.name[1:]}」必须大于 0。")
        if parameter.name.endswith("月") and not 1 <= number <= 12:
            raise ValueError(f"「{parameter.name[1:]}」必须在 1～12 之间。")
        if parameter.name.endswith("日") and not 1 <= number <= 31:
            raise ValueError(f"「{parameter.name[1:]}」必须在 1～31 之间。")
        if parameter.name == "_迷人身躯" and not 1 <= number <= 6:
            raise ValueError("迷人身躯的位置必须在 1～6 之间。")
        if parameter.name == "_撞帧方向" and not 0 <= number <= 4:
            raise ValueError("撞帧方向必须在 0～4 之间（0 为不撞帧）。")
        if parameter.name in ("_需要保存", "_需要预留") and number not in (0, 1):
            raise ValueError(f"「{parameter.name[1:]}」只能为 0 或 1。")
        if parameter.name in ("_判定", "_延迟", "_延迟2", "_等待判定时间") and number <= 0:
            raise ValueError(f"「{parameter.name[1:]}」必须大于 0。")
        if parameter.name in ("_预留帧数", "_雷雨雨天误差") and number < 0:
            raise ValueError(f"「{parameter.name[1:]}」不能为负数。")
        effective[parameter.name] = number
        match = PARAMETER.fullmatch(lines[parameter.line])
        lines[parameter.line] = f"{match[1]}{match[2]}{match[3]}{number}{match[5]}"
    if "_过几帧" in effective:
        frames = effective["_过几帧"]
        if effective.get("_需要预留") == 1:
            frames -= effective.get("_预留帧数", 0)
        if effective.get("_雷雨雨天乱数") == 1:
            frames -= effective.get("_雷雨雨天误差", 0)
        if frames < 0:
            raise ValueError("扣除预留帧和天气误差后帧数为负，请调整参数。")
    if unified_script.is_unified(text):
        unified_script.validate(effective)
    return "\n".join(lines) + "\n"


def inspect_script(text, path=None, label_root=None):
    if unified_script.is_unified(text):
        text = apply_parameters(text, {})
    source = str(path or "未命名.ecs")
    program = EasyConScriptEngine().compile(text, source=source, script_dir=Path(path).parent if path else None)
    if unified_script.is_unified(text):
        program = reachable_metadata(program)
    roots = [Path(path).parent] if path else []
    if label_root:
        root = Path(label_root)
        roots.insert(0, root.parent if root.name.casefold() == "imglabel" else root)
    labels = load_image_labels(roots) if program.requires_image_search else None
    missing = sorted(program.external_labels.difference(labels.labels if labels else {}))
    return program, labels, missing


def adapted_source(path):
    return Path(path).read_text(encoding="utf-8-sig"), ""

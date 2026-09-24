"""Parameter descriptions for the bundled single-file Sword/Shield workflow."""

HEADER = "# 剑盾乱数统合脚本 · ECS v1"
NAME = "剑盾乱数统合.ecs"
MODES = ["使用说明", "测种", "启动后测种", "卡对战 bug", "光速过帧", "原地过帧",
         "自行车", "雷雨自行车", "精准过帧", "迷人身躯换位"]
OPTIONS = {
    "_功能": [(f"{i} · {title}", str(i)) for i, title in enumerate(MODES)],
    "_推进后测种": [("测种", "1"), ("停在能力页", "0")],
    "_撞帧方向": [(title, str(i)) for i, title in enumerate(["不撞帧", "向前", "向后", "向左", "向右"])],
    "_需要保存": [("保存", "1"), ("不保存", "0")],
    "_需要预留": [("不预留", "0"), ("预留", "1")],
    "_雷雨雨天乱数": [("关闭", "0"), ("启用", "1")],
}


def is_unified(text):
    return text.lstrip("\ufeff\r\n ").startswith(HEADER)


def active_parameters(values):
    mode = int(values.get("_功能", 0))
    result = {"_功能"}
    if mode in (1, 2, 3, 4, 5, 6, 7):
        result.add("_识别次数上限")
    if mode in (4, 5, 8):
        result.add("_过帧数")
    if mode in (4, 5, 6, 7):
        result.add("_推进后测种")
    if mode in (1, 2) or (mode in (4, 5, 6, 7) and int(values.get("_推进后测种", 1)) == 1):
        result.add("_测种判定")
    if mode == 4:
        result.update(["_光速延迟", "_光速按键基准", "_光速判定间隔", "_光速判定等待"])
    if mode == 5:
        result.update(["_原地移动延迟", "_原地上移延迟"])
    if mode in (6, 7):
        result.update(["_自行车次数", "_同步月", "_同步日", "_当前月", "_当前日"])
    if mode == 7:
        result.update(["_雷雨月", "_雷雨日"])
    if mode == 8:
        result.update(["_撞帧方向", "_需要保存", "_需要预留", "_雷雨雨天乱数"])
        if int(values.get("_需要预留", 0)) == 1:
            result.add("_预留帧数")
        if int(values.get("_雷雨雨天乱数", 0)) == 1:
            result.update(["_雷雨雨天误差", "_当前月", "_当前日", "_雷雨月", "_雷雨日"])
    if mode == 9:
        result.add("_迷人身躯")
    return result


def validate(values):
    mode = values["_功能"]
    if not 0 <= mode <= 9:
        raise ValueError("功能必须在0～9之间。")
    if mode == 0:
        return
    active = active_parameters(values)
    ranges = {"_过帧数": (1, 100000000), "_自行车次数": (1, 1000000),
              "_识别次数上限": (1, 3600), "_测种判定": (0, 100)}
    for name in ("_光速延迟", "_光速按键基准", "_光速判定等待", "_原地移动延迟", "_原地上移延迟"):
        ranges[name] = (1, 60000)
    ranges["_光速判定间隔"] = (1, 1000000)
    ranges["_预留帧数"] = (0, 100000000)
    ranges["_雷雨雨天误差"] = (0, 100000000)
    for name, (lower, upper) in ranges.items():
        if name in active and not lower <= values[name] <= upper:
            raise ValueError(f"「{name[1:]}」必须在{lower}～{upper}之间。")
    for name in OPTIONS.keys() - {"_功能", "_撞帧方向"}:
        if name in active and values[name] not in (0, 1):
            raise ValueError(f"「{name[1:]}」只能为0或1。")
    if mode == 8 and values["_过帧数"] - values["_需要预留"] * values["_预留帧数"] - values["_雷雨雨天乱数"] * values["_雷雨雨天误差"] < 0:
        raise ValueError("扣除预留帧和天气误差后帧数为负，请调整参数。")

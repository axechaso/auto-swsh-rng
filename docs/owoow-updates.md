# owoow 与汉化资源的更新

PySide6 使用 `AutoSwshRng.Cli desktop-json` 调用现有 owoow 适配层。算法只存在于固定版本的 owoow 子模块；中文展示资源独立导入，界面选项的原始键值、种子、PID、EC 和 IV 不受翻译影响。

## 当前基线

`desktop/resources/upstream.json` 是版本清单：

- 算法：官方 `LegoFigure11/owoow`，提交 `a8514d654ae6c7c4ad87c3647c9911a05f6ef42a`。
- 汉化：`axechaso/owoow`，提交 `c2e48d94b94e34837a3946c22be58a45b07d3319`，使用其 PKHeX.Core 26.5.5 简体中文字符串。
- JSON 协议：1。服务端嵌入编译时的算法验证基线，结果返回 `protocolVersion`、`algorithmCommit` 和 `data`；Qt 拒绝不匹配的协议或算法基线。独立更新翻译不要求重编译算法。

共通设置显示这些版本。**“验证基线”来自编译清单，不是运行时对整个二进制的认证**；源码构建和发布前必须运行 `python tools/check_owoow.py`，防止清单与实际子模块不同。启动脚本和 Actions 已执行此检查。直接 `dotnet build` 的开发者也应先执行它。

平时使用 `git submodule update --init --recursive` 还原仓库记录的版本，不要用 `--remote` 自动追随上游。

## 试跑新版算法

在 GitHub 的 **PySide6 Desktop → Run workflow** 中填入官方 owoow 的完整 40 位 `owoow_commit`。留空则验证当前版本并构建预览包。

指定候选提交时，Actions 只在临时检出中切换 owoow、更新临时清单；保留当前汉化资源和固定种子预期值，执行构建、适配层测试、CLI 回归和桌面测试。候选试跑不提交代码，也不上传发布包。

测试失败需要检查 API 改动、遭遇表更新或 RNG 行为变化。成功也不等于已经升级；正式更新需要一次可审核的提交：

1. 开功能分支，在 owoow 子模块检出审核过的官方完整 SHA。
2. 一并更新 `desktop/resources/upstream.json` 的 `algorithm.commit`，暂存 `third_party/owoow` 指针，再运行版本检查。
3. 调整 `AutoSwshRng.Upstream` 适配层。协议字段含义发生不兼容变化时，同时提升清单、CLI 和 Qt 的协议版本。
4. 运行 Actions 中的 CLI、上游适配层和 Python 测试。固定种子样本在 `tests/AutoSwshRng.Cli.Tests/Fixtures/owoow-baseline.json`，覆盖剑／盾四类遭遇及正反向推进，包含完整返回结果。它是旧版本的独立预期值，不能为了测试通过直接批量重录；确认上游变更正确后，才更新相关样本，并在 PR 说明结果差异及样本来源 SHA。
5. 如出现新增英文地点／宝可梦，覆盖测试会失败，需要更新下面的汉化资源。完成检查后提交、推送并审查 Actions 结果。

这些样本用于发现回归，不是穷尽所有种子、天气和游戏条件的数学证明。

## 更新汉化资源

在独立目录检出所需的 **owoow 汉化版**，确认资源文件没有未提交修改，执行：

```powershell
python tools/import_owoow_zh.py D:/path/to/owoow-zh --commit <审核过的40位提交SHA>
python tools/check_owoow.py
python -m unittest discover -s desktop/tests -p test_upstream_resources.py -v
```

需要 .NET 10 SDK；可通过 `--dotnet D:/path/to/dotnet.exe` 指定。导入器读取汉化源码中的地区、形态、天气等词典，并从其 `owoow.Core.csproj` 读取 PKHeX 版本来导出官方简体中文游戏名称。导入器不会执行汉化版的界面，也不会改动 RNG 算法。

提交生成的 `desktop/resources/owoow.zh-Hans.json` 和 `upstream.json`；资源来源说明随版本更新。普通使用和构建不需要联网重新下载翻译，也不需要在相邻目录放汉化仓库。

运行时缺少翻译会保留原名，避免错误匹配；发布测试则检查当前剑／盾八张遭遇表中的地区、天气、宝可梦名称，提醒维护者补齐。Qt 下拉框显示中文，传给计算服务的仍是英文键；结果表、详情、剪贴板和 CSV 使用同一套汉化文本。

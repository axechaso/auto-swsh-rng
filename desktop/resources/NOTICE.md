# owoow 简体中文资源

来源：[axechaso/owoow 汉化提交 c2e48d94b94e34837a3946c22be58a45b07d3319](https://github.com/axechaso/owoow/tree/c2e48d94b94e34837a3946c22be58a45b07d3319)。
地区、形态、天气和界面术语来自 `owoow.WinForms/Localization/ChineseLocalizer.cs`。
宝可梦、特性、性格、证章、招式、道具和属性名称使用该汉化版依赖的 **PKHeX.Core 26.5.5** 的 `zh-Hans` 游戏字符串，以英文数组索引配对。

原项目：[LegoFigure11/owoow](https://github.com/LegoFigure11/owoow)；游戏字符串：[kwsch/PKHeX](https://github.com/kwsch/PKHeX)。遵循 GPL-3.0-or-later，完整许可证随桌面目录提供。

`owoow.zh-Hans.json` 由 `tools/import_owoow_zh.py` 生成；`upstream.json` 记录算法、汉化提交及文件校验值。导入器按 PKHeX 的 `标识符<TAB>名称` 格式拆分证章，并为 owoow 返回的证章标识添加别名。
只复用展示文本，未复制或修改遭遇计算代码。更新方式见仓库 `docs/owoow-updates.md`。

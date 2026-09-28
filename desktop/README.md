# 剑 / 盾乱数工具 · PySide6 初版

参考本地火叶工具的侧栏、参数卡片和手柄布局，使用 Qt 原生桌面控件。
乱数计算复用现有 owoow 服务；伊机控及 OCR 接入参考 [auto-bdsp-rng](https://github.com/XiaoyuBook/auto-bdsp-rng/tree/eb1e65226fe678dd6e30f48e5d5fbbdeec566ba8)。

## 启动

要求 Windows x64、Python 3.12、.NET 10 SDK（构建）及 Desktop Runtime（运行）。
在仓库根目录执行：

```powershell
git submodule update --init --recursive
python -m pip install -r desktop/requirements.txt
powershell -ExecutionPolicy Bypass -File desktop/启动剑盾乱数.ps1
```

启动脚本把构建产物和缓存放在 `D:\CodexTools\auto-swsh-rng`；其他机器可用 `-ToolRoot` 指定目录。
已有构建可以加 `-SkipBuild`。也可以直接运行 `python desktop/run_pyside6_gui.py`，在共通设置中选择计算服务。
Actions 下载包附带 `desktop/backend` 时会自动找到服务。
仅查看界面使用 `python desktop/run_pyside6_gui.py --no-backend`。

## 乱数与采集

- 明雷、固定 / 静态、暗雷、垂钓，版本 / 区域 / 天气 / 宝可梦联动。
- 两段 64 位十六进制 Seed、范围、闪光、性格、性别、证章及六项 IV 筛选。
- 击败数量、暗雷步数、内部 TID / SID、闪耀护符和证章护符。
- 异步搜索、取消、排序、详情、复制及 UTF-8 BOM CSV 导出。
- Xoroshiro128+ 前进 / 回退、多存档和 Qt 采集卡视频预览。
- 使用 owoow 汉化版的地区、天气、宝可梦／形态、性格、特性和证章名称，结果复制及 CSV 同步汉化。

先在「存档管理」填写内部 16 位 TID / SID（0～65535），再输入种子。
示例种子仅用于体验。单次搜索最多 100,000 帧，显示 / 导出前 10,000 个匹配。
首发特性、图鉴推荐、额外推进配置、动画反查种子和全自动命中尚未实现。

## 伊机控

「伊机控」刷新串口并连接。使用参考项目的纯 Python Native EasyCon 实现、pyserial 握手和原生 Switch 报告协议，不需要启动伊机控 WinForms 窗口。
手动输入和 ECS 脚本共享同一设备连接；运行脚本时禁用手动输入。

手柄支持鼠标按住 / 松开、键盘组合键、方向键、左右摇杆及摇杆按下。
键盘只在手柄获得焦点时生效；切换页面、失焦或退出窗口时释放按键。
左摇杆 WASD、右摇杆 IJKL、十字键方向键；其余映射显示在每个按键下方。
串口写入由设备报告线程执行，Qt 主线程不等待串口。

「脚本库 / 执行」默认载入单文件「剑盾乱数统合.ecs」，将用户提供的 9 份旧脚本重写成函数，按下拉框选择功能，并只显示相关参数。支持参数输入、检查、编辑、另存为和取消；原稿与旧单项适配版保留用于对照。
见 [脚本改动与所需标签](scripts/README.md)。标签由用户后续补充；未找到依赖模板时不会执行脚本按键。
`.IL` 读取共享采集原帧，支持 EasyCon 图像匹配及 TesserDetect；未挪用 BDSP 的游戏脚本或坐标。
停止、异常或退出时会发送中立手柄报告；硬件断线时会显示未连接。

## OCR

参考项目使用 PaddleOCR 3、PaddlePaddle 3.2、PP-OCRv5 mobile 检测 / 识别模型。
建议装到独立的 Python 3.12 环境，再在「OCR 识别」填写该环境的 Python 路径：

```powershell
python -m venv D:\CodexTools\auto-swsh-rng\ocr-venv
D:\CodexTools\auto-swsh-rng\ocr-venv\Scripts\python.exe -m pip install -r desktop/requirements-ocr.txt
```

点击采集取帧或打开截图，选择字段并拖动框选区域，然后识别当前区域或全部已配置区域。
显示原始文本、置信度和待核对状态。区域按原图比例保存，预览缩放或留黑边不会改变实际裁切。
OCR 使用独立常驻进程，CPU 2 线程；可以停止，不阻塞主界面。
首次需要下载模型，之后使用所选缓存目录；也可预置 `official_models/PP-OCRv5_mobile_det` 和 `PP-OCRv5_mobile_rec`。
PaddleOCR 与旧 `.IL` 的 Tesseract 兼容层是两个独立识别入口。

配置保存在 Qt `AppLocalDataLocation/settings.json`；用 `--data-dir` 可隔离。
仅点击对应保存按钮才写配置，原文件损坏时禁止覆盖。

## 验证

```powershell
python -m unittest discover -s desktop/tests -v
dotnet test tests/AutoSwshRng.Cli.Tests/AutoSwshRng.Cli.Tests.csproj
python desktop/run_pyside6_gui.py --page easycon --screenshot desktop/artifacts/controller.png
```

测试覆盖计算服务对照、64 位种子往返、Qt 搜索 / 取消 / 排序、存档、手柄协议、组合输入和释放、脚本模拟、共享帧、ROI 坐标及 OCR 结果解析。
本机已用真实 PaddleOCR 模型识别测试图像；串口、采集卡及游戏流程仍需实机验证。

## owoow 更新

算法子模块与汉化资源分别固定提交；共通设置显示基线版本。构建前检查子模块及资源校验值，前后端检查协议与算法版本。
Actions 运行固定种子结果回归、适配层测试及汉化覆盖检查，也支持手动填入候选 owoow SHA 试跑，候选试跑不会发布。
具体升级和导入汉化版资源的方法见 [更新说明](../docs/owoow-updates.md)。

## 来源与许可

桌面适配代码遵循 GPL-3.0-or-later，完整许可见 `LICENSE.txt`。
Native EasyCon 代码来源和精确版本见 `swsh_app/vendor/easycon/NOTICE.md`。
手柄几何布局参考同一工作区的火叶项目 `pyside_app/controller_layout.py`。
OCR 原生资源的来源与许可见 `easycon_native/NOTICE.md`。
附带脚本保留用户提供原稿中的作者署名。
owoow 汉化版与 PKHeX 游戏文本的精确来源见 [资源说明](resources/NOTICE.md)。

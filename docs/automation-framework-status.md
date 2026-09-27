# 自动闭环框架实现状态

本页记录 [实现方案](剑盾全自动乱数闭环实现方案.md) 在当前工作区的代码状态。模拟和合成数据只验框架行为，不代表真实设备或游戏场景已经通过验证。

## 已完成的框架部分

| 范围 | 当前实现 | 证据 / 检查 |
| --- | --- | --- |
| M0 种子边界 | 固定 128 位协议、观测前后状态、独立验证、完整重定位和结构化待验报告 | `RetailSeedObservationTests`、CLI JSON 协议测试 |
| M0 NPC 探针 | `calibration.probe.evaluate` 按候选参数逐步调用原校准模型，比较预测末状态 / 推进数，返回 0 / 1 / 多候选诊断 | CLI JSON 协议测试；报告保持 `PendingHardwareValidation` |
| M0 反查工具 | `encounter.reverse` 用实际观测约束、有界窗口和完整遭遇上下文枚举候选 h；模拟来源不能进入校准资格 | CLI JSON 协议测试 |
| M1 帧输入 | `FrameStore` 提供带帧号、单调时间、源时间、像素哈希和证据引用的不可变快照 | `test_devices_ocr.py`、`test_replay_evidence.py` |
| M1 回放 / 模拟 | 带哈希校验的图片 / 视频 manifest、虚拟时钟、同一 `SeedObserver`、无硬件 `SimulationDevicePort` 和可注入停帧 / 丢帧故障 | `test_replay_evidence.py` |
| M1 场景守卫 | 连续新帧、置信度、时间和 `contextRevision` 检查；旧帧、旧上下文和未知页面不能通过 | `test_replay_evidence.py` |
| M2 搜索 | `encounter.search` 使用扫描游标和候选游标；结果带稳定快照 ID、请求摘要和排序版本 | CLI JSON 协议测试 |
| M2 runner | 所有候选页读完后才确认空结果或前往下一扫描区间；同一快照不完整、游标倒退或响应身份不符时不重启 | `test_automation_runner.py` 的 T39 案例 |
| M3 NPC 校准核心 | 候选交集、等价候选区分实验、独立状态复验、全场景 / 算法指纹缓存及实验推进账本 | `test_npc_calibration.py`；合成协议端只验证框架逻辑 |
| M4 规划 / 执行核心 | `attempt.plan` 逐候选求解状态相关方程；执行协调器一次执行一个粗推进批次，随后用新动画序列 `seed.locate`，重算计划后再精推 / 触发；复用注入的 ECS 阶段脚本端口 | CLI 协议测试、`test_attempt_planner.py`、`test_attempt_execution.py`、`attempt-execution-report.schema.json`；合成 fake action port 覆盖批次、歧义、失败、过期快照；场景脚本配置和 M2 runner 集成待做 |
| M5 捕获 / 反查判定核心 | 捕获状态转移、投球上限、提示消解、新实体定位、多帧字段三值观测、实际约束反查请求、目标候选验证和结构化尝试报告 | `test_capture_workflow.py`、`capture-workflow-report.schema.json`、CLI 反查协议测试；未接入回放战斗模拟器和设备动作协调器 |
| 证据清单 | 原子写入、路径与 SHA-256 检查、算法 / 脚本 / 设备配置指纹、报告、旧版本失效和真实证据检查 | `test_replay_evidence.py` |
| 正式入口门禁 | runner 默认模拟模式；正式模式要求 `FrameworkReady + HardwareValidated`，证据报告还要求所需实机素材有效 | `test_automation_runner.py`、`test_replay_evidence.py` |
| M6 保存提交基础 | 原子保存 `SuccessDetected/SaveRequested/SaveConfirmed/Completed`；保存请求崩溃恢复禁止普通重启或重复保存 | `test_automation_workflow.py` 的 T40 |
| M6 边界偏移基础 | `b` 按命名边界和模型修订一次性应用；残差更新有界且符号由 T41 固定 | `test_automation_workflow.py` 的 T41 |

## 当前仍未就绪

- 全局 `FrameworkReady` 尚未成立：M3 尚未连接实际探测动作 / 采集流程；M4 执行协调器仍未接入 M2 总 runner 和回放动作模拟，具体场景阶段脚本也未验证；M5 的 Python 请求已与 CLI 做合成端到端联调，但仍未接入回放战斗模拟器和统一任务协调器；M6 完整偏差归因与成功状态机、M7 产品验收及第 23.1 节其余门槛仍需实现。T40、T41 的离线基础模型和测试已具备。
- 当前桌面尚未把新的 runner、模拟器、证据浏览和场景守卫接入单一完整 UI 工作流。
- 没有真实设备适配器、真实动作链、实机动画识别校准、捕获身份确认或保存确认。每个场景保持 `PendingHardwareValidation`；任何模拟报告都不能改变这一状态。
- C# 诊断调用里的 `sourceKind` 默认是 `unknown`。调用方必须给出素材来源；报告只记录证据引用，不代替证据清单对文件、设备配置和版本进行审核。

因此，当前工作区已实现 M0-F、M1-F、M2-F，并补入 M3 / M4 / M5 / M6 的部分离线基础模块，但尚未达到方案第 23.1 节的整体 `FrameworkReady` 验收，更没有达到任何实机硬门槛。

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
| M2 runner | 所有候选页读完后才确认空结果或前往下一扫描区间；最终结果包含跨扫描窗口的组合快照 ID / 摘要及完整覆盖范围；支持由上层保留任务租约 | `test_automation_runner.py` 的 T39 与外部租约案例 |
| M3 NPC 校准核心 | 候选交集、等价候选区分实验、独立状态复验、全场景 / 算法指纹缓存及实验推进账本 | `test_npc_calibration.py`；合成协议端只验证框架逻辑 |
| M4 规划 / 执行核心 | `attempt.plan` 逐候选求解状态相关方程；M2 完整搜索结果绑定为不可变目标快照，超过 512 个候选时全部分批规划后才选择；M2→注入的离线校准提供器→M4 共用单一任务租约；M4 之后可在释放租约前注入 M5 捕获阶段 | CLI 协议测试、`test_attempt_planner.py`、`test_attempt_execution.py`、`test_attempt_workflow.py`；覆盖跨页规划、同租约运行至 M5 阶段、正式门禁、歧义停止、回放粗推 / 重定位 / 触发、动作失败和过期快照；具体场景脚本与真实 NPC 探测端仍待实现 |
| M5 捕获 / 反查判定核心 | 回放战斗事件驱动捕获状态机；处理失败续投、逃脱 / 异常终止、捕获后提示、新实体队伍差异、按帧引用的 reviewed field reads、仅用实际字段构造反查、用户范围内全候选目标验证；成功后写入 T40 并以保存后身份回执结束 | `test_capture_attempt.py`、`test_capture_workflow.py`、`capture-attempt-report.schema.json`、CLI 反查协议测试；回放采用已标注的事件 / 字段 sidecar，不代表已实现画面分类或 OCR |
| 证据清单 | 原子写入、路径与 SHA-256 检查、算法 / 脚本 / 设备配置指纹、报告、旧版本失效和真实证据检查 | `test_replay_evidence.py` |
| 正式入口门禁 | runner 默认模拟模式；正式模式要求 `FrameworkReady + HardwareValidated`，证据报告还要求所需实机素材有效 | `test_automation_runner.py`、`test_replay_evidence.py` |
| M6 反馈闭环核心 | 按顺序归因证据 / 反查 / 检查点 / NPC 场景 / 个体与生成边界；唯一可信真实样本才形成 `e=h-g`；原子归档上下文、拒绝原因、模型版本；残差中位数更新 `b`、超界停止、恶化 / 振荡回退；毫秒参数需同控变量多组稳定斜率后单独提出；明确未命中必须先持久证据和校准决定、再授权重启并以独立测种创建新 epoch；未决目标和 T40 任一未完成阶段均禁止普通重启 | `test_feedback.py`、`test_automation_workflow.py` 的 T40 / T41、`feedback-archive.schema.json`；只验证离线逻辑 |
| M7 桌面证据 / 模拟入口（部分） | 默认“自动流程”工作页；创建 / 读取场景清单；证据导入、SHA-256 核验和缺少当前版本 / 真实素材报告；安全校验回放包并逐帧浏览；单轮合成测种通过 `SeedObserver`、M2 runner 和协议 2 CLI 完成求种、预测验证位复观及完整范围搜索；可注入预检失败 / 测种首帧丢失并检查安全停止；可导出含算法 / 脚本指纹、来源、完整事件和明确限制的模拟报告；另有独立的 M0 / M3 合成 NPC 候选收敛与两组独立状态复验诊断，可导出既有校准报告 schema，且不写入缓存；取消时终止子进程并等待工作线程清理；正式启动按钮固定禁用 | `test_workflow_panel.py`、`test_synthetic_replay.py`、`test_desktop_calculator.py`、`test_simulation_session.py`、`test_workflow_simulation.py`、`test_desktop.py`、`automation-run-report.schema.json`、`npc-calibration-report.schema.json`；NPC 观察样本与候选仍由同一后端生成，只验证校准流程，不代表独立算法或实机验证；NPC 诊断与 M2 不是同一协调任务，完整 M3–M6 UI 阶段执行、场景分类与真实设备动作仍未接入 |

## 当前仍未就绪

- 全局 `FrameworkReady` 尚未成立：M3 尚未连接实际探测动作 / 采集流程；M4 / M5 可由可选阶段工厂在同一任务租约中串接，仍没有具体场景阶段脚本与真实硬件动作端；M5 回放输入是已标注事件 / 字段数据，不含画面分类或 OCR；M6 离线归因、限幅更新、回退、持久归档和安全重启门禁已有实现，但尚无真实提前 / 延后样本与重复运行验收；M7 产品验收及第 23.1 节其余门槛仍需实现。任何模型仍待实机复验。
- M7 尚未完成：桌面已有单轮 M2 模拟、独立的合成 NPC 校准诊断、场景证据管理、回放逐帧浏览和两项故障注入，但 M2→M6 同一任务协调、场景守卫、反查 / 保存诊断入口、完整故障矩阵和长时间运行验收仍未接入完整 UI。求种验证位与 NPC 观察样本均由同一计算后端生成，只验证流程接线，不独立验证算法；合成数据不是画面识别验证。正式启动入口保持禁用。
- 没有真实设备适配器、真实动作链、实机动画识别校准、捕获身份确认或保存确认。每个场景保持 `PendingHardwareValidation`；任何模拟报告都不能改变这一状态。
- C# 诊断调用里的 `sourceKind` 默认是 `unknown`。调用方必须给出素材来源；报告只记录证据引用，不代替证据清单对文件、设备配置和版本进行审核。

因此，当前工作区已实现 M0-F、M1-F、M2-F，并补入 M3 / M4 / M5 / M6 的部分离线基础模块，但尚未达到方案第 23.1 节的整体 `FrameworkReady` 验收，更没有达到任何实机硬门槛。

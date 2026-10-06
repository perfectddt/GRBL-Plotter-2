# AutoWriter → WorkBuddy 开发交接清单

交接日期：2026-10-05。用户决定改用 WorkBuddy 接续开发。本文以当前工作区和现场反馈为准，阶段1尚未全部完成。

## 1. 项目与工作区

- 工作目录：`D:\yy-code-git\autowriter`。
- 用户 fork：`git@github.com:perfectddt/GRBL-Plotter-2.git`，基于 GRBL-Plotter 二次开发写字机器人软件。
- 当前分支：`codex/stage0-baseline`；HEAD：`20d574c9`。
- **全部后续开发仍在未提交工作区中，未 commit、未 push。不要重新克隆覆盖当前目录，不要 reset/clean 或撤销现有修改。**
- `git status --short` 包含既有修改及未跟踪目录 `.codex/`、`Microsoft/`、`写字功能图片/` 等。不能假定它们都是可删除的临时文件。
- 全局约定：删除默认送入系统回收站，禁止以永久删除命令替代；目录合并先核对内容、保留独有文件与冲突版本。
- 原要求“5h额度剩余15%时停止”针对本次 Codex 执行；迁移到 WorkBuddy 后不要假定其额度规则相同。

## 2. 用户目标与参考资料

用户有现成写字机器人，订单名称“2026豪华plus版 空气悬浮 无声版”，原软件不好用，选择在开源 GRBL-Plotter 上开发自己的软件。

优先阅读：

1. `doc/AutoWriter-二次开发说明.md`：分阶段计划及原软件截图功能对照。
2. `doc/阶段1-使用与实机验收.md`：当前功能使用、配置与验收边界。
3. `doc/开发记录.md`：逐次开发、实机测试及用户反馈；后面的修正覆盖早期误判。
4. `doc/阶段1-暂停恢复终止核实.md`：暂停/恢复/终止的六种入口、源码语义与现场测试计划（**实机 R1–R4 全部完成**：R1 通过、R2 部分通过、R3/R4 未通过；含 `!`/`~` 与 ⏸/■/重置 的实测时延对比）。
5. `写字功能图片/`：原软件12张截图。截图仅能证明可见界面，不能证明底层协议或所有功能实现。

原软件快捷方式：`D:\360MoveData\Users\Administrator\Desktop\奎享雕刻.exe.lnk`。
原安装目录：`C:\Users\Administrator\Downloads\写字软件【下载后解压使用】\奎享雕刻`。
不需要破解加密模块，也不要读取许可、认证信息。以用户导出的实际G-code和合法配置为依据。

## 3. 已完成代码与环境

### 阶段0

- Windows、C#、传统 .NET Framework 4.8 WinForms 项目，VS2022/MSBuild 可构建。
- 提供构建、离线启动和基线验证脚本。
- 已处理用户报告的字体过大、两行重叠问题：调整 App.config 的 DPI 设置，保留字体尺寸。真正的高DPI适配仍需后续完善。
- 离线启动、中文资源、基础预览曾验证通过。

### 阶段1已实现部分

- `GRBL-Plotter/AutoWriter/Models/MachineProfile.cs`：JSON机器配置模型，未知值保持未知；包含笔控、坐标映射、速度、范围、验证证据等。
- `Services/MachineProfileService.cs`：配置读取、校验、保存与备份。
- `Services/MotionCalibrationService.cs`：皮带/丝杆/电子齿轮与测量校准计算。
- `Services/CalibrationProgramService.cs`：离线短线、十字、方框程序生成与范围检查。
- `MachineSetupForm.cs`：配置、校准计算和离线程序预览界面。
- `InspectionPolicy.cs`、`ControlSerialFormInspection.cs`、`MainFormInspection.cs`：受限串口查询和明确选定的小步测试。
- `Program.cs`、主界面及串口文件接入离线/检查模式。
- 新增可空能力字段 `requiresContinuousPenMotion`：该机器需要连续排队笔控。该能力为true时，程序生成禁止正值落笔停留；零停留不输出G4 P0。
- `scripts/test-pen-hold.ps1`：独立、受限现场诊断工具，默认不连接，只有 `-Execute` 才执行。

**配置界面不自动应用到原有工艺；正式文字生成和生产发送链路尚未完成配置集成。**（交接时的状态；2026-10-05 已补：发送链路已接入并经实机发送验证，见第 7 节进度表 5/5d。配置仍未 Verified。）
不要因已有配置、预览或单字诊断就宣布正式写字功能已完成。

## 4. 已核实硬件与协议

- 串口：COM9，CH340，115200，8N1。
- 固件输出：`[VER:1.3a.20221114:]`、`[OPT:MPHB]`，自定义 ESP32 / BtWriter 固件。不要按标准GRBL1.1的所有行为直接推断。
- X正向：向右；Y正向：朝用户靠近。
- **Z正向“向上”的早期回答后来被撤回，不可继续作为结论。** 配置中Z方向仍未完整确认。
- 工作用到G54；查询时G54、G92及TLO偏移为0。每次运行必须重新读取并核对状态。
- `$1=25`；`$3=7`；`$5=1`；`$10=1`。
- `$20/$21/$22=0`：软限位、硬限位、回零未启用；禁止未经现场确认自动回零或认为固件会兜住行程。
- `$100/$101/$102=80/80/100`。
- `$110/$111/$112=5000/5000/8000`。
- `$120/$121/$122=3000/3000/8000`。
- `$130/$131/$132=300/300/300` 是固件保存参数，**不是已经实测确认的安全行程**。
- 状态曾持续出现 `Pn:XY`；意义待核实，不擅自修改反相或限位参数。
- 曾捕获ESP32启动/重启信息。查询模式加入启动等待和重启检测；不能把一次重启武断用于解释全部历史现象。

## 5. 笔控发现：接续开发必须保留

原软件界面为“步进笔控写字机”：抬笔0、落笔5、速度10000。

用户实际导出：`D:\360MoveData\Users\Administrator\Desktop\未命名1\未命名1.gcode`。
仓库证据副本：`artifacts/stage1/kenjoy-original-export-20261005.gcode`。

实际命令：

```gcode
G1G90 Z0.0F10000
; 抬笔移动到下一笔起点
G1G90 Z5.0F10000
; 连续XY绘制
G1G90 Z0.0F10000
```

- 独立发Z5后等待：用户看见笔夹下压后立即回弹，低于约0.5秒；控制器报告Z5不能证明机械持续落笔。
- 临时将 `$1` 改255、等待3秒也未解决回弹。已恢复并查询确认 `$1=25`。**不要把255当成已验证解决方案。**
- 将“Z5落笔→XY绘制→Z0抬笔”连续排队，用户确认移动时保持下压、结束后抬起。
- 不要在落笔与绘制之间人为等Idle、插入观察延迟或假定G4能保持压笔。
- 不把Z0/Z5解释成真实上下5mm机械行程；这是该固件已验证的控制值。
- 开发生产发送流程时必须审查缓冲、暂停、停止、错误处理：串口`ok`表示接收，不表示运动完成。
- 目前只证明部分现场路径有效，尚未确定原理，不能保证暂停时还能保持落笔。

## 6. 实机测试结果

每次成功测试后均已抬笔返回起点并关闭串口。以下没有写步数、最大速度或加速度参数。

| 测试 | 用户结果 | 结论范围 |
|---|---|---|
| X 1mm 带笔 | 有短线、压纸正常、无拖纸 | 小范围连续笔控通过 |
| X 10mm | 约10mm、连续、无异常 | 不修改X比例 |
| Y 10mm | 约9.5mm、连续、无异常 | 短线测量不足以校准 |
| Y 100mm，F300 | 约97mm、断续 | 不直接归因步数或速度 |
| Y 100mm，F1000 | 约98.5mm、连续、无异常 | 较快长线通过一次 |
| 同速Y复测 | 约99.5mm、其余正常 | 两次相差1mm，不建立固定比例修正 |
| 10mm方框，F1000 | “都好” | 目视连续闭合、压纸正常，无具体边长尺量 |
| 三条10mm横线、间距5mm | 连续、分开、换行无连接线 | 抬落笔衔接通过 |
| 10×10mm预设几何“工”字 | 正常、连续、无拖线 | 横竖组合及换笔定位通过 |

用户要求移动利索，当前测试使用画线1000mm/min、抬笔定位/返回1800mm/min。不要把此局部通过扩大成全行程或所有图形的速度验收。

`$101`保持80。不要按97mm、98.5mm或99.5mm中的任意一个值直接写校准。

关键日志，均在 `artifacts/stage1/`：

- `$1=255`失败并恢复：`pen-hold-20261005-094605-9e87f51fe2bf4d1bb2b0e2490bc63538.txt`。
- Y100/F300：`pen-hold-20261005-101639-8d4592356677497eb13227cbe505d7a4.txt`。
- Y100/F1000，98.5mm：`pen-hold-20261005-102739-01fb8929cb7a4d66bd0908661607cd31.txt`。
- Y100/F1000，99.5mm：`pen-hold-20261005-103757-70342aa6d7624de5acd0e8e40b0c2aba.txt`。
- 方框：`pen-hold-20261005-104213-bfead739b9c34557a9df1aebdbb95aac.txt`。
- 三横线：`pen-hold-20261005-105019-38972c97cc8b45c585397de94275acb0.txt`。
- 工字：`pen-hold-20261005-105339-eab5118544bb4c059416453d30116a49.txt`。

`artifacts/`被Git忽略，转移到其他机器时必须单独复制需要的证据。候选JSON配置均未全面Verified，不要使用早期误记Z方向的配置，也不要修改验证标记以绕过保护。

## 7. 接手后先做什么

1. 保存当前工作区，阅读上述文档和git diff，保留已有代码。
2. 运行离线测试与构建，确认接手环境可用。禁止把硬件诊断脚本当作默认测试套件运行。
3. 绘制代码调用关系：配置界面→字体/路径生成→G-code生成→串口缓冲发送→状态/错误/暂停/取消。尤其确认原有笔控参数入口和是否插入停留命令。
4. 实现显式的“应用机器配置”流程，展示将应用的串口、笔控与坐标信息，未知值不能自动补成已验证。
5. 将连续笔控能力接到正式程序生成和发送，落笔与XY之间不等Idle；跨笔画抬笔后定位。先离线检查代码，再现场小字验证。
6. 核实暂停、恢复、终止的机械结果与固件语义，记录停止后是否抬笔、位置是否可靠、能否恢复。不要盲目自动复位、解除报警或续写。
7. 全部范围、方向、速度、偏移及现场证据齐全后，再按规则完成配置Verified和阶段1验收。
8. 后续文字功能按开发说明分阶段：单线中文字体、缺字提示/回退、多字形权重、可复现随机变形、页面排版/模板/分页、预览、任务执行等。需要区分手写中心线和普通字体轮廓，不能把轮廓绘制宣称为真实笔画写字。

### 2026-10-05 WorkBuddy 接手进度

| 项 | 状态 | 说明 |
|---|---|---|
| 1 保存工作区、读文档与 diff | 完成 | 未 commit、未 push、未 reset/clean；保留既有修改与未跟踪目录 |
| 2 离线测试与构建 | 完成 | services/program/inspection 回归 PASS；Debug/Release 0 错误 |
| 3 绘制调用关系 | 完成 | 见 [阶段1-写字流程接入设计](阶段1-写字流程接入设计.md) 第 1–2 节；确认笔控入口与 G4 插入点 |
| 4 显式“应用机器配置”流程 | 完成（离线） | 新增 `MachineProfileApplier` 与配置窗口“应用与写字预览”页；未知值不补、只描述不写固件 |
| 5 连续笔控接入生成 | 完成（生成端 + 发送集成 + 实机发送） | `WritingProgramService` 保序、连续笔控不输出 G4、落笔后紧接 XY；发送集成：`MachineSetupForm` 新按钮经 `MainFormWriting.LoadWritingIntoPreview` → `InsertCodeFromForm` 载入主界面，**载入本身不发送**；发送由操作者用主界面绿色 ▶ 显式触发 |
| 5a 实机小字（王 5mm） | 完成（目视通过） | 受限安全壳执行；机器侧 4 笔按序、连续下压、回起点、`$1=25`；纸面目视“看起来没问题”（非精度验收） |
| 5b 多字行（工十王 5mm） | 完成（目视通过） | 9 笔按序、连续下压、回起点、`$1=25`、无复位、未写固件；纸面三字成形、分开、换笔干净 |
| 5c 字号对比（王 5/6/8mm） | 完成（目视通过） | 12 笔按序、回起点、`$1=25`；三格并排清晰、最小 5mm 未见糊连 |
| 5d 端到端发送（界面生成→载入→发送） | 完成（目视通过） | Normal 模式实测；22 行载入、`streaming finished ok True`、约 4 秒；纸面「工」三横一竖清楚连续无拖线。**结尾只抬笔不回原点，需手动回位**；连续笔控在正式链路下本次未目视确认 |
| 6 暂停/恢复/终止核实 | **实机 R1–R4 全部完成**（R1 通过、R2 部分通过、R3/R4 未通过，均属设计如此） | 见 [阶段1-暂停恢复终止核实](阶段1-暂停恢复终止核实.md)：界面上 6 种「停」；**六种都不抬笔**；`!`/`~` 是唯一"立刻停 + 位置连续 + 可续画"的暂停；恢复分支用 `G0` 先动 XY 有带笔横移风险。**R1**（`!`+`~`）20:49 **通过**。**R2**（⏸+继续）21:06 **部分通过**：喂行立即停、恢复干净可续画，但**要等约 10 秒才真停**（在途约 2 个字/约 30 行），停点不可控 → 不能当急停。**R3**（■）21:29 **未通过**：■ 不等 IDLE、不给控制板发停机指令，**按 ■ 后机器又画约 2 个字/约 10.7 秒**，界面显示"已停止"但账目领先机器约 2 个字；`fix overflow` 告警串长度 ≈ 机器继续运动时间。**R4**（重置 Ctrl-X）21:45 **未通过**：**约 1 秒内硬停**（比 ⏸/■ 快一个数量级），控制板报 `ALARM:3` → **位置确定丢失**、无回零只能人工对位、报警态须发 `$X`；`Bf:15` 证明在途指令被硬丢。**R4 顺带查出上游界面 bug：复位后 COM CNC 窗指令行与所有 `$` 按钮被锁死**（`ProcessGrblAlarmMessage`/`ProcessWelcomeMessage` 少了 `UpdateControls()`），COM CNC 窗的「重置」或断开重连可解锁。另修正：**「重置」有两个不同入口**（流量控制面板 = 裸发 Ctrl-X、软件不自知；COM CNC 窗 = 完整软复位） |
| 6a 候选 G + H（复位护栏） | **已实现**（Release 构建 + 离线测试通过；软件已随 00:51 的 Debug 上屏，**界面走查步骤见 [核实文档 §5.8](阶段1-暂停恢复终止核实.md)**） | **G**「重置」二次确认：新增 `AutoWriter/ResetGuard.cs`（只生成文案、无 WinForms 依赖），落点 `MainFormUserControls.OnRaiseCmdEvent`（`e.Realtime == 0x18`，流量控制的裸字节）与 `ControlSerialForm.BtnGRBLReset_Click`（完整软复位）；`MessageBoxButtons.YesNo` + 默认「否」。**H**：`ControlSerialFormInterface` 的 `ProcessWelcomeMessage()` / `ProcessGrblAlarmMessage()` 在 `ResetStreaming()` 后补 `UpdateControls()`，修掉 5.6.1 的指令行锁死。新增 `scripts/test-stage1-reset-guard.ps1`（PASS）。二者都**不改发送时序**。✅ **2026-10-06 上屏走查：G/H 均生效**（弹框默认「否」、按「是」约 1 秒硬停、指令行恢复可输入）。❌ **但同次走查查出第三个 bug（候选 I-a，已实现）**：`$`/`$$`/`$#`/`$N`/`$G`/`$I`/检查GRBL **全部失效，且 `$X` 解锁后仍失效**。根因**不是** grbl 拒收，而是**软件自己的发送队列被堵死**：`ProcessSend()` 约 1235-1239 行「队首以 `$` 开头时只有 `idle`/`check` 才发，否则 `break`」＋ `ProcessGrblWelcomeMessage()` 末尾**无条件**发 `$10=2`/`ReadSettings()`（软复位后 grbl 正是 `ALARM:3`，命令一进队永发不出）。`$X` 独活是因为它在 `RequestSend()` 里被特殊处理成**旁路直发**，不进队列。硬证据：`logfile.txt` 里 `sendBuffer snt:0 cnfrmnd:1 cnt:10 code:'$$' state:alarm`。修法：`ControlSerialFormInterface.cs` 两处——报警态跳过自动读设置（并提示先按 `$X`、再点「检查GRBL」）＋ `$X` 分支先 `sendBuffer.Clear()`。**Release 0 错误（01:38）；Debug 待重建**（被运行中实例锁住）。详见核实文档 §5.1/§5.2 |
| 6b 候选 J（**Y 方向反向修复**） | **已完成**（Debug/Release 构建 0 错误 + 六套离线测试全 PASS；**R7b 现场复验通过 2026-10-05 23:00**：纸面 F 与 上 均正立） | **R7 判向实测**（非对称 F + 上，`artifacts/stage1/R7-orientation-asym-f-shang.nc`）证明纸面**纯上下镜像**（X 正确、Y 反向，排除 180° 旋转）——用「工」验证方向是**零信息量**。根因：`Services/WritingProgramService.cs` 直接输出 `point.Y`，`axisMapping.yDirection` 全项目**零消费**。修法：新增 `MapAxes()`，把 `xDirection/yDirection` 作为**唯一**方向变换点（顺序：先映射 → 用映射后机器坐标查界 → 输出；`null`→按 `+1` 且写程序头注释）。本机 profile 取 **`yDirection=-1`**（纸面"上" → 机器 `-Y`），契约见设计文档 `machinePoint = origin + A × pagePoint`。`test-stage1-writing.ps1` 新增第 10 组断言。复验程序 `artifacts/stage1/R7b-orientation-fixed.nc`（生成器产出，Y 全为负）**已上机复跑两次、纸面正立**；证据 `R7b-orientation-verified-logfile-20261005.txt` + 纸面照片。**遗留**：2D 视图恒按 `+Y` 朝上渲染（无反向开关）⇒ 纸面对了但预览与纸面互为上下镜像；是否反转控制板 `$3` —— **已由行 6c 落地** |
| 6c `$3` 方案（**预览与纸面统一**） | **已完成并现场确认**（`$3` 7→5、`yDirection` -1→+1、重生成 `R7c-orientation-posY.nc`；六套离线测试 PASS、Release 构建 0 错误；**用户确认"软件里的图与纸面上的已对应"**） | 目的：让恒按 `+Y` 朝上的 2D 预览与纸面**同时正确**，且**不动任何显示层代码**。做法：控制板 `$3` 由 7 改 5（仅清 Y 反相位；bit0=X / bit2=Z 不变）＋ profile `yDirection` 改回 `+1`（生成端不再取负）；净物理效果与 R7b 一致、**原点无需重设**。复验程序 `artifacts/stage1/R7c-orientation-posY.nc`（822 字节，Y 全为正：`Y1/Y11/Y6/Y7`）。**2026-10-06 用户上机并留证**：纸面照片判读 **F 与 上 均正立**（未上下翻转、未左右镜像），**且用户确认 2D 预览与纸面同向** ⇒ **R7c 通过、方向闭环**。证据 `artifacts/stage1/R7c-orientation-paper-20261006.png` + `R7c-orientation-result-20261006.md`。**新增 `R7d-orientation-posY-origin.nc`**（990 字节）：`generate-orientation-check.ps1` 新加 `-Park`/`-OriginAlign` 开关后生成，**首末两行都是 `G1 X0 Y0`**，用于"判向 + 观察起笔/回位"一次完成；**R7c 保持不动**（默认输出经核验仍与归档逐字节一致）。⚠️ **`$3` 变更后所有 Y 为负的旧程序（含 `R7b`）作废**，须改用重生成版本。回退：`$3=7` + `yDirection=-1` + 重生成 |
| 6d 收尾回位（**写完回原点**） | **已实现**（六套离线测试全 PASS + Release 构建 0 错误；界面勾选项已随 2026-10-06 00:51 重建的 Debug 上屏） | 起因：用户发现程序结束停在最后一笔终点，**下次开工要人工把笔挪回去**。根因：`ProgramWriter.Finish()` 只抬笔、无回位（`doc/阶段1-使用与实机验收.md` 早记为已知缺口）。实现：`WritingProgramOptions.ParkPosition`（`WritingPoint?`，struct 须可空；null = 历史行为）；该点走**与笔画点相同**的轴变换与行程校验，程序头写 `; End park: ...`；`ProgramWriter.ParkAt()` **先抬笔再空移**、用空移速度；界面「应用与写字预览」新增勾选项「写完回原点（抬笔）至 X/Y」（**默认勾选、默认 0/0**）；`LoadWritingIntoMainPreview` 改为**每次重新生成**（否则新选项不生效）。安全性：`(0,0)` = `G10 L20` 设的原点，**设零时笔尖就在那儿 ⇒ 必然可达**。新增 `scripts/generate-writing-sample.ps1`（不经 exe、直接编译源码出程序）→ `artifacts/stage1/writing-gong-park-origin.nc`（当时 720 字节，末行 `G1 X0 Y0 F1800`；行 6e 起该文件已带起笔对齐重生成，现为 808 字节）。`test-stage1-writing.ps1` 新增第 11 组断言 |
| 6e 起笔点 = 原点（**第一个落笔点 = X0 Y0**） | **已实现**（12 组离线测试全 PASS + Release 构建 0 错误；界面勾选项已随 2026-10-06 00:51 重建的 Debug 上屏） | 起因：用户提出规则——**"以后开始下笔的第一个点就是原点，这样我好控制起点"**，要求落笔位置可预测、由放纸位置控制，而不是去记字形自带边距。现状：内置示例字形 `(1,11)` 起笔（带 1mm 边距），生成器不平移 ⇒ 首笔落在 `(1,11)`。实现（**改生成端、不动示例几何，任何字形都适用**）：`WritingProgramOptions.AlignStartToOrigin`（默认 `false`，保持类库变换可测）；`Generate()` 新增 `MoveStartToOrigin()`，以**第一个笔画的第一个点**为基准整体平移全部点；**关键顺序 = 先按纸面坐标平移、再做 `MapAxes()` 方向变换** ⇒ 任何 `xDirection/yDirection` 下首笔都精确落在工件原点；程序头写 `; Start point: ... is the work origin (X0 Y0).`；非有限/无效首点**原样返回**交由 `ValidateStrokes` 按笔画名报错（不让 NaN 偏移扩散）。界面「应用与写字预览」新增勾选项「**起笔点=原点（第一个落笔点 X0 Y0）**」**默认勾选**；`generate-writing-sample.ps1` 默认开启并加 `-NoOriginAlign`；判向脚本 `generate-orientation-check.ps1` **默认不动**（保 R7b/R7c 归档可复现）另加 `-OriginAlign`。⚠️ **两个推论**：① 原点是**起笔点**不是包围盒左下角——字形从顶部起笔（内置「工」）时整字落在原点**下方**（本机 `$3=5` 下 Y 为负、纸面朝操作者侧展开，对齐后为 `X 0..10 / Y -10..0`）；② 若配置填了 `usableBoundsWorkMm`，须同时覆盖原点与展开方向，否则**生成期报错**、不静默裁剪（当前候选配置未填行程，故不触发）。与 6d 叠加时**起点终点同为原点**。`test-stage1-writing.ps1` 新增第 12 组断言（含"关闭选项时输出与组 1 基线逐字节相同"） |
| 6f 2D 视图（**显示方向 + 抬笔可视化**） | **已实现**（离线测试全 PASS + Release 构建 0 错误；界面菜单已随 2026-10-06 00:51 重建的 Debug 上屏） | 需求：用户要"2D 视图能左右翻转、上下翻转、顺/逆时针旋转"，并说"视图里没体现 Z 轴，希望按下与抬起不同颜色（或线条），且能关闭抬起来的轨迹"。**查证后两个功能其实都已存在、只是没被看到**：① `penUp=Green` / `penDown=Red` 本就是两支笔，视图菜单早有「Pen-Up path」开关，**但 `gui2DPenUpShow` 默认 `False`** ⇒ 抬笔默认不画，只见红色落笔，这就是"看不出 Z 轴"的根因；② 翻转/旋转早已有，但在 **G 代码菜单**（`Mirror X/Y`、`Rotate 90/-90/180/自由角度`，走 `TransformGCode*`，**改写代码**）。**用户经 AskUserQuestion 拍板**：翻转/旋转 = **仅改屏幕显示**；抬笔 = **默认显示 + 虚线**。实现：**新增 `GUI/ViewTransform.cs`**（只依赖 System.Drawing，可离线测；`MirrorX/MirrorY/QuarterTurns`，变换绕图形中心，**identity 时返回单位矩阵 ⇒ 未启用时绘制路径与改动前完全一致**，故回归风险为零）；**符号铁律（实测）**：世界 Y 向上时 GDI+ `Rotate(+90)` 经视图 `ScaleTransform(s,-s)` 后屏幕表现为**逆时针** ⇒ **顺时针传 -90°**。`MainFormPictureBox.cs`：Paint 里 `MultiplyTransform(V, Prepend)`；新增 `ViewFitRange()` 处理转 90° 的宽高比（改 `CalculatePicScaling`/`Paint`/`OnPaint_scaling` **三处**，第三处是"G代码作背景图"路径）；新增 `ViewToMachine()` 用逆矩阵反算 ⇒ **读数/标记仍是真实工件坐标**；翻转期间**暂停点选图形**（否则会选错），平移缩放正常；`SetupViewOrientationMenu()` 在代码里动态建菜单（不动 .resx），`MainForm_Load` 调用；`penUp.DashStyle = Dash`；`gui2DPenUpShow` 默认 `False→True`。新增 `scripts/test-stage1-viewtransform.ps1`（用**真实 GDI+ 矩阵**验证顺/逆时针方向、镜像轴、刚体性、中心不动点） |
| 7 配置 Verified 与阶段 1 验收 | 未开始 | 需完整实机证据；小字/端到端通过均不等于配置 Verified |
| 8 后续文字功能 | 未开始 | — |

新增/变更文件：`Services/WritingProgramService.cs`、`Services/MachineProfileApplier.cs`、`Models/WritingStroke.cs`、`MainFormWriting.cs`、`MachineSetupForm.cs`（发送集成按钮）、`AutoWriter/ResetGuard.cs`（候选 G 的文案）、`scripts/test-stage1-writing.ps1`、`test-stage1-ui-smoke.ps1`、`test-stage1-reset-guard.ps1`、`scripts/test-pen-hold.ps1`（新增 `-CharacterRow`/`-SizeCompare`/`-DrawOffsetXmm/-DrawOffsetYmm`）。另外改了三处上游文件（G/H）：`GUI/MainFormUserControls.cs`、`MachineControl/ControlSerialForm.cs`、`MachineControl/ControlSerialFormInterface.cs`。六套离线测试（services/program/inspection/writing/ui-smoke/reset-guard）全 PASS；**本次 G/H 改动的 Release 构建 0 错误**（Debug 因正在运行的实例锁住 `bin\Debug\GRBL-Plotter.exe`，需先关程序再重建）。

**候选 J 追加（2026-10-05 22:55）**：`Services/WritingProgramService.cs`（新增 `MapAxes()` + `Resolved` 增方向字段）、`artifacts/stage1/machine-profile-continuous-pen-confirmed.json`（`yDirection` `1 → -1`）、`scripts/test-stage1-writing.ps1`（第 10 组断言）；新增复验程序 `artifacts/stage1/R7b-orientation-fixed.nc`。**Release 与 Debug 均已重建（22:55，0 错误）**，六套离线测试全 PASS。

**起笔对齐追加（2026-10-05 23:59）**：`Services/WritingProgramService.cs`（新增 `MoveStartToOrigin()` + `WritingProgramOptions.AlignStartToOrigin`）、`MachineSetupForm.cs`（新增勾选项「起笔点=原点（第一个落笔点 X0 Y0）」，默认勾选）、`scripts/generate-writing-sample.ps1`（默认开启 + `-NoOriginAlign`）、`scripts/generate-orientation-check.ps1`（判向脚本默认不动、另加 `-OriginAlign`）、`scripts/test-stage1-writing.ps1`（第 12 组断言）；重生成 `artifacts/stage1/writing-gong-park-origin.nc`（808 字节）。**Release 构建 0 错误**、12 组离线测试全 PASS。✅ **Debug 已于 2026-10-06 00:27 重建**（用户关闭旧实例后），界面已含该勾选项。

**2D 视图追加（2026-10-06 00:30）**：**新增** `GUI/ViewTransform.cs`（显示方向状态 + 矩阵）、`scripts/test-stage1-viewtransform.ps1`；**改动** `GUI/MainFormPictureBox.cs`（应用显示矩阵、`ViewFitRange`、`ViewToMachine`、选择保护、`SetupViewOrientationMenu`、抬笔虚线）、`GUI/MainForm.cs`（`MainForm_Load` 里调用建菜单）、`Properties/Settings.settings` + `Settings.Designer.cs`（`gui2DPenUpShow` 默认改 `True`）、`GRBL-Plotter.csproj`（加编译项）。**Release 构建 0 错误**、视图变换离线测试全 PASS。✅ **Debug 已于 2026-10-06 00:27 重建**（0 错误）⇒ 界面菜单与抬笔虚线现已可见。

**判笔规则修正追加（2026-10-06 00:45）**：用户截图暴露「勾选了 Pen-Up path 也看不到分层」——根因是**判笔规则不匹配**（视图只认 `G0/G1` 切换、`Z<0` 落笔、`(PD`/`(PU` 标记；本项目**全 G1 + Z5 落笔**，三条全不满足 ⇒ 整段程序含空移全判落笔、`pathPenUp` 恒空）。**新增** `MachineControl/PenZClassifier.cs`（Z 过渡判据，零依赖）+ `scripts/test-stage1-penzclassifier.ps1`；**改动** `MachineControl/GCode2DViewpaths.cs`（zDown/zUp 各追加一项，原判据未动）、`Properties/Settings.settings` + `Settings.Designer.cs`（新增 `gui2DPenZEnable`/`gui2DPenZUpValue`/`gui2DPenZDownValue`）、`AutoWriter/MachineSetupForm.cs`（载入前按 profile 同步这三个设置）、`GRBL-Plotter.csproj`。**生成端一个字节未改**。**Release 构建 0 错误**、判笔离线测试全 PASS。✅ **Debug 已于 2026-10-06 00:51 重建**（用户关闭实例后，0 错误；二进制符号核验 `PenZClassifier`/`IsPenDownMove`/`IsPenUpMove`/`gui2DPenZ*`/`SetupViewOrientationMenu`/`ViewTransform`/`AlignStartToOrigin`/`ParkPosition` 全部在；`bin\Debug` 与 `bin\Release` 现均含全部功能）。⚠️ 旧 user.config 里 `gui2DPenUpShow=False` 仍在 ⇒ **要在视图菜单里勾一次**才能看见抬笔虚线（勾选会持久化）。

实机：按用户指示“先①多字行、再②字号对比、最后③发送集成”执行；①②机器侧全通过（日志 `artifacts/stage1/pen-hold-20261005-113731-*`、`pen-hold-20261005-113754-*`），纸面目视确认通过（照片 `artifacts/stage1/row-and-size-paper-20261005.jpeg`）；未写固件、配置保持 Unverified。

③发送集成已走通端到端实机发送：Normal 模式（`-Mode Normal`）下“生成并载入主界面预览 → 开始模拟 → 绿色 ▶ 发送”，22 行、`streaming finished ok True`，纸面「工」目视通过。证据：`artifacts/stage1/end-to-end-send-logfile-20261005.txt`、`end-to-end-sent-program-20261005.nc`。

下一步建议：⑥ **暂停/恢复/终止四轮实机核实已全部做完**（证据：R1 `artifacts/stage1/R1-feedhold-20261005.*`、R2 `R2-softpause-20261005.*`、R3 `R3-stop-20261005.logfile.txt`、R4 `R4-reset-20261005.*`）。一句话结论：**只有「进料保持 `!`」是真正"立刻停、位置连续、可续画"的**；⏸ 要等约 10 秒且停点不可控，■ 不给控制板任何停机指令（界面说停了、机器还在画约 2 个字），「重置」约 1 秒硬停但**丢位置、锁界面**。

接下来按性价比排序：
1. ~~**R7b 上机复跑**~~ → **已完成（2026-10-05 23:00，通过）**：载入 `artifacts/stage1/R7b-orientation-fixed.nc` 连发两次，纸面 **F 与 上 均正立**，`(X 正确, Y 正确)`，上下镜像消除。证据 `R7b-orientation-verified-logfile-20261005.txt` + 纸面照片。⚠️ **判向正确性与"是否重启软件"无关**：发的是预先算好的 .nc 文件，坐标已写死；只有"界面现场生成"才依赖含 `MapAxes()` 的 exe。
2. ✅ **候选 G + H 的上屏确认 —— 已完成（2026-10-06）**：弹框正确出现且默认在「否」、按「是」后约 1 秒硬停、COM CNC 指令行**恢复可输入** ⇒ **H 通过**（R4 时全灰的 bug 已消除）。⚠️ 遗留一项待用户复验：用户报告"按钮按了无效"，判定为 **ALARM:3 锁定**（grbl 拒收除 `$X` 外的命令）而非 UI 未启用——按钮与文本框共用 `isConnected && (!isStreaming || isStreamingPause)`，不可能一亮一灰。**请点一次 `$X` 按钮确认能否解锁**，若也不能则回报。详见核实文档 §5.8 第 5 节。`bin\Debug` 与 `bin\Release` 均已为最新（含 G/H + J + 收尾回位 + 起笔对齐 + 视图方向 + 判笔修正）；`bin\Debug-GH` 是更早的临时绕锁副本，**已过时、可弃用**（未删除）。
3. ~~**2D 视图与纸面镜像的取舍**~~ → **已解决（2026-10-05 23:35，进度表 6c）**：走控制板 `$3` 7→5 + profile `yDirection` 改回 `+1`，**用户已确认"软件里的图与纸面上的对应了"**。显示层代码一行未动。
4. **收尾回位 + 起笔对齐的上屏与上机（进度表 6d / 6e）**：① **Debug 需再重建一次**（2026-10-06 00:27 那版**不含** 00:45 的判笔规则修正；用户再次打开程序锁住了 `bin\Debug`，须先关程序）；② 不连机器走查「应用与写字预览」页是否出现「写完回原点（抬笔）至 X/Y」与「起笔点=原点（第一个落笔点 X0 Y0）」且**都默认勾选**，并看 2D 视图的「视图方向」五项；再在**视图菜单里勾一次「无切削、落笔的路径（快速移动）」**（旧 user.config 存着 `False` 会覆盖默认值），确认三条空移**变成绿色虚线**、落笔仍是红色实线（判笔修正生效的标志）；③ 上机跑一次 `artifacts/stage1/writing-gong-park-origin.nc`（或界面现场生成），确认"**首笔落点即原点** → 画完 → 抬笔 → 回到原点 → 停住"，纸面无多余拖线。⚠️ 该程序用的是**内置示例「工」（全对称字形）**，只适合观察**落笔点与回位动作**，**不能用来判方向**（判向必须用非对称字形，见 `R7-orientation-result-20261005.md`）。
4. 已有"工"相关程序（`end-to-end-sent-program-20261005.nc` 等）**需重新生成并重验**（方向已变）。
5. **R5 连续笔控在正式发送链路的落笔横移目视**（核实文档 5.9）——"够不够放心连续写"唯一没闭环的现象。程序已备：`artifacts/stage1/R5-continuous-draw-40mm.nc`（3 条 40mm 横线、F200、约 40 秒，范围落在 R2 已验证的安全盒内）。**需装笔**。方向已由 R7b 确认，R5 可做（但注意 R5 程序是**旧方向**生成的，需按 `MapAxes()` 重生成或接受其方向仅用于笔控观察）。
6. **R6 暂停期间手动动机器后恢复**（核实文档 5.10）——确认是否走 `[Restore Position]` 分支（该分支至今未触发）。**首次务必不装笔**，程序用 `R2-software-pause-longprogram.nc`。
7. 可选 **候选 I**：把「流量控制」的「重置」改为走完整软复位，消除两个「重置」的行为不一致（G 落地后两个入口都会弹确认框，但**行为仍不一致**：一个只发字节、一个会存坐标清状态）。
8. 补齐行程/方向/速度现场证据后再谈配置 Verified。

提示：做软件暂停核实**必须先把程序拉长**（内置换能「工」太短，全部代码 <1 秒喂完，按暂停看不到动静；已备好 81 行的 `R2-software-pause-longprogram.nc`）。另：判断控制板是否在线**不要看系统串口列表**——`Win32_SerialPort` 不枚举 CH340 之类的 USB 转串口；改用 `Win32_PnPEntity` 查 `USB-SERIAL CH340 (COM9)`，或直接看 COM CNC 窗有没有实时状态刷新。


## 8. 离线构建与验证命令

在 PowerShell 中，工作目录为 `D:\yy-code-git\autowriter`：

```powershell
& ./scripts/test-stage1-services.ps1
& ./scripts/test-stage1-program.ps1
& ./scripts/test-stage1-inspection.ps1
& ./scripts/build.ps1 -Configuration All -SkipRestore
git -c core.whitespace=cr-at-eol diff --check
& ./scripts/start-autowriter.ps1 -Mode Offline
```

此前这些C#离线检查和Debug/Release构建通过；最新诊断脚本扩展另做语法、默认不连接检查和实际现场验证。不要虚构接手后的测试结果。
Debug已有旧项目警告，不必为无关警告扩大本阶段改动。环境缺依赖时再按脚本检查VS和.NET4.8 Targeting Pack，首次缺包可省略SkipRestore。

## 9. 实机执行边界

- 原软件与新软件不能同时占用COM9。交接最后一次测试已关闭新串口，原软件此前用户确认断开；接手时重新确认实时状态。
- 每次换更大区域、方向或装笔状态，需要用户确认纸、笔和余量；别复用历史确认作无限行程授权。
- 量尺结束后移出运动区域。用户希望少做重复确认，但必要的现场状态确认仍要保留。
- 正常启动原GRBL-Plotter可能自动连接/发送复位，先使用Offline；检查模式也不是任意运动许可。
- `test-pen-hold.ps1`只有Execute才接串口；不要在用户不知情时加Execute。独立hold模式会临时写$1，恢复失败时必须停止。
- 连续测试JSON的 `OriginalSettingRestored=false` 在 `SettingMayHaveChanged=false` 时代表没走恢复分支，不代表恢复失败；结合日志确认。
- 连续测试失败后不会盲目自动返回XY。先检查错误、真实位置与笔状态，不能强行重试。
- 实机动作和故障处理应由现场用户配合；只读参数、离线开发无需反复询问许可。

## 10. 可直接粘贴给WorkBuddy的接续请求

> 请接续开发 D:\yy-code-git\autowriter 的 AutoWriter 写字机器人项目。先阅读 doc/WorkBuddy-开发交接清单.md、doc/开发记录.md、doc/阶段1-使用与实机验收.md、doc/AutoWriter-二次开发说明.md，并检查当前未提交工作区，不覆盖、不撤销现有修改。阶段0及部分阶段1已经完成，实机COM9/115200为定制ESP32固件；Z0抬笔、Z5落笔，落笔与XY必须连续排队，独立落笔等待会回弹。直线、方框、三横线和几何工字已通过现场测试。请优先把已验证笔控与机器配置接入正式写字流程，先离线开发和验证，再由我确认现场条件后小范围实机验收。不要自动写固件参数、回零、复位或宣称阶段1全部完成。删除文件默认进入回收站。完成后给出改动、验证结果、未完成事项与下一步。

---

## 11. 待办（阶段 1 封档后，2026-10-06 09:25）

> **进度（2026-10-06 下午）**：§11.1 候选 **A/B/C/D/E/F/I 全部实现**（提交 `b0ab2f68`；E 经用户批准采用「限流 N=4 + `!`」折中——近乎立停，恢复残余 ≤4 短笔 ≤2cm 缺墨风险）。§11.2 三项**开发侧全部完成**（多字排版 `TextLayoutService` + 字形库 `BuiltinGlyphLibrary`（工/王/一/空格）、字号 `glyphScale`、端到端样本 `artifacts/stage2/stage2-writing-sample-6mm.nc`，10 套件全 PASS，Debug+Release 已重建）。**待用户实机**：① §11.1 全套行为的暂停/恢复实测（重点：C 的边界停日志 `[Pause: drain N line(s) to stroke boundary]`、E 的 `[Feed hold !]`/`[Release hold ~]`、B 的新恢复序列）；② §11.2-3 样本程序的纸面验收；③ §11.3 装机观察（R5、R6 Case A）。

阶段 1（暂停 / 恢复 / 终止）核心链路已闭环：R1–R4 + R6/K（日志+纸面）通过，G/H/I-a/J/K 全部实现；Debug 与 Release 均于 09:09 重建。**以下均为增强、非缺陷**，是否实现由用户决定。

### 11.1 暂停 / 恢复 / 终止 — 增强候选

| 编号 | 任务 | 实现位点 | 说明 |
| --- | --- | --- | --- |
| **A** | 暂停/停止时自动抬笔 | `flowControlText` 钩子（如 `G1 Z0 F10000`） | **对 `!` 进料保持无效**（实时命令无软件干预时机）。改后要避免破坏 `!` 不抬笔的语义（项目约定） |
| **B** | 恢复分支**先抬笔 → G1+显式 F 定位 → 再落笔** | `PauseStreaming` 恢复段 | 消除 `G0` 带笔横移。**K 部分覆盖**：K 在 XY 后重压 Z，但 XY 移动仍先于 Z；理论上 B 更安全 |
| **C** | 软件暂停改为停在笔画边界 | 生成端或发送端需笔画分组标记 | 目前生成程序无分组标记 |
| **D** | 暂停期间**拦截/禁止手动移动**，或恢复前用 `?`/`$#` 核对实际位置 | 恢复段 + COM CNC 命令屏蔽 | 比 B 更保守 |
| **E** | 按 ⏸ 时**先补发实时 `!`**（让机器立刻减速），再走软件暂停逻辑 | `MainFormStreaming.BtnStreamStart_Click` 暂停分支 | 把约 10 秒延迟压到 0，且停点由操作者决定（R2 实测驱动） |
| **F** | 按 ■ 时**先补发 `!`**，或改回"等 IDLE"分支 | `ControlSerialForm.StopStreaming` | 消除"界面已停、机器还在画约 2 个字"的窗口（R3 实测驱动） |
| **I** | 把**流量控制面板**的「重置」改为走完整软复位（`GrblReset`），与 COM CNC 的「重置」一致 | `MainFormUserControls.OnRaiseCmdEvent` | 消除两个「重置」行为不一致；与 G 一起做更稳。G 落地后两个入口都弹确认框，**但行为仍不一致**：一个只发字节、一个会存坐标+清状态 |

**优先级建议**（由用户决定）：**E / F > A > D > B > C**（E/F 立竿见影且风险低；A 满足"6 种停都不抬笔"之外的常见需求；D 治本；B/C 是兜底）。

### 11.2 阶段 2 — 写字功能本身路线

**当前状态**：`AutoWriter/Services/WritingProgramService.cs`（生成端）+ `AutoWriter/MainFormWriting.cs`（界面）+ `AutoWriter/MachineSetupForm.cs`（发送集成）三者已通；`WritingProgramOptions` 支持起笔对齐（默认勾）、收尾回位（默认勾）、字形路径反演、字控合规、行程校验。**已验证**：单字「工」、多字行（pen-hold-20261005-113731 等）、字号对比（5/6/8mm）、端到端发送（含纸面目视）。

**待办**（按性价比）：

1. **多字排版**：把"一笔一调 Generate"提升为"一字符串 → 单次产出连续程序"。
   - 字距/行距参数（`LetterSpacing` / `LineSpacing` / mm 数值）；包围盒基线对齐。
   - 离线覆盖：固定字符串的程序生成、行尾回位、行程自洽（不出纸、不超 limit）；重写 `scripts/test-stage1-writing.ps1` 多字组断言。
   - 实机：把已有多字行实验程序（`pen-hold-20261005-113731`）用新生成器**现场出程序**并发送一次，纸面对比。

2. **字号**：在 `WritingProgramOptions` 加 `SizeMm`（或 `HeightMm`），生成端按比例缩放全部笔画点。
   - 离线：固定字号产出 + 与基线逐字节对不上**不应是 bug**（缩放本应不同），改为检"包围盒高度 = 输入 SizeMm ± 容差"和"行程不被超"。
   - 实机：5/6/8mm 三排目视对照 `pen-hold-20261005-113754` 旧产物是否仍正确（旧产物是旧方向生成；新产物应与之一致或在新方向上正确）。

3. **实际书写任务**：选一个能稳定展示"全程连续笔控 + 多字 + 字号 + 起笔回位"的小句子（如"工" 6 个或"王" 1 个）。
   - 端到端：界面输入 → 生成 → 预览 → 发送 → 纸面。
   - 通过标准：纸面**单字符内笔画连续无断墨**、**字符之间干净无拖笔**、**首尾皆在原点**、**纸面方向与 2D 预览同向**（已由 R7c 闭环）。

### 11.3 阶段 2 — 装机观察

- **R5 连续笔控横移目视**（`R5-continuous-draw-40mm.nc`，40mm×3 条，F200 单条 12 秒）：阶段 1 多次端到端发送中"工"内 2 秒级连续落笔未观察到中断；**12 秒级**横移仍需装笔目视一次。
- **R6 Case A 完整路径**（K 加入后的 XY+Z 完整重压序列）：先不带笔核对日志出现完整 `G90 G0 X..` + `G4 P0.5` + `G90 G1 Z0` + `G1 Z5` 序列，再装笔做一次纸面。

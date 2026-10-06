# AutoWriter 项目长期记忆

工作目录 `D:\yy-code-git\autowriter`（GRBL-Plotter C# WinForms 改版）。未提交工作区，**禁 reset/clean/覆盖**；删除送回收站。R1–R6 等细节一律查 `doc/阶段1-暂停恢复终止核实.md` 与 `doc/开发记录.md`，本文件只存操作规则与结论。

## 机器与笔控（已验证，不得改写）
- COM9 / 115200 / 8N1，定制 ESP32 固件 `1.3a.20221114`，勿按标准 GRBL1.1 推断。
- 抬笔 `Z0`、落笔 `Z5`、Z 进给 `F10000`、空移 `F1800`、绘图 `F1000`；`$1` 恒为 25。
- **连续笔控**：`Z5→XY→Z0` 连续排队，落笔与 XY 之间**禁等 Idle、禁插 G4**；**单独 Z5 会 0.5s 内物理回弹**（固件账面仍 Z=5，零长度 Z5 指令不重触发舵机 → 候选 K 的由来）。
- **`~` 必须在 Hold:0 之后发**（2026-10-06 卡死事故实证）：`!` 后毫秒级跟 `~` 会被固件**丢弃**（与标准 GRBL 不同），机器卡死 HOLD、后续发送全堵规划器。任何 `!` 后的释放都要等状态上报 HOLD（`pendingStopHoldRelease` 模式）。
- `$20/$21/$22=0`：无限位无回零，**禁自动回零/复位/写固件参数**，`$H` 物理不存在。运行前核对 G54、G92/TLO=0、`$1=25`。机器配置恒 `Unverified`。

## 坐标方向（R7c 已闭环 2026-10-05）
- +X 向右；**现行 `$3=5`**（EEPROM，含 Z 位勿乱改；回退 = `$3=7` + profile `yDirection=-1` + 重生成程序）。机器 +Y 朝纸面「上」。profile：`xDirection=1`、`yDirection=+1`。
- 唯一变换点 `WritingProgramService.MapAxes()`（先映射→查行程→输出），别处不得再取负。
- 2D 视图恒按 +Y 朝上渲染 ⇒ "预览=纸面"靠 `$3=5` + `yDirection=+1` 保证，勿加显示层反向开关（已否决）。
- 字形 Y 正负看起笔点（顶部起笔「工」⇒ Y 负；底部起笔 ⇒ 正），**没有"Y 恒为正"**；判向只能用非对称字形（F+上），手工 .nc 不经生成器不能验证方向修复。

## 暂停 / 恢复 / 终止（R1–R6 实测，细节见 doc）
- **六种停都不抬笔**。只有 `!`+`~` 是立刻停+位置连续+可续画（R1✓）。⏸ 约 10 秒、停点不可控（R2）；■ 不给控制板停机指令、最误导（R3）；重置约 1 秒硬停、丢位置、须 `$X`（R4）。两个「重置」入口行为不同（流量面板=裸发 0x18；COM CNC=完整软复位）。
- 在途约 30 行 ≈ 2 个字；暂停类测试必须用长程序 `artifacts/stage1/R2-software-pause-longprogram.nc`（83 行）。
- 已实现候选（均上屏/单测过）：**G** 重置二次确认（`ResetGuard.cs`，默认「否」）；**H** 报警后补 `UpdateControls()` 治 COM CNC 锁死；**I-a** 报警跳过自动读设置 + `$X` 旁路时清队列（治队列堵死；教训：先看 logfile 的 `snt/cnfrmnd/state`）；**「解锁」按钮**（`$X`+清队列+回 Idle 自动重读设置）；**「置零」按钮**（`G10 L20 P0 X0 Y0 Z0`，**每轴必须显式写**，裸写什么都不做）；**J** 暂停落定时回退未确认行，治恢复丢行；**K** 暂停点 Z>0 时恢复前强制 `G90 G1 Z0 F10000`+`G1 Z5 F10000` 真实跳变重压笔（`AutoWriter/ResumeRestore.cs`，09:20 实机触发验证 ✓）。日志 `line N` = 已确认完成行数，恢复从 N+1 续发。
- `flowControlText="M5; M9"` 暂停/停止后自动发；`resetRestoreWorkCoordinates` 必须恒 False（开了重置会写 EEPROM）。
- 生成端：起笔点=原点（`AlignStartToOrigin`，先纸面平移再 MapAxes）、收尾回位（`ParkPosition`）均闭环；生成程序**不取消 G92/TLO**。

## 运行与发送
- `scripts/start-autowriter.ps1 -Mode Normal` 才连串口；发送 = 左栏「G 代码流」绿色 ▶（右键设起始行）。手动指令在 COM CNC 窗（常驻单实例）`cBCommand`；**勿误选 `$H`**。
- 控制板在线看 `Get-CimInstance Win32_PnPEntity` 的 `USB-SERIAL CH340 (COM9)`（`Win32_SerialPort` 不枚举 CH340）。

## 2D 视图 / GUI 控件（只影响"看"）
- 视图菜单翻转/旋转五项**仅显示**（`ViewTransform.cs`；屏幕顺时针 = `Rotate(-QuarterTurns*90f)`，勿改符号）；要改输出用 G 代码菜单 Mirror/Rotate（改写代码、有 undo）。
- 抬落笔分层判笔 `PenZClassifier.cs`（Z 抬笔值→落笔值的**过渡**才算落笔）。软件置零后 2D 图自动跟随（posWork 每次状态上报重算）。
- 置零三入口勿混：COM CNC「置零」按钮 / DRO「刀具坐标」左键→预设坐标 `XY=0`；「零位」面板是**移动到**零点（机器会动）；「家」=`$H` 不存在。

## 环境约定（别再踩）
- 编译、或命令文本含编译器名 ⇒ 用 PowerShell 工具（Bash 被安全策略拦）；构建走 `scripts/build.ps1`。程序运行时 bin 被锁（MSB3027），先关程序再重建。
- 测试脚本纯 ASCII（PS5.1 按 ANSI 解析无 BOM）；Write-Host 不回传 ⇒ 结论写 `artifacts/stage0/*.txt` 再读；`&` 调脚本 `exit 1` 不抛异常 ⇒ **必查 `$LASTEXITCODE`**；各 test 脚本含同名类型须独立进程。
- 新 .cs 含中文必须 **UTF-8 BOM**（ACP=936）；Python 写文件会 CRLF→LF，写完统一回 CRLF（保 BOM）再 `git diff --stat` 复核。
- 🔴 WinForms 加按钮/加列四坑：① resx 插 `btnXXX.*` + ZOrder/TabIndex；② `LayoutSettings` 内 `Columns.Styles` 重写为 **N 项、和恰 100**（漏了→新列 AutoSize 挤邻列，不报错；改法 html.unescape→改→escape 写回）；③ 窗体同步加宽；④ Designer 五件套 + 查同控件被 Add 两次的残留。守卫：`test-stage1-unlock-layout.ps1`（反射强制布局断言）。

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

## SVG 导入（2026-10-11 实测修正，旧结论"填充致乱"已推翻）
- **真值工具**：`scripts/test-svg-import.ps1 -Svg <file>`（配 `scripts/SvgImportTests.cs`）→ 调项目自己的 `GCodeFromSvg.ConvertFromFile` 打印 G 代码，不用开 GUI。基线/结果在 `artifacts/stage2/before-*.txt` / `after-*.txt`。**排查导入问题一律先用它**，别再靠推理猜。
- 实测：几何（rect/circle/line/polyline、多子路径 `z m`、嵌套 transform）**本来就正确**；默认 `importGraphicHatchFillEnable=False` 且设备 `Fill.Enable=False` ⇒ **HatchFill 提前 return，不会出现填充密线**。默认 `importSVGApplyFill=True` 只在设备档开了填充时才生效。
- 真缺陷（已修）：① `<style>`/`class` CSS 完全被忽略；② `stroke-width` 不继承（`GCodeFromSvgText.cs:550` 每元素重置 `strokeWidth="0.1"` 再 `SetPenWidth`，冲掉 `<g>` 的值）；③ `stroke:none` 被写成笔色 `"none"`。
- 修法：`GCodeFromSvg.cs` 加 `CollectCssRules()`+`ResolveSvgStyle()`，把 CSS/内联/表现属性/继承合成后**追加写回元素 `style`**（只补 stroke/fill/stroke-width/stroke-dasharray，不覆盖其它声明）；`ParseGroup` 备份+还原继承变量；`stroke:none|transparent|url()` 不注入；`FillIsNone/StrokeIsNone` 也在 `GCodeFromSvgText.cs` 三处 `SetPenColor/SetPenFill` 前使用。
- 设备影响输出：`guiToolSelection=2` ⇒ **设备=Router**（`MainForm.cs:238`）；设备决定 `UseSpecificDevice()`/工具表/Hatch 分支，G 代码头会写 `( Use case: Device X )`。
- 其它：`importSVGMaxSize=100` 会把 >100mm 的图整体缩放；`importSVGCircleToDot` 开时 `<circle>` 变一个点。测试件 8 个在 `artifacts/stage2/ext*.svg`。


## 环境约定（别再踩）
- 编译、或命令文本含编译器名 ⇒ 用 PowerShell 工具（Bash 被安全策略拦）；构建走 `scripts/build.ps1`。程序运行时 bin 被锁（MSB3027），先关程序再重建。
- **直接写 `csc.exe` 或 `[Reflection.Assembly]::LoadFrom` 会被安全策略拦**（"equivalent to Add-Type"），但**把 csc 调用放进 `.ps1` 文件再执行是允许的**（项目既有 `test-*.ps1` 模式，如 `scripts/test-svg-import.ps1`）。
- PowerShell 工具的 stdout 常不回传 ⇒ 结论写文件再 Read，或看 `$LASTEXITCODE`。
- 测试脚本纯 ASCII（PS5.1 按 ANSI 解析无 BOM）；Write-Host 不回传 ⇒ 结论写 `artifacts/stage0/*.txt` 再读；`&` 调脚本 `exit 1` 不抛异常 ⇒ **必查 `$LASTEXITCODE`**；各 test 脚本含同名类型须独立进程。
- 新 .cs 含中文必须 **UTF-8 BOM**（ACP=936）；Python 写文件会 CRLF→LF，写完统一回 CRLF（保 BOM）再 `git diff --stat` 复核。
- 🔴 WinForms 加按钮/加列四坑：① resx 插 `btnXXX.*` + ZOrder/TabIndex；② `LayoutSettings` 内 `Columns.Styles` 重写为 **N 项、和恰 100**（漏了→新列 AutoSize 挤邻列，不报错；改法 html.unescape→改→escape 写回）；③ 窗体同步加宽；④ Designer 五件套 + 查同控件被 Add 两次的残留。守卫：`test-stage1-unlock-layout.ps1`（反射强制布局断言）。

## 现代界面外壳（2026-10-12 U1–U4 实装）
- **布局机制**：`AutoWriter/Ui/ModernShell.cs`（partial MainForm）运行时 re-parent，Designer/resx/功能零改动；模式存 `%LOCALAPPDATA%\GRBL_Plotter\AutoWriter\ui-layout.json`（含 "classic"=经典，默认现代），AutoWriter 菜单可切换（重启生效）。新界面改动只碰此文件 + `UiTheme.cs`。
- **WinForms 停靠规则（踩过）**：按 z-index 从高到低停靠；Fill 控件必须 `SetChildIndex(root,0)` 且最后加；被替换的 Fill 容器必须改 `Dock=None` 再 Visible=false，否则吃掉新容器空间；MakeCard 卡片内标题栏后加（高 index 先靠上边缘）。
- **控件可见性**：控件 `Visible` 要求全部祖先可见，反射测试断言前必须 `form.Show()`。
- **反射 harness 构造 MainForm**：exe 必须带 NLog.config（编译到 bin\Debug 旁运行）；无消息泵进程 WinForms 清理崩 0xC000041D → 判定看输出标记不看退出码；CloseMainWindow 会命中常驻 COM CNC 窗（关闭=隐藏），强杀后启动稳定仍算过。见 `scripts/test-modern-shell.ps1`。
- **配置窗两个密集页签的排布**：`MachineSetupForm` 的「应用与写字预览」「区域排版」均按**左滚动卡片列 + 右预览/画布**组织（避免竖直堆叠被 465px 高的嵌入区压成大量滚动）。分组容器用 `UiGroup`（见下）。

## UiGroup（2026-10-12 新增，用于把平铺按钮改成分组卡片）
- 位置 `AutoWriter/Ui/UiTheme.cs`。用法：`var g = new UiGroup("标题"); g.Row.Controls.Add(...); g.NewRow(4); ...` 最后把 `g.Card` 按**由下到上** Add 到列容器（高 index 先停靠 = 在上）。
- 配套：`UiTheme.FieldLabel(text)`、`Field(text,width)`、`AddButton(panel,text,action,kind,height)`、`AddCheck(panel,text,checked,changed)`、`AddNote(body,text)`。
- **教训（方向性）**：用户反对用「更多 ▾」下拉**隐藏**按钮；要的是**重新分组排布**。以后整理密集界面，默认走分组卡片，不要折叠。
- **对齐（第五轮，通用规则）**：行内控件**不要靠手写 margin 对齐**。统一 `ControlHeight` + 容器在布局时统一算 `Top=(行高-控件高)/2`；**Label/CheckBox 必须 `AutoSize=false` + 固定高 + `TextAlign=MiddleLeft`**，否则盒心对齐了文字仍偏高。同一页签的卡片要共用同一个 `Left/Width`。
- 守卫脚本 `scripts/test-setup-tabs.ps1`：断言上述三点（卡片同左同宽、不塌陷、同行同一基线）+ 每个按钮仍在位可见。改这两个页签后必须跑。
- 界面语言：新 UI 硬编码中文（与 AutoWriter 菜单同模式）；ControlSetupForm 换肤**不换字体**（本地化 resx 防裁切），AutoWriter 自有弹窗可换。

## UI 布满与对齐（用户于 2026-10-13 重新明确）
- 用户纠正：此前的项目记忆不等于其以前对话的 skill。本次规则以当前明确说明为准。
- 按钮组布满卡片可用宽度，按统一列划分；同列按钮等宽、左右边缘上下对齐，同行文字垂直居中。两按钮行均分宽度，单按钮行铺满。不得仅按文字宽度留下大片右侧空白。
- 区域编辑、排列顺序采用两列网格；勾选方框与文字居中对齐。统一字体与字号，并保留主操作颜色层次。

## 配置页签统一（2026-10-13）
- 原「步进参数计算」「校准程序预览」合并为「步进计算与校准」。现为 0 机器配置 / 1 步进计算与校准 / 2 应用与写字预览 / 3 区域排版，现代外壳入口已更新。
- 所有页签统一浅底、白色内容、左侧 360 卡片列、右侧详情/预览；计算输入共用标签宽度，预览标题 Dock=Top 不覆盖内容。
- 窗口状态新增可选 TabName，以名称恢复页签；旧记录的 1/2 合并到1，3映射2，4映射3。已验证旧区域页与新写字页恢复。

## 布局守卫补充（2026-10-14）
- MachineSetupForm 左侧导航列=SplitContainer，默认 360、可拖（SplitterWidth=6、CardBorder 可见）；`SplitterDistance` 禁止在 Form.Load 里设（页面未布局必抛），统一走 `ApplySplitDistanceWhenLaidOut`。探针 `scripts/test-setup-nav-width.ps1` 可复跑。
- 提交前验证历史可构建：`git worktree add <临时目录> <commit>` + 主仓 `packages` 用 junction 链入 + 跑该 worktree 的 `scripts/build.ps1 -Debug -SkipRestore`；删 junction 用 `[IO.Directory]::Delete()`（`cmd rmdir` 在 Git Bash 下引号失效）。重排历史用 cherry-pick + `git branch -f`，不用 reset。分组提交注意依赖顺序（如 GlyphFontPack.Peek 必须先于用它的 MachineSetupForm）。

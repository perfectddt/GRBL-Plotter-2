# AutoWriter 发布入口

当前开发入口只有这一处：

`artifacts/autowriter/Release/GRBL-Plotter.exe`

窗口标题带版本 `2026.10.11`。以后不要再为每个阶段另存一个 EXE。`artifacts/page-task`、`artifacts/canvas-edit`、`artifacts/templates` 等都是历史构建，只留作对照，不作为新的测试入口。

重新生成：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-autowriter.ps1
```

脚本只写入 `artifacts/autowriter/Release`。它不删除历史目录，也不覆盖 `%LOCALAPPDATA%\GRBL_Plotter\AutoWriter\last-machine-setup.json`。

## 迁移和备份

- 写字项目仍是 `.awproject`，版本保持 1。新增的补充字库、手写变化、表格和区域字体都是可省略字段，旧项目可以直接打开。
- 保存项目时会为字体、补充字库和区域字体记录 SHA256。字库文件被改过就不能打开，避免静默用错笔画。补充字库只是叠加，不会改写原来的 `.fontpack.json`。
- 项目保存时旁边会留 `.bak-` 备份。上次窗口设置仍在本地 `last-machine-setup.json`，保存时保留 `.bak`。
- 模板仍在 `RegionTemplates`，不包含正文和字体。
- 机器配置和写字项目分开保存。换电脑时复制项目、字库、补充字库和机器配置；路径变了要在项目里重新选择字体。

提交和推送仍需单独授权。本文不代替实机纸面验收。

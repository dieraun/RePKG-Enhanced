# RePKG-Enhanced

跨平台的 Wallpaper Engine `.pkg` 提取工具：**大文件不崩、批量不卡、内存可控**。

基于 [notscuffed/RePKG](https://github.com/notscuffed/RePKG)（MIT，已停更）做最小必要改动，修复其「大文件提取失败」缺陷，并新增 Avalonia 桌面界面。
解析内核与导出逻辑只有一份，CLI 与 GUI 共用。

![主窗口（浅色）](docs/images/main-window-light.png)

## 它修好了什么

原版 `PackageReader` 在解析 pkg 时会执行 `reader.ReadBytes(entry.Length)`，把**整包所有条目**一次性读进内存：

```csharp
private static void PopulateEntriesWithData(int dataStart, List<PackageEntry> entries, BinaryReader reader)
{
    foreach (var entry in entries)
    {
        reader.BaseStream.Seek(entry.Offset + dataStart, SeekOrigin.Begin);
        entry.Bytes = reader.ReadBytes(entry.Length);   // ← 大文件 OOM / 截断的来源
    }
}
```

本项目把**条目表与数据区彻底分离**：读表只记 `FullPath / Offset / Length`，提取时 `Seek` 到条目偏移再按 1 MiB 分块流式复制。

实测（`REPKG_BIG_TESTS=1`，本机 Windows 10 / 7.9 GB 内存）：

| 场景 | 耗时 | 托管分配增量 | 输出校验 |
|---|---|---|---|
| 单条目 1 GiB pkg | 14.1 s | **1.0 MiB** | SHA-256 与期望逐字节一致 |
| 2.2 GiB pkg（两个 1.1 GiB 条目） | 20.9 s | **1.0 MiB** | 同上 |

峰值内存由块大小决定，与条目体积、包总体积无关。

## 功能

- **流式提取**：条目不整块入内存；输出原子写入（临时文件 + 原子替换），中断不留半成品。
- **批量**：目录递归 + 扩展名过滤（`-e/--onlyexts`、`-i/--ignoreexts`，与原版语义一致），单一有界导出队列，并发数与队列长度可配，单条目失败隔离，暂停/取消在条目边界生效。
- **TEX 解码**：`RGBA8888 / R8 / RG88 / DXT1·3·5 / LZ4` → PNG；动画 TEX → GIF（自研 GIF89a 编码器，逐帧不物化）；视频纹理 → MP4 透传；可关掉转换导原始 `.tex`。
- **GUI**（Avalonia 12）：单/批量加载、拖拽导入、条目三态勾选、按图片/文本/视频筛选、TEX/图片/文本预览、导出队列暂停/取消、明暗主题跟随系统。
- **清单与日志**：`manifest.json` 资源清单（条目表 + 偏移），`export-log.jsonl` 逐事件日志。
- **路径安全**：拒绝 `..`、绝对路径、盘符、UNC、保留设备名、符号链接逃逸。

## 命令行

```powershell
# 提取单个 pkg（TEX 自动转 PNG/GIF/MP4，并写 .json 元信息）
repkg extract wallpaper.pkg -o out

# 递归提取一个目录下所有 pkg，只要 tex 和 png
repkg extract D:\workshop -r -o out -e tex,png

# 只导原始条目，不做 TEX 转换
repkg extract wallpaper.pkg --no-tex-convert

# 查看清单（文本 / JSON）
repkg info wallpaper.pkg
repkg info wallpaper.pkg --json

# 单独转换一个 .tex
repkg tex quad.tex -o out

# 每个 pkg 一个子目录，目录名取 project.json 的 title，并附带复制 project.json
repkg extract D:\workshop -r -n -c --manifest
```

退出码：`0` 全部成功 / `1` 有失败项 / `2` 参数错误 / `130` 被取消。

> 与原版 CLI 的**有意差异**：批量时每个 pkg 导出到自己的子目录（原版摊平到同一目录，同名条目会互相覆盖）；不提供 `interactive` 子模式。

## 构建

需要 .NET 10 SDK。

```powershell
dotnet build RePKG-Enhanced.slnx -c Release

dotnet test tests\RePKG.Tests\RePKG.Tests.csproj -c Release
dotnet test tests\RePKG.Tests.Shared\RePKG.Tests.Shared.csproj -c Release
dotnet test tests\RePKG.Tests.Cli\RePKG.Tests.Cli.csproj -c Release
dotnet test tests\RePKG.Tests.Gui\RePKG.Tests.Gui.csproj -c Release

# 大文件门禁（会产生 GB 级中间文件，需 ~10 GB 空闲磁盘）
$env:REPKG_BIG_TESTS = '1'
dotnet test tests\RePKG.Tests.BigFiles -c Release
```

## 发布（Windows x64，NativeAOT 单文件）

```powershell
dotnet publish src\RePKG.Cli\RePKG.Cli.csproj -c Release -r win-x64 -o artifacts\cli
dotnet publish src\RePKG.Gui\RePKG.Gui.csproj -c Release -r win-x64 -o artifacts\gui
```

产物是**单个 exe**：`repkg.exe`（约 13.7 MiB）、`RePKG-Enhanced.exe`（约 35.4 MiB）。
`PublishAot` 与 `PublishSingleFile` 在 .NET 10 无法共存（[dotnet/runtime#117986](https://github.com/dotnet/runtime/discussions/117986)），所以 SkiaSharp / HarfBuzz / ANGLE 的原生库以 `EmbeddedResource` 打进 exe，首次运行时自解压到 `%LOCALAPPDATA%\RePKG-Enhanced\native\<版本>` 再预加载。详见 [docs/06-publish-and-ci.md](docs/06-publish-and-ci.md)。

打 `v*` tag 会触发 [.github/workflows/release.yml](.github/workflows/release.yml)：`windows-latest` 上还原 → 编译 → 四套单测 → AOT 发布 → 校验「目录里不许有 DLL」→ 上传两个 exe 与 `SHA256SUMS.txt` 到 Release。

## 项目结构

```
src/RePKG.Core         解析内核（netstandard2.0，最小改动）
src/RePKG.Application  流式读取、TEX 解码、GIF 编码（net10.0）
src/RePKG.Shared       导出队列、原子写入、路径校验、日志、清单、原生库引导
src/RePKG.Cli          命令行入口（repkg）
src/RePKG.Gui          Avalonia 桌面入口
build/                 MSBuild 片段（原生库内嵌）
docs/                  阶段报告与决策记录
tests/                 单元测试 + 大文件门禁 + CLI/GUI 集成测试
```

## 依赖

| 依赖 | 版本 | 许可 | 用途 |
|---|---|---|---|
| [K4os.Compression.LZ4](https://github.com/MiloszKrajewski/K4os.Compression.LZ4) | 1.3.8 | MIT | TEX 的 LZ4 解压 |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | 3.119.4 | MIT | 像素解码与 PNG 编码（Avalonia 后端同源） |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) | 12.1.4 | MIT | 跨平台 GUI |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.2 | MIT | 源生成 MVVM（AOT 友好） |

TEX 的 DXT1/3/5 解码是原版 RePKG 自带的托管实现，未引入原生依赖。

## 文档

- [阶段 0：事实核查与根因](docs/00-stage0-fact-check.md)
- [阶段 1：目录与文件清单](docs/01-file-inventory.md)
- [阶段 2：流式内核与大文件门禁](docs/02-streaming-core.md)
- [阶段 3：Shared 层（队列 / 原子写入 / 路径安全）](docs/03-shared-layer.md)
- [阶段 4：TEX 栈与 CLI](docs/04-cli-and-textures.md)
- [阶段 5：GUI 与资源清单](docs/05-gui.md)
- [阶段 6：Windows 发布与 CI](docs/06-publish-and-ci.md)
- [决策记录（ADR）](docs/DECISIONS.md)

## 许可

MIT，见 [LICENSE](LICENSE)。原版 RePKG 的许可证见 [LICENSE-RePKG](LICENSE-RePKG)；逐文件改动点在源文件头部注释中标注「基于 notscuffed/RePKG (MIT) 二次修改」。

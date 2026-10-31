# 阶段 6：Windows x64 发布与 CI

- 状态：**已完成，待用户验收**
- 交付物：`src\RePKG.Cli\RePKG.Cli.csproj`、`src\RePKG.Gui\RePKG.Gui.csproj`、`build\NativeBundle.targets`、`src\RePKG.Shared\Native\NativeBootstrap.cs`、`.github\workflows\release.yml`、`global.json`

---

## 1. 需求与变更

阶段 0 定的发布方案是「三平台 × 单文件 NativeAOT」。本轮按用户指示（`m01552`）收敛为：

| 项 | 阶段 0 方案 | 阶段 6 实际 |
|---|---|---|
| 平台 | Windows / Linux / macOS | **只做 Windows x64**（其余平台暂时忽略） |
| 产物形态 | Win/Linux 单文件 + macOS `.app` | **单个 `.exe`，目录里不留任何 DLL** |
| CI | 三平台矩阵 | **只有 `windows-latest`**，打 tag 触发，产物作为 Release 附件上传 |
| 代码跨平台性 | — | **保持**：csproj 里的 AOT/内嵌配置全部带 `Condition="'$(RuntimeIdentifier)' == 'win-x64'"`，Linux/macOS 仍可正常 `dotnet build` / `dotnet run` |

---

## 2. 关键技术事实：`PublishSingleFile` 对 NativeAOT 无效

实测（阶段 0 探针，本阶段复验）：

- 同时设 `<PublishAot>true</PublishAot>` 与 `<PublishSingleFile>true</PublishSingleFile>`，发布目录里 **仍然有** `libSkiaSharp.dll`、`libHarfBuzzSharp.dll`、`av_libglesv2.dll`。
- 上游 dotnet/runtime Discussion **#117986**（2025-07-23）确认：这几个属性组合「并不工作（原生 DLL 依旧躺在 exe 旁边）」，只有去掉 `PublishAot` 才是真单文件。
- .NET 10 另有一条破坏性变更：单文件应用不再在 exe 目录搜索原生库（`learn.microsoft.com/en-us/dotnet/core/compatibility/interop/10.0/native-library-search`）。

结论：想同时要「AOT」和「单文件」，只能**自己把原生库打包进 exe，运行时自解压再预加载**。

---

## 3. 实现：原生库内嵌 + 自解压

### 3.1 机制（已用独立探针 `%TEMP%\aotembed` 验证）

1. 把原生 DLL 作为 `EmbeddedResource` 编进程序集（AOT 会保留嵌入资源）；
2. 启动时把资源解压到 `%LOCALAPPDATA%\RePKG-Enhanced\native\<版本>\`；
3. 用 `NativeLibrary.Load(绝对路径)` **预加载**；
4. 之后 SkiaSharp / HarfBuzz / ANGLE 的 `DllImport("libSkiaSharp")` 等**裸名加载会命中已加载模块**（Windows 加载器按基名匹配）——所以不需要任何 hook 或 `SetDllDirectory`。

探针实测输出：

```
[probe] resource present = True
[probe] extracted = ...\aotembed-cache\1\libSkiaSharp.dll (11628896 bytes)
[probe] NativeLibrary.Load handle = 0x7FFEBCA80000
[probe] png bytes = 94
[probe] RESULT=OK
```

### 3.2 工程落点

| 文件 | 作用 |
|---|---|
| `build\NativeBundle.targets` | 两个 MSBuild 目标：`_BundleNativeLibraries` 把 `@(NativeCopyLocalItems)` 里点名的那几个 DLL 变成 `EmbeddedResource`（`LogicalName = RePKG.Native.<文件名>`）；`_DropBundledNativeLibraries` 从 `ResolvedFileToPublish` 里删掉同名 DLL 与原生库 pdb |
| `src\RePKG.Shared\Native\NativeBootstrap.cs` | `EnsureLoaded()`：幂等、扫描入口程序集与 Shared 程序集里的 `RePKG.Native.*` 资源、按长度校验解压、`NativeLibrary.Load` 预加载；`LastError` / `LoadedLibraries` / `ExtractedFiles` 可观测；任何异常都被吞掉并记录（原生库加载失败不能让程序连一条错误信息都打不出来） |
| `src\RePKG.Cli\Program.cs` | `Main` 第一行 `NativeBootstrap.EnsureLoaded();` |
| `src\RePKG.Gui\Program.cs` | `Main` 里**在 `BuildAvaloniaApp()` 之前**调用（Avalonia 初始化时就要碰 Skia） |
| `src\RePKG.Cli\RePKG.Cli.csproj` / `src\RePKG.Gui\RePKG.Gui.csproj` | `win-x64` 条件组里开 `SelfContained` / `PublishAot` / `BundleNativeLibraries` / `BundledNativeLibraryNames`，并 `Import` 上面的 targets |

内嵌清单：

- CLI：`libSkiaSharp.dll`（TEX 解码与 PNG/GIF 编码需要；CLI 无 UI，不需要 HarfBuzz / ANGLE）
- GUI：`libSkiaSharp.dll` + `libHarfBuzzSharp.dll` + `av_libglesv2.dll`

### 3.3 踩过的坑：目标挂载点

第一版把目标挂在 `BeforeTargets="PrepareResourceNames"`，`Message` 明明打印了「内嵌原生库 → libSkiaSharp.dll」，但 `repkg.dll` 依旧是 **31 232 B**（资源根本没进去）。

用 `dotnet build -v:diag` 看目标顺序才定位到根因：

```
Target "SplitResourcesByCulture" ... (target "PrepareResourceNames" depends on it)
Target "CreateCustomManifestResourceNames" ...
Target "_BundleNativeLibraries" ...
Target "PrepareResourceNames" ...
```

`SplitResourcesByCulture` 是 `PrepareResourceNames` 的**依赖**，它会在 `PrepareResourceNames` 之前就把 `@(EmbeddedResource)` 快照进 `_CoreCompileResourceInputs`；挂在 `PrepareResourceNames` 的 `BeforeTargets` 已经太晚，资源被静默丢弃。

改成 `BeforeTargets="SplitResourcesByCulture"` 后立刻正常：`repkg.dll` 从 31 232 B 变成 **11 660 288 B**。这条经验已写进 `build\NativeBundle.targets` 的注释里。

---

## 4. 发布命令与实测产物

```powershell
# CLI
dotnet publish src\RePKG.Cli\RePKG.Cli.csproj -c Release -r win-x64 -o artifacts\cli

# GUI
dotnet publish src\RePKG.Gui\RePKG.Gui.csproj -c Release -r win-x64 -o artifacts\gui
```

实测（Windows 10.0.19045、.NET SDK 10.0.401、VS 18.11 MSVC 14.51）：

| 产物 | 大小 | 目录内其它文件 |
|---|---|---|
| `artifacts\cli\repkg.exe` | **14 369 280 B**（13.7 MiB） | 只有 `.pdb` |
| `artifacts\gui\RePKG-Enhanced.exe` | **37 118 464 B**（35.4 MiB） | 只有 `.pdb` |

两个目录里**都没有 DLL**（`_DropBundledNativeLibraries` 生效），也没有 `libSkiaSharp.pdb` / `libHarfBuzzSharp.pdb` 这类几十 MB 的调试符号。

### 4.1 CLI 冒烟（清空原生缓存后运行）

```
> repkg.exe --version
repkg 0.4.0

> repkg.exe extract .bigtmp\demo\demo.pkg -o %TEMP%\repkg-cli-out --manifest
共 1 个 pkg → ...\repkg-cli-out（TEX 转 PNG/GIF/MP4）
[完成] demo.pkg → ...\repkg-cli-out\demo  成功 3 / 跳过 0 / 失败 0 / 取消 0，8.0 MiB，2.2s

> 目录内容
demo\project.json  37 B
demo\materials\quad.png  98 B          ← 证明 SkiaSharp 原生库真的加载成功了
demo\materials\quad.json 165 B
demo\scene\big.bin  8 388 608 B
demo\manifest.json  485 B
demo\export-log.jsonl 1 120 B

> 解压出来的原生库
%LOCALAPPDATA%\RePKG-Enhanced\native\1.0.0\libSkiaSharp.dll  11 628 896 B
```

### 4.2 GUI 冒烟（清空原生缓存后运行）

```
pid=6564 exited=False
title=[RePKG-Enhanced — Wallpaper Engine pkg 提取] handle=1967534
rect=52,52 size=1216x819        ← 1200×780 客户区 + 边框
```

解压出的原生库：`av_libglesv2.dll` 5 394 096 B、`libHarfBuzzSharp.dll` 1 816 088 B、`libSkiaSharp.dll` 11 628 896 B。

---

## 5. CI（`.github\workflows\release.yml`）

- **触发**：`push` tag `v*`；另加 `workflow_dispatch`（手动指定 tag，可为已存在的 Release 覆盖上传）。
- **矩阵**：只有 `windows-latest`（用户要求暂时只跑 Windows 流水线）。
- **权限**：`permissions: contents: write`（`gh release create/upload` 需要）；`concurrency` 按 ref 串行，避免两个 tag 同时抢 Release。
- **步骤**：
  1. checkout → `setup-dotnet` 安装 `10.0.x` → `dotnet --info`
  2. 用 `vswhere` 确认镜像里有 MSVC C++ 工具链（NativeAOT 需要 `link.exe`，缺失时**尽早失败**而不是等到链接阶段）
  3. `dotnet restore` → `dotnet build` → 四套单元测试（`RePKG.Tests` / `RePKG.Tests.Shared` / `RePKG.Tests.Cli` / `RePKG.Tests.Gui`，均 `--no-build`）
  4. `dotnet publish` CLI 与 GUI（`-c Release -r win-x64`）
  5. **产物校验**：目录里出现任何 `.dll` 直接 `throw`（防止哪天 targets 失效却发布了一个「假单文件」）；exe 小于 5 MB 也 `throw`
  6. CLI `--version` 冒烟
  7. 重命名成 `repkg-win-x64.exe` / `RePKG-Enhanced-win-x64.exe`，生成 `SHA256SUMS.txt`
  8. `actions/upload-artifact` 存档
  9. `gh release create`（不存在时）或 `gh release upload --clobber`（已存在时）把两个 exe + 校验和挂到 Release

YAML 已用 PyYAML 解析校验（15 个 step，单个 job）。

---

## 6. 验收点对照

阶段 0 与用户验收清单中与本阶段相关的项：

| # | 验收点 | 结果 |
|---|---|---|
| 1 | Windows x64 单文件 `.exe`（CLI 与 GUI） | ✅ 14.4 MB / 37.1 MB，目录内无 DLL |
| 2 | 原生库自解压机制可用 | ✅ 清空缓存后 CLI 转出 PNG、GUI 正常起窗，`%LOCALAPPDATA%` 下解出 3 个 DLL |
| 3 | CLI 功能不受 AOT 影响 | ✅ `--version` / `extract`（含 TEX→PNG、manifest、JSONL 日志）全部正常 |
| 4 | GUI 功能不受 AOT 影响 | ✅ 窗口标题/尺寸正确，无启动崩溃 |
| 5 | CI 只跑 Windows，tag 触发 | ✅ `on.push.tags: v*`，单 job `windows-latest` |
| 6 | 产物自动上传为 Release 附件 | ✅ `gh release create/upload` + `upload-artifact` |
| 7 | 代码保持跨平台 | ✅ AOT/内嵌配置全部 `Condition="'$(RuntimeIdentifier)' == 'win-x64'"`；`dotnet build RePKG-Enhanced.slnx` 不带 `-r` 时照旧 0 error |
| 8 | 提交并 push 到远程 | ✅ 见 `docs\DECISIONS.md` ADR-020 与提交记录 |

全量回归（本次提交前）：

```
dotnet build RePKG-Enhanced.slnx -c Release   → 0 Error（5 条 xUnit1051 测试告警）
RePKG.Tests          40 passed
RePKG.Tests.Shared   63 passed
RePKG.Tests.Cli      13 passed
RePKG.Tests.Gui      15 passed
合计                 131 passed / 0 failed
（BigFiles 门禁另行以 REPKG_BIG_TESTS=1 运行：2 passed）
```

---

## 7. 已知限制

1. **只发布 Windows x64**：Linux/macOS 的发布配置与 CI 作业尚未恢复（阶段 0 的 B′/C′ 方案仍在 `docs\00-stage0-fact-check.md` 里，随时可接）。
2. **首次启动有一次性解压成本**：GUI 约 19 MB（三个原生库）、CLI 约 11.6 MB，解压到 `%LOCALAPPDATA%\RePKG-Enhanced\native\<版本>`；版本升级会重新解压一份，旧版本目录目前**不会自动清理**。
3. **exe 体积**：AOT + 内嵌原生库，CLI 13.7 MiB、GUI 35.4 MiB，比框架依赖发布大，但换来零依赖单文件。
4. **`.pdb` 仍会生成在产物目录**：CI 只上传 `.exe` 与校验和，pdb 不对外分发。
5. **未做代码签名 / 公证**：Windows SmartScreen 可能提示「未知发布者」。
6. **CI 尚未真正跑过**：本次只能在本地复现全部步骤；首次 `v*` tag 触发时若 runner 镜像的 SDK 版本与 `global.json`（`10.0.100` + `rollForward: latestFeature`）不匹配，需要按 CI 日志调整。
7. **`gh release` 依赖 `GITHUB_TOKEN` 的 `contents: write`**：若仓库组织策略收紧默认 token 权限，需要改用 PAT。

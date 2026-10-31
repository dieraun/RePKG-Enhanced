# 阶段 0：事实核查 · 根因确认 · 发布策略选型（v2，含实测更正）

> 项目：RePKG-Enhanced（基于 notscuffed/RePKG，MIT，二次修改）
> 本阶段**不产出任何源码**。核查时间 2026-10-31；基线 `notscuffed/RePKG@8005eb2`（2025-03-12，3829★，MIT，无 release）。
> v2 相对 v1 的变化：**更正 v1 关于"本机缺 MSVC/无法 AOT"的错误结论**（见 §1.2），补入 NativeAOT / 单文件 / SkiaSharp 的实测数据，并按你的决定替换图像栈。

---

## 1. 结论摘要

### 1.1 根因（命中，可定位到行）
`RePKG.Application/Package/PackageReader.cs:54-61` 对**整包每个条目**执行 `entry.Bytes = reader.ReadBytes(entry.Length)`；`ReadEntryBytes` 默认 `true`（:11）、`ReadFrom` 末尾**无条件**调用（:31）。故"提取任意一个条目"= 先把整包数据区读满内存。

### 1.2 更正：本机 AOT 工具链是齐全的
v1 说"全盘无 `link.exe`、无 Windows SDK、无法 NativeAOT"——**这是错的**，我只搜了 `C:\Program Files\...`，而 VS 装在 D 盘。实测事实：

| 项 | 实测值 |
|---|---|
| VS | `Visual Studio Professional 2026`，18.11.12306.390，**`D:\vside`**，productId `Microsoft.VisualStudio.Product.Professional`，channel `VisualStudio.18.Release`，660 个包 |
| 工作负载/组件 | **已装** `Microsoft.VisualStudio.Workload.NativeDesktop`、`Microsoft.VisualStudio.Component.VC.Tools.x86.x64`、`Microsoft.VisualStudio.Component.Windows11SDK.26100` |
| 编译器 | `D:\vside\VC\Tools\MSVC\14.51.36231\bin\Hostx64\x64\{cl,link}.exe` |
| Windows SDK | `KitsRoot10 = D:\Windows Kits\10\`；`Lib\10.0.26100.0\um\x64\kernel32.lib` 存在；`bin\10.0.26100.0` 存在 |
| AOT 冒烟 | `dotnet new console` + `PublishAot=true` + `-r win-x64` → **成功**，产物 `aotprobe.exe 911.5 KB`，耗时 91.5 s（含 ILCompiler 首次下载） |

**结论：不需要安装任何 VS 组件；C 盘只放 NuGet 缓存。** 依赖目录已在 D 盘（`D:\vside` 与 `D:\Windows Kits\10`），后续若要再装组件，包缓存可改 `HKLM\SOFTWARE\Microsoft\VisualStudio\Setup` 的 `CachePath`（本次无需）。

### 1.3 单文件 AOT：SDK 不提供，需自研一层
实测（Avalonia 12.1.4 + SkiaSharp 3.119.4）：

| 配置 | 结果 |
|---|---|
| `PublishAot=true` + `IncludeNativeLibrariesForSelfExtract=true`（无 `PublishSingleFile`） | 产物 `AotGui.exe 16.9 MB` + `av_libglesv2.dll` + `libHarfBuzzSharp.dll` + `libSkiaSharp.dll` 三件套 |
| 再加 `PublishSingleFile=true` + `SelfContained=true` | **完全一样**：三件套仍然散落在输出目录 |

上游证实：dotnet/runtime Discussion #117986（2025-07-23）标题即 *"NativeAOT + PublishSingleFile + IncludeNativeLibrariesForSelfExtract"*，正文：*"these properties don't 'work' (it leaves native DLLs next to the EXE)"*，且**去掉 `PublishAot` 就真能单文件**。→ 你选的"方案 B（SDK 支持的原生库自解压）"**在 NativeAOT 下不存在**，必须自研或改形态（见 §5）。

### 1.4 Avalonia + SkiaSharp 的 AOT 实测结论（利好）
空 Avalonia 12.1.4 桌面应用（含 `.axaml`、编译绑定 `x:DataType`、`FluentTheme`），AOT 发布：**0 warning / 0 error**，耗时 207.9 s，exe 16.9 MB。运行时自检输出：

```
[selftest] SkiaSharp 3.119.0.0 runtime=10.0.12
[selftest] png bytes=195
[selftest] gif encode supported=False size=-1
[selftest] gray8 ok=True
[selftest] RESULT=OK
```

即：AOT 下 SkiaSharp 的 `SKBitmap`/`SKCanvas`/PNG 编码/`Gray8` 全部可用；**GIF 编码不可用**（`Encode(SKEncodedImageFormat.Gif)` 返回 null，与 upstream issue #1130 一致）。

---

## 2. 事实核查表（v2）

| # | 项 | 结论 | 依据 |
|---|---|---|---|
| 1 | 仓库结构/框架 | `RePKG.Core`(netstandard2.0)、`RePKG.Application`(netstandard2.0)、`RePKG`(CLI, net472)、`RePKG.Tests`(net472, NUnit) | csproj |
| 2 | 大文件根因 | **确认**：整包条目全量入内存 | `PackageReader.cs:11,31,54-61` |
| 3 | `RePKG.Neo` | GitHub 无此仓库；引用代码 = upstream master 同文件同行 | GitHub Search + 逐行比对 |
| 4 | 其它全量载入点 | `Extract.cs:107,161,191,278,285`；`TexReader.cs` `new MemoryStream(bytes)` | 源码 |
| 5 | 32 位偏移 | `Offset/Length` int32、`dataStart` `(int)` → 数据区上限约 2 GiB（有符号）/4 GiB（按 uint32） | `PackageEntry.cs`、`PackageReader.cs:25,47-48`、`PackageWriter.cs` |
| 6 | TEX 硬上限 | 单 mipmap **250 MB**、mipmap 32、帧 100k、图 100 | `RePKG.Core/Constants.cs` |
| 7 | .NET 10 | LTS；本机 SDK **10.0.401**、runtime **10.0.12** | `dotnet --list-sdks` |
| 8 | Avalonia 12 | 12.1.4，MIT，含 `net10.0` 依赖组；依赖 **SkiaSharp 3.119.4** + HarfBuzzSharp 8.3.1.3（均 MIT/BSD 系） | nuspec 实读 |
| 9 | Avalonia AOT | 官方支持；要求 XAML 编译期、`AvaloniaResource`、避免反射 DI；本项目实测 0 警告 | docs + §1.4 |
| 10 | 严格单文件 AOT | **SDK 不支持**（见 §1.3） | 本机实测 + runtime Discussion #117986 |
| 11 | NativeAOT 工具链 | **已具备**（见 §1.2） | vswhere JSON + link.exe + 实测发布成功 |
| 12 | ImageSharp 许可 | 2.1.13 = **Apache-2.0**（OSI 认可、permissive、**与 MIT 项目兼容**，只需保留其 LICENSE/NOTICE）；3.x/4.x 改 Six Labors Split License（非 OSI，商用有收入门槛） | NuGet nuspec |
| 13 | ImageSharp 的 AOT 现状 | 2.x 无 trim/AOT 注解；`Image.LoadPixelData<T>` 走泛型反射路径；官方建议 3.x 才逐步完善 → 与我们 AOT 目标冲突 | 文档/已知限制 |
| 14 | SkiaSharp 许可 | MIT（包元数据 `license type="expression">MIT`，`requireLicenseAcceptance=true`）；底层 Skia 为 BSD-3-Clause，均 permissive | nuspec |
| 15 | SkiaSharp 是否新增原生载荷 | **不新增**：`Avalonia.Skia` 已依赖 `SkiaSharp 3.119.4` + `SkiaSharp.NativeAssets.Win32` | nuspec |
| 16 | SkiaSharp 能否编码 GIF | **不能**（返回 null）；issue #1130 | 本机实测 + issue |
| 17 | SkiaSharp 的 trim 风险 | 核心 SkiaSharp 实测 0 警告；已知开放 issue #3327 是 `SkiaSharp.Views.WinUI`（WinUI/WinRT 激活工厂）路径，**与本项目无关** | 实测 + issue #3327 |
| 18 | `System.Drawing.Common` | 全仓零引用，Windows-only → 删 | grep |
| 19 | Newtonsoft.Json | 用于 `dynamic json = ...`（AOT 不可用）、`TexJsonInfoGenerator`、`Info.cs`、`Helper.cs` | 源码 |
| 20 | CommandLineParser | 2.9.1，反射式 → AOT 不安全 | 已知 |
| 21 | CLI 语义 | `-o/--output`、`-i/--ignoreexts`、`-e/--onlyexts`（逗号分隔、自动补 `.`、`-i` 优先）、`-t/--tex`、`-s/--singledir`、`-r/--recursive`、`-c/--copyproject`、`-n/--usename`、`--no-tex-convert`、`--overwrite`、必填 `Input`；非递归只扫一层子目录 `*.pkg` | `Extract.cs` + README |
| 22 | 取消/退出 | `Console.CancelKeyPress`→`Program.Closing`，循环内 `Environment.Exit(0)`，无清理、无退出码 | `Extract.cs`/`Program.cs` |
| 23 | 原版发布 | `Publish.bat`：net472 + ilrepack，仅 Windows | `Publish.bat` |
| 24 | 静默截断 | `BinaryReader.ReadBytes` 到流尾不抛异常、返回短数组 → 截断 pkg 会"成功"出短文件 | .NET 行为 |

---

## 3. 根因链条（三级放大）

1. **一级**：`ReadFrom` → `PopulateEntriesWithData` → 每条目 `ReadBytes(Length)`，内存 ≈ 数据区体积。提取 10 KB 图也要读满 8 GB 包。
2. **二级（TEX 放大 3–5×）**：`LoadTex(entry.Bytes)` → LZ4 解压 → DXT → RGBA → 位图 → PNG 字节；全部 >85 KB 进 LOH。GIF 路径更糟：`ConvertToGif` 把**每一帧都 `Clone()` 成独立 `Image<Rgba32>`** 常驻（`TexToImageConverter.cs:108-141`）→ 内存 ≈ 帧数 × W×H×4。
3. **三级（静默截断）**：见 #24 → 用户看到"提取失败/花屏"而非明确错误。

**修复口径：一级必须彻底移除 `ReadBytes(entry.Length)`；二级在 GIF 路径改为逐帧流式编码；三级改为显式报错 + 失败隔离。**

---

## 4. 验收口径（已定）

| 决策 | 结论 |
|---|---|
| 偏移宽度 | 按 **uint32** 解释、内部一律 `long`；写回仍写 32 位 → 支持到 4 GiB 数据区，字节级兼容原版 |
| ≥1 GB 单条目 | 用**非 TEX 条目**（`.mp4`/`.bin`）或 `--no-tex-convert` 原始导出验收；TEX 转换路径另设 ≤200 MB 体量指标（受 250 MB mipmap 上限约束） |
| 内存断言 | **以 `GC.GetTotalAllocatedBytes(precise: true)` 为主**（条目提取全过程分配量应 ≈ 块大小 × 循环次数，与条目体积无关），工作集仅作辅助记录、不作断言主依据 |
| 取消语义 | 条目边界生效；清当前临时文件；**已完成条目保留** |

---

## 5. 发布策略（按实测修正）

| 方案 | 单文件 | AOT | 额外工程量 | 风险 |
|---|---|---|---|---|
| **B′ 自研原生库内嵌 + 首启解压** | ✅ 真单文件 | ✅ | 把 3 个原生库作为 `EmbeddedResource` 打入，启动时解压到 `%TEMP%\RePKG-Enhanced\native\<ver>\` 并用 `NativeLibrary.SetDllImportResolver` 挂钩（Win/Linux ~150–250 行） | 中低。**macOS 有坑**：硬化运行时 + 库验证会拒绝加载解压出的 dylib，需 `disable-library-validation` 或对解压库签名/公证 |
| **B″ AOT 文件夹分发（zip）** | ❌ exe+3 dll | ✅ | 0 | 低；与你"单文件"要求不符 |
| **C′ 非 AOT 单文件** | ✅（`IncludeNativeLibrariesForSelfExtract` 真生效） | ❌ | 0 | 低；60–80 MB、冷启动慢、无 AOT 收益 |
| **A′ 三方静态链接** | ✅ | ✅ | 依赖 `StaticLink.Avalonia` | 高（版本/许可/升级断链），不建议 |

**建议**：Windows/Linux 走 **B′**；macOS 单独评估（要么 B″ 的 `.app` 包形态，要么 B′ + 关闭库验证）。此项仍需你确认（Q1′）。

---

## 6. 图像栈：ImageSharp → SkiaSharp 的复核

### 6.1 许可事实（你的前提需要更正）
Apache-2.0 **不是** copyleft，也不阻止 MIT 项目依赖它：MIT 项目可以分发依赖 Apache-2.0 库的二进制，义务仅是保留该库的 LICENSE/NOTICE 与变更声明。所以"MIT 项目不能用 ImageSharp 2.1.13"在法律上不成立（这也是大量 MIT 项目的现实做法）。

### 6.2 但仍然应该换掉（技术理由，与许可无关）
1. **AOT/trim**：ImageSharp 2.x 无 trim 注解，`Image.LoadPixelData<T>`/`Image<TPixel>` 走泛型路径，AOT 下需大量 root 声明；SkiaSharp 核心实测 0 警告。
2. **原生载荷不增加**：Avalonia.Skia 本来就带 `SkiaSharp 3.119.4` + Win32/Linux 原生库；再引 ImageSharp 是**纯新增** ~3–5 MB 与第二套编解码器。
3. **内存**：ImageSharp 的动画 GIF 路径必须把全部帧物化；我们的目标是"峰值与体积脱钩"，Skia + 自研流式编码器更契合。
4. **维护**：ImageSharp 2.x 已停更（2.1.13 是最后 2.x），继续用等于锁死在一个不再修的版本。

### 6.3 替换映射（已核对到具体 API）

| 原实现 | SkiaSharp 对应 | 备注 |
|---|---|---|
| `Image.LoadPixelData<Rgba32>(bytes,w,h)` | `SKBitmap` + `SKColorType.Rgba8888`，`InstallPixels`/`copy` | 直接 |
| `Image.LoadPixelData<L8>` | `SKColorType.Gray8` | 实测可用 |
| `Image.LoadPixelData<RG88>` | **无 2 通道类型** → 手工展开为 `Rgba8888`：`RGB = G`，`A = R` | 依据 `RG88.cs:40`：`new Rgba32(s.G, s.G, s.G, s.R)`（即灰度=G、透明度=R） |
| `image.Mutate(x => x.Crop(w,h))` | 新建目标 `SKBitmap` + `SKCanvas.DrawBitmap(src, srcRect, dstRect)` | 直接 |
| `image.SaveAsPng(ms)` | `SKImage.Encode(SKEncodedImageFormat.Png, quality)` | 实测 bytes=195 ✅ |
| `image.SaveAsGif(ms, GifEncoder{Local})` | **无** → 自研 GIF89a 编码器 | 见 §6.4 |
| `frame.Clone(ctx => ctx.Crop(...).Rotate(angle))` | `SKCanvas` 变换（`RotateDegrees` + `DrawBitmap` 到透明底） | 直接 |
| GIF 帧延迟 `GifFormatMetadata.FrameDelay` | 自研编码器的 GCE delay 字段 | — |

### 6.4 动画 GIF 的缺口与方案
Skia 只能解码 GIF、不能编码（实测 + #1130）。三种处理：

| 方案 | 说明 | 影响 |
|---|---|---|
| **G1 自研 GIF89a 编码器**（建议） | `RePKG.Application/Texture/Gif/GifWriter.cs`：LZW + 每帧局部调色板（中位切分量化到 256 色）+ GCE 延迟/循环/透明索引；**逐帧写入输出流，不物化全部帧** | +250–350 行 MIT 自有代码；顺带修掉二级内存放大；可用 SkiaSharp 解码自家 GIF 做像素级回归（自带测试 oracle） |
| G2 放弃动画 GIF | 只导出 PNG（静态首帧）或 PNG 序列 | 功能回退，原版支持者会感知 |
| G3 引第三方 MIT GIF 库 | 需逐个核实许可与 AOT 兼容 | 与"轻量、少依赖"冲突 |

---

## 7. 依赖处置清单（v2）

| 依赖 | 版本 | 处置 | 理由 |
|---|---|---|---|
| `SkiaSharp` | **3.119.4** | **新增（实际零新增载荷）** | Avalonia.Skia 已依赖；MIT；AOT 实测 0 警告、PNG/Gray8 可用 |
| `SkiaSharp.NativeAssets.Win32`（及 Linux/macOS 对应包） | 3.119.4 | 随 SkiaSharp 引入 | 平台原生库 |
| `SixLabors.ImageSharp` | 2.1.9 | **移除** | 见 §6.2（AOT、载荷、内存、停更） |
| `System.Drawing.Common` | 9.0.0 | **移除** | 零引用 + Windows-only |
| `Newtonsoft.Json` | 13.0.3 | **替换**为 `System.Text.Json`（源生成上下文） | `dynamic` 在 AOT 不可用 |
| `CommandLineParser` | 2.9.1 | **替换**为手写解析器 | 反射式，AOT 不安全；参数集小 |
| `K4os.Compression.LZ4` | 1.3.8 | **保留** | 纯托管、TEX LZ4 必需 |
| `CommunityToolkit.Mvvm` | 8.4.2 | **新增（GUI）** | 源生成、AOT 友好 |
| `Avalonia` / `Avalonia.Desktop` / `Avalonia.Themes.Fluent` | 12.1.4 | **新增** | MIT、net10.0、AOT 实测通过 |
| 测试栈 | NUnit 3.14(net472) | **改** `xunit.v3` + `Microsoft.NET.Test.Sdk`（net10.0） | 需 `GC.GetTotalAllocatedBytes`；需 AOT 无关的现代测试宿主 |

> Core/Application 保留 `netstandard2.0`（最小改动；SkiaSharp/LZ4 均支持 netstandard2.0）；两个入口与测试项目用 `net10.0`。

---

## 8. 测试策略（阶段 2 门禁如何判定）

1. **合成 pkg 生成器**：用原版 `PackageWriter` 造包（可控条目数/体积），保存生成种子，确保可重复。
2. **1 GB 单条目用例**：`bin` 条目（`--no-tex-convert`）→ 提取 → SHA-256 与源数据比对（逐字节一致）；断言 `GC.GetTotalAllocatedBytes` 增量 ≤ 常量级（如 < 64 MB），**不看工作集**。
3. **2 GB 总数据用例**：多条目大包（需 Q2 的 uint32 决策，已定）。
4. **截断包用例**：条目声明长度 > 实际 → 断言提取显式失败、错误被记录、不产出半成品。
5. **批量用例**：目录内混合正常/损坏 pkg → 断言其余全部成功、失败计数正确、CLI 退出码非零。
6. **失败隔离 + 取消**：注入写盘失败/取消信号，断言条目边界生效、临时文件被清、目标文件不存在。
7. **GIF 回归**：自研编码器输出 → SkiaSharp 解码 → 逐帧像素/延迟比对。
8. **磁盘**：测试产物写 `D:\`/`E:\`（各剩 48/60 GB），用后清理；`C:` 仅 NuGet 缓存。

> **缺样本风险**：本机无任何真实 WE `.pkg`/`.tex`。全部回归只能靠合成样本，真实兼容性存在盲区（尤其 GIF/视频纹理分支）。见 Q9。

---

## 9. 决策记录（全部已闭环）

| 编号 | 决策 | 备注 |
|---|---|---|
| Q1 | ~~单文件 NativeAOT + 原生库自解压（方案 B）~~ → 因 SDK 不支持（§1.3）作废 | 由 Q1′ 取代 |
| **Q1′** | **平台差异化发布**：Windows / Linux → 单文件 AOT + 自研原生库内嵌首启解压（B′）；macOS → `.app` 包（内含 AOT 可执行 + 原生库） | 阶段 6 落地 |
| Q2 | 偏移按 **uint32** 解释，内部一律 `long` | 见阶段 1 的 D2 |
| Q3 | ≥1 GB 验收用**非 TEX 条目** + `--no-tex-convert` 原始导出 | TEX mipmap 上限 250 MB |
| Q4 | VS 工具链：**无需安装任何组件**（已具备 NativeDesktop + VC.Tools + SDK 26100 + ATL，均在 `D:\vside`） | §1.2 更正 |
| Q5 | 测试素材：合成 pkg（可重复 seed） | — |
| Q6 | 取消后**保留已完成条目**，仅清理当前临时文件 | — |
| Q7 | CLI **兼容核心参数**，去掉 `interactive` 子模式 | 差异清单见阶段 4 |
| Q8 | 动画 GIF：**G1 自研 GIF89a 编码器**（LZW + 中位切分量化 + 逐帧写流） | §6.4 |
| Q9 | **无真实样本**，全部基于合成样本，报告中标注盲区 | — |
| Q10 | 图像栈：**SkiaSharp 3.119.4 取代 ImageSharp**（MIT；Avalonia 已依赖；AOT 实测通过） | §6.1–6.3 |
| Q11 | 大文件测试以 **`GC.GetTotalAllocatedBytes(precise: true)` 增量**为主断言，工作集仅作辅助参考 | §4 |

---

## 10. 遗留风险（不阻塞，进入阶段 6 前需复核）

- **B′ 自研解压的启动成本**：首次启动需解压约 18 MB 原生库（`av_libglesv2.dll` 5.3 MB + `libSkiaSharp.dll` 11.4 MB + `libHarfBuzzSharp.dll` 1.8 MB）；计划解压到 `%LOCALAPPDATA%\RePKG-Enhanced\native\<hash>\` 并加锁防并发，二次启动零成本。若你更希望"零解压、纯文件夹分发"，阶段 6 前可切回 B″。
- **macOS 公证与 Gatekeeper**：未签名 `.app` 在他人机器上会被拦截；本项目不购买证书时只能提供"右键打开 / `xattr -d com.apple.quarantine`"说明。
- **无真实样本的兼容盲区**：GIF/视频纹理分支只有合成样本覆盖，真实文件首次接触时可能需要一轮修正。
- **`ReadStringI32Size` 的 255 截断**：已在阶段 1 计划中改为"超长即抛出/告警"，但这会改变上游对畸形包的容错行为（原本静默截断后继续解析）。

---

## 11. 附：本轮实测命令与证据

```text
vswhere -all -format json -include packages
  → installationPath=D:\vside, installationVersion=18.11.12306.390
  → 含 Microsoft.VisualStudio.Workload.NativeDesktop / VC.Tools.x86.x64 / Windows11SDK.26100

HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots
  → KitsRoot10 = D:\Windows Kits\10\
  → D:\Windows Kits\10\Lib\10.0.26100.0\um\x64\kernel32.lib 存在

# 1) 最小 AOT 冒烟
dotnet new console; PublishAot=true; dotnet publish -c Release -r win-x64
  → exit=0, 91.5s, aotprobe.exe 911.5 KB

# 2) Avalonia 12.1.4 + SkiaSharp 3.119.4 AOT
dotnet publish -c Release -r win-x64
  → exit=0, 207.9s, 0 warnings, AotGui.exe 16.9 MB + av_libglesv2.dll/
    libHarfBuzzSharp.dll/libSkiaSharp.dll

# 3) 运行时自检（AOT 产物）
.\AotGui.exe selftest
  → SkiaSharp 3.119.0.0 / png bytes=195 / gif encode supported=False / gray8 ok=True / RESULT=OK

# 4) 单文件尝试
+ <PublishSingleFile>true</PublishSingleFile> <SelfContained>true</SelfContained>
  → exit=0, 142.1s, 产物与 (2) 完全相同（三件套仍在外）
```

*本文件为阶段 0 交付物，未包含任何源码。*

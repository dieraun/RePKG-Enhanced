# 阶段 1：目录树与文件清单（改动性质标注）

> 基线：`notscuffed/RePKG@8005eb2`（MIT）。本阶段**不产出源码**，只提交结构、文件清单、改动性质与公共 API 形状，待你确认后进入阶段 2。
> 标注：`·` 原样保留（仅移动/改 TFM）｜`~` 修改｜`+` 新增｜`-` 删除

---

## 0. 两个需要你点头的结构决策

### D1. 目标框架（TFM）

| 项目 | TFM | 理由 |
|---|---|---|
| `RePKG.Core` | **netstandard2.0**（保持） | 纯数据模型 + 枚举 + 接口，无 IO 依赖；保持最小改动，任何 TFM 都能消费 |
| `RePKG.Application` | **net10.0**（由 netstandard2.0 上移） | 需要 `Span<byte>`（DXT 解压、GIF LZW、量化）、`BinaryPrimitives`、`NativeLibrary`、AOT 注解。仅被 net10.0 入口消费，上移无副作用 |
| `RePKG.Shared` | **net10.0** | 需要 `NativeLibrary.SetDllImportResolver`（netstandard 不可用）、`System.Threading.Channels` |
| `RePKG.Cli` / `RePKG.Gui` / 测试 | **net10.0** | 入口 |

### D2. `RePKG.Core.Package.PackageEntry.Offset/Length` 由 `int` → `long`

- 原因：Q2 已决定按 **uint32** 解释偏移（支持到 4 GiB 数据区），`int` 无法表示 >2 GiB。
- 影响面：`PackageReader`（读）、`PackageWriter`（写 `(uint)`）、`Extract.cs`（用 `long` 算 seek），以及 GUI 显示的体积。Core 内部其余代码不受影响。
- 这是对上游公开 API 的破坏性变更；本项目为新仓库、上游已停更，接受。
- 若你坚持不改 Core 类型，替代方案是 Core 保留 `int`、在 Application 里到处 `(uint)entry.Offset` + 显式 `long` 运算——更易出错，不推荐。

---

## 1. 目录树

```text
D:\BetterRePKG\
├─ LICENSE                                   + 本项目 MIT
├─ LICENSE-RePKG                             + 原版 MIT 全文（保留，不修改）
├─ THIRD-PARTY-NOTICES.txt                   ~ 移除 ImageSharp/System.Drawing；加入 SkiaSharp(MIT)/Skia(BSD-3)/Avalonia(MIT)/LZ4(MIT)/CommunityToolkit(MIT)
├─ README.md                                 + 英文说明（仓库首页）：用法、构建、发布、改动说明
├─ README.zh-CN.md                           + 中文说明（与英文版一一对应，顶部互相链接）
├─ Directory.Build.props                     + 公共编译属性（LangVersion、Nullable、InvariantGlobalization、AOT/trim 默认、确定性构建）
├─ Directory.Packages.props                  + 中央包版本管理（锁版本）
├─ global.json                               + 固定 SDK 10.0.4xx（rollForward: latestFeature）
├─ RePKG-Enhanced.sln                        + 新解决方案
├─ .gitignore  .gitattributes                · 沿用并补充（bin/obj/artifacts/.tmp）
│
├─ docs\
│  ├─ 00-stage0-fact-check.md                + 阶段 0 报告（已交付，随决策更新）
│  ├─ 01-file-inventory.md                   + 本文件
│  └─ DECISIONS.md                           + 决策记录（ADR：Q1′~Q9 及后续）
│
├─ src\
│  ├─ RePKG.Core\                            ← 原版内核，最小改动（TFM: netstandard2.0）
│  │  ├─ RePKG.Core.csproj                   ~ 加 IsTrimmable/IsAotCompatible；其余不动
│  │  ├─ Package\Enums\EntryType.cs          ·
│  │  ├─ Package\Interfaces\IPackageReader.cs      ~ 移除 `ReadEntryBytes` 语义（改为“仅读条目表”的单一职责）
│  │  ├─ Package\Interfaces\IPackageWriter.cs      ·（签名随 PackageEntry 类型同步）
│  │  ├─ Package\Package.cs                  ·
│  │  ├─ Package\PackageEntry.cs             ~ Offset/Length: int → long（D2）
│  │  ├─ Package\PackageEntryTypeGetter.cs   ·
│  │  ├─ Texture\Enums\*.cs (6)              ·
│  │  ├─ Texture\Helpers\*.cs (3)            ·
│  │  ├─ Texture\Interfaces\*.cs (13)        ·
│  │  ├─ Texture\Interfaces\Data\*.cs (7)    ·
│  │  └─ Texture\*.cs (7)                    ·
│  │
│  ├─ RePKG.Application\                     ← 解析内核实现（TFM: net10.0）
│  │  ├─ RePKG.Application.csproj            ~ TFM；去 ImageSharp/System.Drawing/Newtonsoft；加 SkiaSharp
│  │  ├─ Constants.cs                        ~ 新增流式常量（ChunkSize=1MiB 等）；mipmap 上限维持 250 MB
│  │  ├─ Extensions.cs                       ~ `ReadStringI32Size` 截断改为“超长即抛/告警”，不再静默截断；`WriteStringI32Size` 写**字节数**而非字符数
│  │  ├─ Exceptions\*.cs (3)                 ·
│  │  ├─ Package\PackageReader.cs            **核心改造** 仅读条目表；删除 PopulateEntriesWithData
│  │  ├─ Package\PackageWriter.cs            ~ long → uint32 写回
│  │  ├─ Package\PackageEntryStream.cs       + 单条目只读子流（限长 + 单句柄 seek）
│  │  ├─ Package\PackageFileSource.cs        + 以 FileShare.Read 持有唯一读句柄，按条目产出流
│  │  ├─ Package\EntryExtractor.cs           + 流式复制（分块/取消/进度/原子写回调）
│  │  ├─ Texture\TexReader.cs                ~ 支持从 Stream 读；仍强制 250 MB 单 mipmap 上限
│  │  ├─ Texture\TexHeaderReader.cs          ·
│  │  ├─ Texture\TexImageContainerReader.cs  ·
│  │  ├─ Texture\TexImageReader.cs           ~ 跳过 mipmap 时零分配；截断即抛，不静默
│  │  ├─ Texture\TexFrameInfoContainerReader.cs ·
│  │  ├─ Texture\TexMipmapDecompressor.cs    ·
│  │  ├─ Texture\Helpers\DXT.cs              ·
│  │  ├─ Texture\Helpers\RG88.cs             - 删除（改由 Skia 展开，见 stage0 §6.3）
│  │  ├─ Texture\TexToImageConverter.cs      **重写** ImageSharp → SkiaSharp（PNG/Gray8/RGBA/RG88 展开/裁剪/旋转）
│  │  ├─ Texture\Gif\GifWriter.cs            + GIF89a 编码（LZW、GCE 延迟/循环/透明、局部色表、逐帧写流）
│  │  ├─ Texture\Gif\ColorQuantizer.cs       + 中位切分 → ≤256 色（含透明索引保留）
│  │  ├─ Texture\Gif\GifFrameEnumerator.cs   + 逐帧产出（裁剪/旋转/延迟），不物化全部帧
│  │  ├─ Texture\TexJsonInfoGenerator.cs     ~ Newtonsoft → System.Text.Json（源生成）
│  │  └─ Texture\Writer\*.cs (6)             ·（仅被测试/未来 repack 使用）
│  │
│  ├─ RePKG.Shared\                          ← 队列 / 流式 IO / 路径 / 日志 / 原生库引导（TFM: net10.0，新增）
│  │  ├─ RePKG.Shared.csproj                 +
│  │  ├─ IO\StreamPump.cs                    + 定长分块复制（1 MiB）、取消、进度回调
│  │  ├─ IO\AtomicFileWriter.cs              + 同目录临时文件 + 原子替换；中断清理
│  │  ├─ IO\LengthLimitedStream.cs           + 只读、限长、可 seek 的子流（单句柄共享）
│  │  ├─ Paths\SafePath.cs                   + 路径穿越防御（`..`/绝对路径/盘符/UNC/符号链接逃逸）
│  │  ├─ Paths\FileNameSanitizer.cs          + 非法字符/保留名/长度处理
│  │  ├─ Catalog\PackageCatalog.cs           + 目录递归扫描 + `*.pkg` 枚举 + 扩展名过滤（复用原版语义）
│  │  ├─ Catalog\PackageEntryFilter.cs       + onlyexts/ignoreexts 语义（`-i` 优先、OrdinalIgnoreCase、自动补 `.`）
│  │  ├─ Export\ExportQueue.cs               + 有界队列、并发上限（默认 1–2）、暂停/取消、失败隔离、计数
│  │  ├─ Export\ExportJob.cs                 + 单文件导出任务（pkg → 条目 → 输出）
│  │  ├─ Export\ExportOptions.cs             + 输出目录、覆盖策略、no-tex-convert、singledir 等
│  │  ├─ Export\ExportResult.cs              + 成功/失败/跳过计数 + 明细
│  │  ├─ Export\ExportProgress.cs            + 进度快照（文件/条目/字节/速度）
│  │  ├─ Logging\IExportLog.cs               +
│  │  ├─ Logging\JsonlExportLog.cs           + JSONL 追加写（崩溃安全）
│  │  └─ Native\NativeBootstrap.cs           + 内嵌原生库首启解压 + `SetDllImportResolver`（Win/Linux 单文件）
│  │
│  ├─ RePKG.Cli\                             ← 命令行入口（TFM: net10.0，AOT）
│  │  ├─ RePKG.Cli.csproj                    + AOT/单文件属性；EmbeddedResource 原生库（按 RID）
│  │  ├─ Program.cs                          + 入口、Ctrl+C、退出码
│  │  ├─ Cli\CliParser.cs                    + 手写解析（无反射，AOT 安全）
│  │  ├─ Cli\ExtractOptions.cs / InfoOptions.cs +
│  │  ├─ Commands\ExtractCommand.cs          + 建 Catalog → ExportQueue → 跑完 → 汇总
│  │  ├─ Commands\InfoCommand.cs             + 输出包清单（JSON / 表格）
│  │  ├─ Commands\ListCommand.cs             + 列出条目（可过滤）
│  │  └─ Output\ConsoleReporter.cs           + 进度/失败摘要/退出码映射
│  │
│  └─ RePKG.Gui\                             ← Avalonia 12 入口（TFM: net10.0，AOT）
│     ├─ RePKG.Gui.csproj                    + Avalonia 12.1.4；AOT/单文件；EmbeddedResource 原生库；AvaloniaResource
│     ├─ Program.cs                          + NativeBootstrap → BuildAvaloniaApp
│     ├─ App.axaml(.cs)                      + 主题跟随系统（FluentTheme + ThemeVariant 跟随）
│     ├─ Views\MainWindow.axaml(.cs)         + 三段式：包列表 / 条目表 / 队列面板
│     ├─ Views\EntryPreviewView.axaml(.cs)   + 图片/TEX/文本预览
│     ├─ Views\ExportQueueView.axaml(.cs)    + 暂停/取消/失败明细
│     ├─ Views\SettingsView.axaml(.cs)       + 输出目录、并发、覆盖策略
│     ├─ ViewModels\MainWindowViewModel.cs   + 编排（复用 Shared.ExportQueue）
│     ├─ ViewModels\PackageItemViewModel.cs  + 三态勾选
│     ├─ ViewModels\EntryItemViewModel.cs    + 条目行（类型/大小/状态）
│     ├─ ViewModels\QueueItemViewModel.cs    +
│     ├─ Services\FilePickerService.cs       + 文件/目录选择
│     ├─ Services\DialogService.cs           + 确认/错误弹窗
│     ├─ Services\PreviewService.cs          + 缩略图生成（Skia），限流/缓存上限
│     ├─ Assets\*.axaml / Icons            + 主题资源（无动画）
│     └─ app.manifest                        + DPI/长路径感知（Windows）
│
└─ tests\
   ├─ RePKG.Tests\                           ← 移植原版单测 + 新单测（TFM: net10.0，xunit.v3）
   │  ├─ RePKG.Tests.csproj                  + 
   │  ├─ Package\PkgWriterTests.cs           ~ 由 PkgWriterTests.cs 移植（NUnit → xunit）
   │  ├─ Texture\TexDecompressingTests.cs    ~ 移植
   │  ├─ Texture\TexWriterTests.cs           ~ 移植
   │  ├─ TestHelper.cs                       ~ 移植（样本生成）
   │  ├─ Texture\SkiaTexConvertTests.cs      + PNG/Gray8/RG88/裁剪
   │  ├─ Texture\GifWriterTests.cs           + 编码→Skia 解码→像素/延迟比对
   │  ├─ Package\PackageReaderTests.cs       + 条目表零数据读取断言
   │  └─ Package\TruncatedPackageTests.cs    + 截断包必须显式失败
   ├─ RePKG.Tests.Shared\                    ← 队列/IO/路径/日志单测
   │  ├─ StreamPumpTests.cs / AtomicFileWriterTests.cs
   │  ├─ SafePathTests.cs（穿越用例全表）
   │  ├─ ExportQueueTests.cs（并发上限、失败隔离、暂停/取消、有界）
   │  └─ PackageEntryFilterTests.cs（与原版语义对齐）
   ├─ RePKG.Tests.BigFiles\                  ← 门禁用（默认 Skip，`REPKG_BIG_TESTS=1` 启用）
   │  ├─ SyntheticPkgFactory.cs              + 造 1 GB/2 GB 包（可重复 seed）
   │  ├─ LargeEntryExtractionTests.cs        + 1 GB 条目 + 分配量断言 + SHA-256 比对
   │  └─ BatchExtractionTests.cs             + 多包批量内存断言 + 损坏包混入
   └─ RePKG.Tests.Cli\                       ← CLI 集成（进程内调用 + 真实进程退出码）
      └─ ExtractCommandTests.cs / ExitCodeTests.cs
```

---

## 2. 按改动性质的汇总

| 类别 | 数量 | 说明 |
|---|---|---|
| 原版原样保留（仅移动，`·`） | Core 43 个 + Application 15 个 | 主要是数据模型、枚举、接口、DXT/LZ4 解压、TEX 写入器 |
| 原版修改（`~`） | Application 7 个 + Core 3 个 | `PackageReader`（核心）、`PackageWriter`、`Ext ensions.cs`、`TexReader`、`TexImageReader`、`TexJsonInfoGenerator`、`Extensions.cs`、`Constants.cs`；Core：`PackageEntry`、`IPackageReader`、csproj |
| 原版删除（`-`） | 1 个 | `Texture/Helpers/RG88.cs`（ImageSharp 像素类型） |
| 新增（`+`） | Application 6、Shared 18、Cli 8、Gui 14、测试 ~15、工程文件 4 | 见树 |
| 上游整体丢弃 | `Publish.bat`、`RePKG.sln(.DotSettings.user)`、`RePKG/Command/*`、`RePKG/Program.cs`、`RePKG/Helper.cs`、`RePKG/Extensions.cs`、`*.DotSettings*`、`*.csproj.user` | 由 `RePKG.Cli`/`RePKG.Shared` 重写替代；原 CLI 的 `Extract.cs`/`Info.cs` 逻辑**语义**迁移，代码不继承（net472 + CommandLineParser 结构不适用于 AOT） |

> 逐文件“基于 notscuffed/RePKG (MIT) 二次修改”头部注释只加在 `~` 与 `-` 涉及的原版文件上；`·` 文件保留原样（含原 header），在 `THIRD-PARTY-NOTICES.txt` 中统一声明来源。

---

## 3. 新增公共 API 形状（签名字级，无实现）

```csharp
// RePKG.Application.Package
public sealed record PackageTable(IReadOnlyList<PackageEntry> Entries, long DataStart, long HeaderSize);

public interface IPackageTableReader { PackageTable ReadTable(Stream stream); }

public sealed class PackageFileSource : IDisposable            // 单读句柄，FileShare.Read
{
    public static PackageFileSource Open(string path);
    public PackageTable Table { get; }
    public Stream OpenEntry(PackageEntry entry);               // 定长、只读、可 seek
}

public static class EntryExtractor
{
    public static long CopyEntry(PackageFileSource src, PackageEntry e, Stream dest,
        int bufferSize = 1 << 20, IProgress<long>? progress = null, CancellationToken ct = default);
}

// RePKG.Application.Texture
public interface ITexImageConverter
{
    ImageResult Convert(ITex tex);                             // PNG bytes / GIF bytes / mp4 bytes
    MipmapFormat GetConvertedFormat(ITex tex);
}

// RePKG.Application.Texture.Gif
public sealed class GifWriter : IDisposable
{
    public GifWriter(Stream output, int width, int height, int loopCount = 0, int backGroundIndex = 0);
    public void AddFrame(ReadOnlySpan<byte> rgba, int delayCentiseconds, int? transparentIndex = null);
    public void Dispose();                                      // 写 trailer
}

// RePKG.Shared.IO
public static class StreamPump
{
    public static long Copy(Stream src, Stream dst, long length, int bufferSize = 1 << 20,
        IProgress<long>? progress = null, CancellationToken ct = default);
}

public sealed class AtomicFileWriter : IDisposable             // tmp + 原子替换；Dispose 未提交则删除 tmp
{
    public AtomicFileWriter(string destination, bool overwrite);
    public Stream Stream { get; }
    public void Commit();
}

// RePKG.Shared.Paths
public static class SafePath
{
    public static bool TryResolve(string root, string entryFullPath, out string fullPath, out string? reason);
    public static string Resolve(string root, string entryFullPath);   // 失败抛 PathTraversalException
}

// RePKG.Shared.Export
public sealed class ExportQueue : IAsyncDisposable
{
    public ExportQueue(ExportOptions options, IExportLog log);
    public int MaxConcurrency { get; init; }                          // 默认 2
    public int MaxQueuedFiles { get; init; }                          // 默认 64（有界）
    public ValueTask EnqueueAsync(ExportJob job, CancellationToken ct);
    public void Pause(); public void Resume(); public void Cancel();
    public ExportResult RunAsync(CancellationToken ct);
    public IAsyncEnumerable<ExportProgress> Progress { get; }
}
```

---

## 4. 依赖图与构建顺序

```text
Core (netstandard2.0)
  └── Application (net10.0: SkiaSharp, LZ4, STJ)
        └── Shared (net10.0: Channels, SkiaSharp via Application)
              ├── Cli (net10.0, AOT)
              └── Gui (net10.0, Avalonia 12.1.4, AOT)

tests/* → 引用对应被测项目
```

**硬规则（阶段 2–5 持续校验）**：`Application` 不得引用 `Shared`/`Avalonia`；`Shared` 不得引用 `Avalonia`；`Cli` 与 `Gui` 只允许通过 `Shared.ExportQueue` 驱动导出，**不得各自实现条目循环**。

---

## 5. 阶段映射（每阶段只碰这些文件）

| 阶段 | 文件范围 |
|---|---|
| 2 | `Application/Package/*`（新 4 + 改 3）、`Core/Package/PackageEntry.cs`、`Core/Package/Interfaces/IPackageReader.cs`、`Application/Constants.cs`、`Application/Extensions.cs`、`tests/RePKG.Tests.BigFiles/*`、`tests/RePKG.Tests/Package/*` |
| 3 | `Shared/*`（IO/Paths/Export/Logging/Catalog/Native 全部）、`tests/RePKG.Tests.Shared/*` |
| 4 | `Application/Texture/*`（Skia 重写 + GIF）、`Cli/*`、`tests/RePKG.Tests/Texture/*`、`tests/RePKG.Tests.Cli/*` |
| 5 | `Gui/*` |
| 6 | 根工程文件、`.github/workflows/*`、macOS `.app` 打包脚本、`THIRD-PARTY-NOTICES.txt` |

---

## 6. 阶段 1 验收点

1. 上表每个 `~`/`-` 文件都能对应到阶段 0 的根因或决策（无“顺手重构”）。
2. 新增文件数量与阶段 2–5 的工作量一致，无占位文件。
3. 两条硬规则（无反向依赖、无重复条目循环）在阶段 2 的设计评审中可机械校验。
4. 你确认 D1（TFM 上移）与 D2（`Offset/Length` 改 `long`）。

**已知限制（阶段 1 结束时明确说明）**
- 尚未写任何源码与工程文件，`dotnet build` 无法运行；本阶段无可执行验收物。
- 无真实 `.pkg`/`.tex` 样本（Q9 已确认），兼容性回归全部依赖合成样本。
- macOS 形态（`.app`）的具体打包与公证流程留到阶段 6 设计。

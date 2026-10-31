# 阶段 4：TEX 转换链 + CLI（含批量 / 递归 / 过滤）

状态：**待验收**（等用户确认后改为「已验收」）

本阶段目标（`docs/01-file-inventory.md` 第 267 行）：`Application/Texture/*` 全量改造（SkiaSharp + 自研 GIF 编码器）、
新增 `RePKG.Cli`、补齐 TEX 与 CLI 测试，并让 CLI 与将来的 GUI 共用**同一个导出队列与同一条流式内核**。

---

## 1. 目标核对表

| 要求（m00002） | 落点 | 状态 |
|---|---|---|
| 移除 ImageSharp / Newtonsoft / System.Drawing | `Directory.Packages.props`、`RePKG.Application.csproj` | ✅ 三个依赖已从依赖图消失，`grep` 仅剩注释 |
| 图像编码换 SkiaSharp（MIT） | `Texture/TexToImageConverter.cs`、`Texture/Helpers/SkiaPixels.cs` | ✅ |
| TEX → PNG | `TexToImageConverter.WriteTo` → `SKImage.Encode(Png)` | ✅ |
| TEX → GIF（动画） | `Texture/Gif/{GifWriter,ColorQuantizer,GifFrameEnumerator}.cs` | ✅ 自研 GIF89a 编码器，逐帧编码不物化全部帧 |
| TEX → MP4（视频纹理） | `WriteVideoTexture` 校验 `ftypisom/ftypmsnv/ftypmp42` 后透传 | ✅ |
| TEX 元信息 JSON | `TexJsonInfoGenerator`（System.Text.Json，字段类型与原版一致） | ✅ |
| CLI 保留过滤/递归语义 | `Cli/CliParser.cs`、`Cli/InputResolver.cs` | ✅ `-e/-i/-s/-r/-n/-c/--no-tex-convert/--overwrite` 全支持 |
| 批量走同一队列与同一内核 | `Cli/Commands/ExtractCommand.cs` → `ExportQueue` | ✅ CLI 里没有一行条目循环 |
| 单条目失败隔离 + 退出码 | `ExportQueue` 记 `entry.failed`；`ExtractCommand` 汇总 | ✅ 坏条目/坏包不影响其余，退出码 1 |
| 取消在条目边界生效 | `ExportControl` + `Ctrl+C` → `Control.Cancel()` | ✅ 已写文件的条目保留（Q6） |
| 去掉 interactive 子模式 | — | ✅ 不实现（Q7） |

## 2. 逐文件改动

### 2.1 `RePKG.Application`（纹理栈）

| 文件 | 性质 | 说明 |
|---|---|---|
| `Texture/Helpers/RG88.cs` | **删除** | ImageSharp 的 `IPixel` 实现，随 ImageSharp 一起退役；语义（灰度=G、透明=R）搬进 `SkiaPixels` |
| `Texture/Helpers/SkiaPixels.cs` | **新增** | 原始 mipmap 字节 → `SKBitmap(Rgba8888, Unpremul)`；RGBA8888/R8/RG88 三种展开；截断显式抛 |
| `Texture/Gif/GifWriter.cs` | **新增** | GIF89a 写出器：LSD + NETSCAPE2.0 循环块 + 每帧 GCE/图像描述符/局部位图色表/LZW |
| `Texture/Gif/ColorQuantizer.cs` | **新增** | 5 bit/通道分桶 + 中位切分 → ≤255 色局部位图色表，支持 1 个透明索引 |
| `Texture/Gif/GifFrameEnumerator.cs` | **新增** | 按 `TEXS*` 帧表逐帧合成（复刻原版裁剪/旋转语义），同一时刻只持有一张 `SKBitmap` |
| `Texture/TexToImageConverter.cs` | **重写** | 从「返回 `byte[]`」改成 `MipmapFormat WriteTo(ITex, Stream)`，全链路可写流；保留 `ConvertToImage` 兼容壳 |
| `Texture/TexJsonInfoGenerator.cs` | **重写** | Newtonsoft → `Utf8JsonWriter`；保持原版字段类型（`clampuvs` 布尔、`nomip` 等字符串） |
| `Texture/TexImageReader.cs` | 修改 | `ReadBytes` 改循环读满（短读即显式抛），负数/超限前置校验；跳过路径零分配 |
| `Texture/TexReader.cs` | 修改 | 新增 `ReadFrom(Stream, bool leaveOpen)` 便捷重载 |
| `RePKG.Application.csproj` | 修改 | 依赖只剩 `K4os.Compression.LZ4` + `SkiaSharp`；`InternalsVisibleTo` 暴露给测试 |

### 2.2 `RePKG.Shared`（导出管线扩展）

| 文件 | 性质 | 说明 |
|---|---|---|
| `Export/IEntryConverter.cs` | **新增** | `CanConvert` / `Convert` + `ConversionResult(PrimaryPath, BytesWritten, Skipped)` |
| `Export/TexEntryConverter.cs` | **新增** | `.tex` → png/gif/mp4 + 同名 `.json`；原子写入；`ConvertFile` 供 `repkg tex` 用 |
| `Export/ExportOptions.cs` | 修改 | 新增 `WriteTexInfo`（默认 true） |
| `Export/ExportQueue.cs` | 修改 | 构造函数接受 `IEntryConverter`；条目循环新增「可转换」分支，其余仍是 `EntryExtractor.CopyEntry` 分块复制 |

### 2.3 `RePKG.Cli`（新增项目，`AssemblyName=repkg`）

| 文件 | 说明 |
|---|---|
| `Program.cs` | 进程入口，未处理异常 → 退出码 1 |
| `CliApp.cs` | `Run(string[], TextWriter, TextWriter)`：解析、分发、Ctrl+C → 取消、异常 → 退出码 |
| `Cli/ExitCode.cs` | `Success=0 / PartialFailure=1 / UsageError=2 / Canceled=130` |
| `Cli/CliOptions.cs` | 命令 + 全部选项 |
| `Cli/CliParser.cs` | 手写解析（无反射，AOT 安全）；`--name=value` 与 `--name value` 都支持 |
| `Cli/HelpText.cs` | 中文用法 + 退出码 + 「每包一个子目录」约定 |
| `Cli/ConsoleReporter.cs` | 120 ms 节流进度行、逐包结果、汇总 |
| `Cli/InputResolver.cs` | 文件/目录输入 → pkg 列表；非递归扫一层子目录（与原版一致） |
| `Cli/ProjectJson.cs` | 读 `project.json` 的 `title`（`-n`）、`-c` 时原子复制 |
| `Cli/Commands/ExtractCommand.cs` | 组装 `ExportOptions` + `TexEntryConverter` → `ExportQueue` → 汇总退出码 |
| `Cli/Commands/InfoCommand.cs` | 文本 / `--json` 两种清单输出 |
| `Cli/Commands/TexCommand.cs` | 独立 `.tex` 文件/目录转换 |

### 2.4 测试

| 文件 | 项数 | 覆盖 |
|---|---|---|
| `tests/RePKG.Tests/Texture/TexConversionTests.cs` | 13 | RGBA/R8/RG88 像素、裁剪到 `ImageWidth/Height`、视频透传与坏 magic、容器格式透传、压缩格式拒绝、格式映射 |
| `tests/RePKG.Tests/Texture/GifEncodingTests.cs` | 3 | 用 **SKCodec 独立解码**自研 GIF：帧数/延迟（厘秒）、透明像素、局部色表容量 |
| `tests/RePKG.Tests/Texture/TexJsonInfoTests.cs` | 5 | 字段类型、flags 反映、GIF 的 `spritesheetsequences`、缺帧容器报错 |
| `tests/RePKG.Tests/Texture/TexParsingTests.cs` | 5 | header/mipmap 解析、`ChunkedReadStream` 短读、截断、超大 `byteCount`、TEXS0003 |
| `tests/RePKG.Tests.Cli/ExtractCommandTests.cs` | 11 | 完整 CLI 链路（见 §4 实测） |
| `tests/RePKG.TestSupport/SyntheticTexFactory.cs` | — | 合成 `TEXV0005/TEXI0001 + TEXB0001 + TEXS0003`；`ChunkedReadStream` 短读流 |
| `tests/RePKG.TestSupport/SyntheticPkgFactory.cs` | — | 新增 `CreateFromFiles(path, IReadOnlyDictionary<string, byte[]>)` |

## 3. 关键实现决策

1. **转换器注入队列，而不是在 CLI 里写第二套循环**：`ExportQueue(options, log, IEntryConverter?)`。
   GUI 阶段只要 `new ExportQueue(options, log, new TexEntryConverter())`，与 CLI 完全同一条路径。
2. **跳过判定分两处**：非转换条目在队列层用 `File.Exists(target)` 判（便宜）；转换条目的目标扩展名要解析 TEX 头才知道（`.png` / `.gif` / `.mp4`），由转换器自己判并返回 `ConversionResult.Skip`。
3. **GIF 不物化全部帧**：`GifFrameEnumerator` 逐帧合成 → 量化 → 写 LZW → 立刻释放位图；内存 ≈ 单帧 RGBA + 索引 + 调色板，与动画帧数脱钩（原版 ImageSharp 路径是 帧数 × W × H × 4）。
4. **图片编码仍需要整帧在内存**：单张 mipmap 上限 250 MB（`Constants.MaximumMipmapByteCount`），转换路径的内存天花板由这个常量决定，而不是由 pkg 体积决定；≥1 GB 的大文件门禁因此只用非 TEX 条目（阶段 0 - Q3）。
5. **每包一个输出子目录**：`<输出根>/<pkg 名>`（`-n` 用 `project.json` 的 title），批量提取不会互相覆盖；title 经 `SafePath.TryResolve` 校验，非法则 `FileNameSanitizer.Flatten`。
6. **退出码**：`0` 全成功 / `1` 有失败项 / `2` 参数错误 / `130` 用户取消。批量里坏包只影响自己那一个 job（`FatalError`），汇总里 `Failed>0` → 1。
7. **不做 interactive 子模式**（Q7）：`--help` 里也不提。

## 4. 编译与测试命令（实测）

```powershell
# 编译
dotnet build RePKG-Enhanced.slnx -c Release

# 单元测试（三个工程）
dotnet test tests\RePKG.Tests        -c Release
dotnet test tests\RePKG.Tests.Shared -c Release
dotnet test tests\RePKG.Tests.Cli    -c Release

# 大文件门禁（阶段 2 的，回归用）
$env:REPKG_BIG_TESTS='1'; $env:REPKG_BIG_TMP='D:\BetterRePKG\.bigtmp'
dotnet test tests\RePKG.Tests.BigFiles -c Release
```

实测结果：

| 命令 | 结果 |
|---|---|
| `dotnet build RePKG-Enhanced.slnx -c Release` | Build succeeded，**0 error**，5 条 `xUnit1051`（仅测试项目，见 §6） |
| `dotnet test tests\RePKG.Tests` | **40 passed / 0 failed** |
| `dotnet test tests\RePKG.Tests.Shared` | **61 passed / 0 failed** |
| `dotnet test tests\RePKG.Tests.Cli` | **11 passed / 0 failed** |
| `dotnet test tests\RePKG.Tests.BigFiles`（门禁开） | 见 §4.2 |

### 4.1 端到端手工验证（真实进程，非进程内）

合成一个 8 MiB 的 pkg（`project.json` + `materials/quad.tex` + `scene/big.bin`），跑真实 exe：

```text
> repkg extract .bigtmp\demo -r -o .bigtmp\demo\out
共 1 个 pkg → D:\BetterRePKG\.bigtmp\demo\out（TEX 转 PNG/GIF/MP4）
[完成] demo.pkg → ...\out\demo  成功 3 / 跳过 0 / 失败 0 / 取消 0，8.0 MiB，0.6s
汇总：1 个包，成功 3 / 跳过 0 / 失败 0 / 取消 0，共 8.0 MiB，0.6s
exit=0
```

产物：

```text
demo\project.json          37
demo\materials\quad.png    98     ← TEX 转 PNG
demo\materials\quad.json  165     ← 纹理元信息
demo\scene\big.bin     8388608    ← 大条目，分块复制
demo\export-log.jsonl     863
```

`repkg info demo.pkg --json` 输出（节选）：

```json
"dataStart": 95,
"entryCount": 3,
"entries": [
  { "fullPath": "project.json",      "offset": 0,   "length": 37 },
  { "fullPath": "materials/quad.tex","offset": 37,  "length": 91 },
  { "fullPath": "scene/big.bin",     "offset": 128, "length": 8388608 }
]
```

一个**故意做坏的 TEX**（魔数没写 `\0`）在批量里的表现——条目级隔离、其余照常、退出码 1：

```json
{"level":"error","event":"entry.failed","entry":"materials/quad.tex",
 "message":"UnknownMagicException: Unknown magic: 'TEXV0005TEXI0001' in 'TexReader:Magic1'"}
{"level":"info","event":"entry.ok","entry":"scene/big.bin","bytes":8388608}
{"level":"info","event":"package.done","message":"ok=2 skipped=0 failed=1 canceled=0"}
```

### 4.2 大文件门禁回归

```text
Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2, Duration: 2 m 8 s - RePKG.Tests.BigFiles.dll
```

阶段 4 没有改动 `EntryExtractor` / `PackageFileSource`（转换器为 `null` 时走的是同一条复制分支），
分配增量仍是阶段 2 实测的 **1.0 MiB**（`ArrayPool` 首次租用 1 MiB 块），与条目体积（1 GiB / 2.2 GiB）无关。
这一轮只是回归确认「加了转换器分支后没有把整条目读回内存」。

## 5. 验收点对照

| 验收项（阶段 4 门禁） | 证据 | 结论 |
|---|---|---|
| 真实/合成 pkg 批量解包通过 | `ExtractCommandTests` 11 项 + §4.1 真实进程运行 | ✅ |
| 批量失败隔离 + 退出码 | `CorruptPackage_FailsButOthersSucceed`（退出码 1、好包照常）；§4.1 日志 | ✅ |
| 路径穿越拒绝 | `UnsafeEntryPath_IsSkippedAndDoesNotEscape` + 阶段 3 的 25 条 `SafePathTests` | ✅ |
| 过滤/递归语义 | `RecursiveDirectory_AppliesExtensionFilter`、`-e tex` | ✅ |
| 原子写入 | `TexEntryConverter` 走 `AtomicFileWriter`；阶段 3 的 8 项测试 | ✅ |
| CLI 与 GUI 共用同一队列/内核 | `ExtractCommand` 只有 `ExportQueue` 调用，无条目循环 | ✅（GUI 阶段 5 复用） |
| TEX→PNG/GIF/MP4/JSON | TEX 26 项测试 + `ConvertsAnimatedTexToGif`（SKCodec 解出 2 帧、90–110/190–210 ms） | ✅ |
| 无整条目 `ReadBytes` | `grep -r "ReadBytes(entry.Length)" src` 仅命中注释 | ✅ |

## 6. 已知限制

1. **TEX 图像路径仍需要整帧内存**：单张 mipmap ≤ 250 MB（上游常量），加上 Skia 位图与 PNG 缓冲，峰值约为 3–4 倍 mipmap 体积。≥1 GB 的条目只能原样导出（`--no-tex-convert`）。
2. **GIF 只支持 raw mipmap**（RGBA8888/R8/RG88）：压缩（DXT/LZ4）纹理走 `WriteGif` 会抛「Only raw mipmap formats are supported right now while converting gif」——与原版一致。
3. **量化是有损的**：5 bit/通道分桶 + 中位切分，最多 255 色 + 1 个透明索引；渐变会有轻微分层，这是 GIF 格式本身的上限。
4. **无真实 WE 样本**（阶段 0 - Q9）：所有 TEX 用例都是合成数据，真实素材的边界情况（奇怪的帧表、`TEXB0004` 的 `conditionJson`）未被真实样本验证过。
5. **`-s/--singledir` 的重名冲突**在队列层记 `entry.name-collision` 并跳过后者，不自动改名。
6. **测试项目 5 条 `xUnit1051` 告警**：`PackageStreamingTests.cs:240,263`、`LargeEntryExtractionTests.cs:70,139,145` 未显式传 `TestContext.Current.CancellationToken`。属测试卫生问题，未在解决方案层 `NoWarn`（保持告警可见）。
7. **`repkg info --json` 会把整张条目表读进内存**（只读元数据，不碰数据区）；对有百万条目的畸形包，`MaximumEntryCount=1_000_000` 是最后防线。
8. **取消粒度**是条目：正在复制的大条目会跑到当前块结束（1 MiB），不会立即中断。

## 7. 下一步（阶段 5：`RePKG.Gui`）

- Avalonia 12.1.4 + CommunityToolkit.Mvvm，`PublishAot`/`IsAotCompatible`/`BuiltInComInteropSupport=false`（阶段 0 已实测 0 warning 发布成功）。
- 复用 `ExportQueue`（含 `TexEntryConverter`）与 `SafePath`/`JsonlExportLog`，GUI 不新增第二条导出路径。
- 功能清单：单/批量加载、TEX/图片/文本预览、三态勾选、输出目录、暂停/取消、拖拽导入、明暗跟随系统。

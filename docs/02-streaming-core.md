# 阶段 2 交付：内核流式改造 + 大文件回归

> 状态：**门禁待验收**（峰值内存测试已通过，等用户确认）
> 日期：2026-10-31
> 前置：阶段 1 已确认（D1 TFM 上移、D2 `Offset/Length` 改 `long`）

---

## 1. 本阶段目标回顾

| 目标 | 结果 |
|---|---|
| 提取链路彻底移除 `ReadBytes(entry.Length)` | ✅ 已从 `RePKG.Application/Package` 全部删除（`grep` 见 §5） |
| 读条目表不碰数据区 | ✅ 只解析魔数 + 条目表，随后 `Seek(dataStart)` 返回 |
| 单条目提取分块流式复制 | ✅ `EntryExtractor.CopyEntry` 为全项目唯一实现，1 MiB 缓冲 |
| 峰值内存与条目体积脱钩 | ✅ 1 GiB 条目分配 **1.0 MiB**；2.2 GiB 总量分配 **1.0 MiB** |
| 逐字节一致、无截断 | ✅ 输出 SHA-256 与期望值完全一致 |
| 只读打开、不锁文件 | ✅ `FileAccess.Read` + `FileShare.Read` |
| 偏移按 uint32 解释、内部 `long` | ✅ 读写两侧均改；写回超 `uint.MaxValue` 显式抛错 |

---

## 2. 逐文件改动

### 2.1 `src/RePKG.Core`（1 个文件修改）

| 文件 | 性质 | 改动 |
|---|---|---|
| `Package\PackageEntry.cs` | 修改 | `Offset`/`Length` 由 `int` 改 `long`（配合 uint32 语义）；`Bytes` 保留但注释标明解析链路不再填充，仅写包路径使用 |

### 2.2 `src/RePKG.Application`（4 修改 + 3 新增）

| 文件 | 性质 | 改动 |
|---|---|---|
| `Package\PackageReader.cs` | **重写核心** | 删除 `PopulateEntriesWithData` 与 `ReadEntryBytes`；`ReadFrom` 只解析表并返回；新增 `MaximumEntryCount = 1_000_000` 与 `ValidateRanges`（越界抛 `InvalidDataException("Entry `…` (offset …, length …) exceeds the package body size of … bytes — the package is truncated or corrupt")`）；条目路径上限 255 → **1024** |
| `Package\PackageWriter.cs` | 修改 | 累积 `long currentOffset`，落盘写 `(uint)`；超 4 GiB 抛 `NotSupportedException` |
| `Extensions.cs` | 修改 | `ReadStringI32Size`：超上限/负数**抛错**而非静默截断，可 seek 时校验剩余长度，读满循环；`WriteStringI32Size` 改写**字节数**（原写字符数，非 ASCII 路径回读错位） |
| `Constants.cs` | 修改 | 新增 `DefaultCopyBufferSize = 1 << 20`、`MinimumCopyBufferSize = 4 << 10`；原 4 个 TEX 上限不变 |
| `Package\PackageEntryStream.cs` | **新增** | `Stream` 子类，持 `SafeFileHandle` + 绝对区间，用 `RandomAccess.Read/ReadAsync` 读取 → 不共享可变 Position，同一包可并行读多条 |
| `Package\PackageFileSource.cs` | **新增** | `Open(path, validateEntryRanges = true)`；暴露 `Entries/DataStart/HeaderSize/BodyLength`；`OpenEntry(entry)` 二次校验区间并返回 `PackageEntryStream`；`FindEntry(fullPath)` 序数匹配 |
| `Package\EntryExtractor.cs` | **新增** | `CopyEntry` / `CopyStream`，`ArrayPool<byte>` 租 1 MiB，`IProgress<long>` 每 16 MiB 回调，`CancellationToken` 可中断；提前 EOF 抛 `EndOfStreamException("Source ended after … of … bytes — the entry is truncated")` |

### 2.3 未改动的原版文件

`RePKG.Application` 其余 15 个文件（TEX 解析/解码/转换、`RG88`、`DXT`、`TexJsonInfoGenerator` 等）**本阶段保持原样**，仍引用 `SixLabors.ImageSharp`/`Newtonsoft.Json`——这是阶段 4 的替换范围，不影响本阶段的 pkg 流式改造。

### 2.4 仓库级新增

`global.json`（sdk 10.0.401）、`Directory.Build.props`、`Directory.Packages.props`（中央版本）、`RePKG-Enhanced.slnx`、`.gitignore`、`.gitattributes`、`LICENSE-RePKG`。

### 2.5 测试工程（新增 4 个）

| 工程 | 内容 |
|---|---|
| `tests\RePKG.TestSupport` | `DeterministicFiller`（64 KiB 模板块 + SplitMix64，可按绝对偏移确定性填充）、`SyntheticPkgFactory`（常量内存合成 pkg、生成期望哈希）、`HashingSink`（只算 SHA-256 不落盘）、`BigTestGate`（`REPKG_BIG_TESTS=1` 开关 + 磁盘空间检查） |
| `tests\RePKG.Tests` | `PackageStreamingTests`（9 项）、`TruncatedPackageTests`（5 项） |
| `tests\RePKG.Tests.BigFiles` | `LargeEntryExtractionTests`（2 项，受门禁开关保护） |

---

## 3. 编译与测试命令

```powershell
# 1) 全量编译
cd D:\BetterRePKG
dotnet build RePKG-Enhanced.slnx -c Release

# 2) 快速回归（无需门禁，约 1 秒）
dotnet test tests\RePKG.Tests\RePKG.Tests.csproj -c Release

# 3) 大文件门禁（写入 GB 级中间文件，建议指定盘符）
$env:REPKG_BIG_TESTS = "1"
$env:REPKG_BIG_TMP   = "D:\BetterRePKG\.bigtmp"
dotnet test tests\RePKG.Tests.BigFiles\RePKG.Tests.BigFiles.csproj -c Release --logger "console;verbosity=detailed"
```

---

## 4. 验收结果（实测数据）

### 4.1 快速回归

```text
Passed!  - Failed: 0, Passed: 14, Skipped: 0, Total: 14, Duration: 1 s
```

14 项覆盖：表读取不填 `Bytes`、表读取分配量、条目字节精确、越界读取归零、双流并行、非 ASCII 路径回写、进度回调、64 MiB 提取分配上限、取消、5 类截断/异常输入。

### 4.2 大文件门禁（`REPKG_BIG_TESTS=1`）

```text
合成 pkg 耗时 4018 ms，大小 1073741867
预期哈希耗时 8929 ms
提取耗时 14096 ms，分配 1.0 MiB，峰值工作集 40.2 MiB
输出哈希耗时 9881 ms
合成 2.2 GiB pkg 耗时 21294 ms，大小 2362232074
提取 2.2 GiB 耗时 20854 ms，分配 1.0 MiB，峰值工作集 42.3 MiB

Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2, Duration: 1 m 41 s
```

| 场景 | 数据量 | 分配增量 | 峰值工作集 | 逐字节一致 | 结果 |
|---|---|---|---|---|---|
| 单条目 1 GiB → 文件 | 1 GiB | **1.0 MiB** | 40.2 MiB | SHA-256 相等 | ✅ |
| 单包 2 × 1.1 GiB → 哈希 | 2.2 GiB | **1.0 MiB** | 42.3 MiB | 两条 SHA-256 均相等 | ✅ |

**关键观察**：分配增量恒为 1.0 MiB（正好是 `DefaultCopyBufferSize` 的 `ArrayPool` 首次租用），与条目体积、包体积无关；峰值工作集 40 MiB 主要来自 .NET 运行时自身，与数据量无线性关系。原版同一场景会分别占用 1 GiB / 2.2 GiB 常驻。

---

## 5. 硬指标核验

```powershell
# 提取链路已无整条目载入
Select-String -Path src\**\*.cs -Pattern "ReadBytes\(entry\.Length\)|PopulateEntriesWithData|ReadEntryBytes"
# → 仅剩 PackageReader.cs 顶部注释中的"原版行为"说明
```

剩余 `ReadBytes(...)` 调用全部位于 `Texture\TexImageReader.cs`（TEX mipmap 读取，受 `MaximumMipmapByteCount = 250 MB` 约束），**不属于 pkg 提取链路**，将在阶段 4 重写 TEX 解码时处置。

---

## 6. 与阶段 1 清单的偏差（需确认）

| # | 阶段 1 计划 | 实际 | 理由 |
|---|---|---|---|
| 1 | 新增 `PackageTable` + `IPackageTableReader` | **未实现**，`PackageReader.ReadFrom` 仍返回 `Core.Package.Package`，`DataStart` 由 `stream.Position` 给出 | 该抽象只是把 `Package` 再包一层，对"流式"没有贡献；少 2 个公共类型、少一层间接。若后续 GUI 需要"元数据与句柄分离"，可再引入 |
| 2 | 新增 `Shared\IO\StreamPump.cs` | **未实现**，分块复制只在 `Application\Package\EntryExtractor.cs` | 阶段 1 的两条硬规则之一是"不得有第二处复制循环"，提前建 `StreamPump` 反而会制造重复实现；阶段 3 若 `Shared` 需要（日志/原子写入的落盘复用），再抽出并把 `EntryExtractor` 改为委托 |
| 3 | 解决方案文件 `.sln` | 实际为 `RePKG-Enhanced.**slnx**`（.NET 10 SDK 默认新格式） | 无功能差异；如需 `.sln` 可一条命令转换 |
| 4 | 条目路径上限 255 | **1024** | 原版 255 是静默截断的暴力上限；改为显式上限 + 抛错，1024 覆盖真实 WE 包的最长路径 |
| 5 | `PackageFileSource.Open(path)` | 增加可选参数 `validateEntryRanges = true` | 让"表校验"可关，便于测试与诊断损坏包（`OpenEntry` 仍做第二次校验，安全性不降级） |

---

## 7. 已知限制

1. **TEX 分支仍是全量内存**：`TexReader` 基于 `new MemoryStream(bytes)`，`TexToImageConverter` 还会再物化解码结果 + 编码结果。≥ 1 GB 的 TEX 条目本阶段仍会 OOM——这与阶段 0 的 Q3 决策一致（1 GiB 验收走非 TEX 条目 + `--no-tex-convert`），阶段 4 处理。
2. **包体上限 4 GiB**：格式层面 `uint32` 偏移，单包数据区不能超过 `uint.MaxValue`；`PackageWriter` 会显式抛错而不是写出损坏包。
3. **条目表仍整表驻留**：百万级条目时元数据会到数百 MB（`MaximumEntryCount` 兜底）。真实 WE 包条目数是百/千级，暂不分块；列入风险观察。
4. **尚无入口层**：`RePKG.Cli`/`RePKG.Gui` 未创建，本阶段只有库 + 测试可验证。
5. **`FileShare.Read`**：Windows 上仍允许其他进程同时读，但若对方以 `FileShare.None` 打开则本工具打不开——属预期行为。

---

## 8. 下一步（阶段 3）

`RePKG.Shared`：`ExportQueue`（单队列 + 有限并发 + 暂停/取消 + 失败隔离 + 进度）、`AtomicFileWriter`、`SafePath`、`JsonlExportLog`。门禁：单测通过（含路径穿越全部拒绝、原子写入中断不留半成品）。

> 等待用户确认：§6 的 5 项偏差是否接受（尤其 #1/#2 少建的两个抽象）。

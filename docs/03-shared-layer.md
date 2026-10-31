# 阶段 3 — `RePKG.Shared`（队列、流式 IO、路径校验、日志）

- 状态：**已验收**（2026-10-31）
- 门禁：单测通过 —— `RePKG.Tests.Shared` **61 passed / 0 failed**，`RePKG.Tests` 回归 **14 passed / 0 failed**，解决方案 0 error
- 上游基线：`notscuffed/RePKG` @ `8005eb2`（MIT），本阶段全部为**新增**代码，未改动 `RePKG.Core` / `RePKG.Application`

---

## 1. 本阶段交付内容（对照阶段 1 清单）

| 阶段 1 规划 | 实际 | 说明 |
|---|---|---|
| `Shared/Paths/SafePath.cs` | ✅ `src\RePKG.Shared\Paths\SafePath.cs` | 路径穿越防御 |
| `Shared/Paths/FileNameSanitizer.cs` | ✅ `src\RePKG.Shared\Paths\FileNameSanitizer.cs` | `SingleDirectory` 压平 |
| `Shared/IO/AtomicFileWriter.cs` | ✅ `src\RePKG.Shared\IO\AtomicFileWriter.cs` | 临时文件 + 原子替换 |
| `Shared/IO/StreamPump.cs` | ➖ 未建 | 分块复制仍只有 `RePKG.Application\Package\EntryExtractor.cs` 一处（ADR-010） |
| `Shared/Logging/IExportLog.cs` + `JsonlExportLog.cs` | ✅ 合并进 `src\RePKG.Shared\Logging\JsonlExportLog.cs` | 接口 + 空实现 + JSONL 实现同文件 |
| `Shared/Catalog/PackageCatalog.cs` | ➖ 未建 | 批量清单由 `RePKG.Cli`/`RePKG.Gui` 阶段按需建立；本阶段尚无第二个使用点，不做投机抽象 |
| `Shared/Catalog/PackageEntryFilter.cs` | ✅ `src\RePKG.Shared\Catalog\PackageEntryFilter.cs` | 复用原版 `-e/-i` 语义（ignore 优先） |
| `Shared/Export/ExportQueue.cs`（+Job/Options/Result/Progress） | ✅ `Export\ExportQueue.cs`、`Export\ExportOptions.cs`、`Export\ExportResult.cs` | 单一导出队列 |
| `Shared/Native/NativeBootstrap.cs` | ⏳ 推迟到阶段 5 | 与 GUI 启动路径绑定，阶段 4 用不到 |

新增依赖：**0**（只用 BCL：`System.Threading.Channels`、`System.Text.Json`、`System.Buffers`）。`System.Threading.Channels` 属共享框架，不增加发布体积。

## 2. 关键实现要点

### 2.1 路径穿越防御（`SafePath.TryResolve`）

逐项拒绝，任何一项命中即返回 `false`（`Resolve` 则抛 `PathTraversalException`）：

- 空串 / 仅空白 / 含 `\0`
- 绝对路径（`Path.IsPathRooted`）、`\\server\share` 与 `//server/share`、盘符形式 `C:`、`file:` 等含 `:` 的段
- 任何段为空、`.`、`..`
- 非法字符 `< > : " | ? *`、尾部点 / 尾部空格（Windows 归一化陷阱）
- 保留设备名 `CON/PRN/AUX/NUL/COM1-9/LPT1-9`（含带扩展名形式，如 `CON.txt`）
- 规范化后不在根目录之下（Windows 用 `OrdinalIgnoreCase`，避免 `D:\Out` / `D:\out` 大小写绕行）
- 已存在目录的符号链接 / 联接点跳到根外（`DirectoryInfo.ResolveLinkTarget(returnFinalTarget: true)`；解析失败按危险处理）

### 2.2 原子写入（`AtomicFileWriter`）

`Create(target)` → 同目录 `.<name>.<12位随机hex>.tmp`（`FileMode.CreateNew`，不覆盖已有文件）→ 写完 `Flush(flushToDisk: true)` → `File.Move(temp, target, overwrite: true)`。

- `Dispose()` 未 `Commit()` 时删除临时文件；`Commit()` 失败也删临时文件后再抛
- 目标文件在同一目录，`File.Move` 因此是同卷 rename，不会退化为复制
- 中途断电/进程被杀 → 只遗留 `.tmp`，目标文件保持旧内容（测试 `Simulated_Crash_Keeps_Old_Target` 用"写完但不 Commit 就 Dispose"验证）

### 2.3 导出队列（`ExportQueue`）

- 有界 `Channel<WorkItem>`（`QueueCapacity`，`BoundedChannelFullMode.Wait`）→ 生产者（GUI 批量导入）会被反压，不会无限堆任务
- worker 数 = `Concurrency`（默认 1，可配 1–2 或更高），**每个 worker 同一时刻只持有一个 pkg 的流句柄**
- 每个条目：`WaitIfPausedAsync()`（条目边界暂停）→ 跳过已存在（未 `Overwrite`）→ `SafePath.TryResolve`（失败计 `Skipped` 并记 `entry.unsafe-path`）→ `AtomicFileWriter` + `EntryExtractor.CopyEntry`（1 MiB 块）→ `Commit()`
- 单条目异常 → `entry.failed` 记录并继续；整包异常 → `FatalError`，其余 job 不受影响
- 取消：`Control.Cancel()` → 条目边界或复制中（`CopyEntry` 的 `CancellationToken`）生效；未提交的临时文件由 `AtomicFileWriter.Dispose` 清理；已完成条目默认保留；`RollbackOnCancel = true` 时删除本 job 已写文件
- 日志：`IExportLog`（构造注入）+ 每个输出目录共享一个 `JsonlExportLog`（`append: true`，避免 GUI 多次导出互相截断）；`ExportQueue.DisposeAsync` 负责关闭共享句柄

### 2.4 JSONL 日志（`JsonlExportLog`）

事件名固定：`package.start` / `entry.ok` / `entry.unsafe-path` / `entry.name-collision` / `entry.canceled` / `entry.failed` / `package.done` / `package.failed`。每行一个 JSON 对象，含 `timestamp`、`level`、`event`、`package`、`entry`、`bytes`（仅非零时写）、`message`。用 `Utf8JsonWriter` + `ArrayBufferWriter<byte>`，`JavaScriptEncoder.UnsafeRelaxedJsonEscaping` 保证中文不被转义；`lock` 保证并发写不撕行。

## 3. 编译与测试

```powershell
cd D:\BetterRePKG

# 编译
dotnet build RePKG-Enhanced.slnx -c Release

# 门禁：Shared 单测
dotnet test tests\RePKG.Tests.Shared -c Release

# 回归：内核单测（阶段 2）
dotnet test tests\RePKG.Tests -c Release

# 大文件门禁（阶段 2，仍需显式开启，约 1m40s）
$env:REPKG_BIG_TESTS=1; $env:REPKG_BIG_TMP="D:\BetterRePKG\.bigtmp"
dotnet test tests\RePKG.Tests.BigFiles -c Release
```

实测结果：

```
Build succeeded.  5 Warning(s)  0 Error(s)          # 5 条均为测试项目 xUnit1051 建议性告警
Passed! - Failed: 0, Passed: 61, Skipped: 0, Total: 61  - RePKG.Tests.Shared.dll    (4 s)
Passed! - Failed: 0, Passed: 14, Skipped: 0, Total: 14  - RePKG.Tests.dll           (1 s)
Skipped! - Failed: 0, Passed: 0, Skipped: 2, Total: 2   - RePKG.Tests.BigFiles.dll  (未设 REPKG_BIG_TESTS)
```

## 4. 验收点对照

| 验收点 | 证据 |
|---|---|
| 路径穿越全部拒绝 | `SafePathTests`：25 条危险输入（`../x`、`..\x`、`/etc/passwd`、`C:\Windows\x`、`\\srv\share\x`、`a/../../b`、`CON`、`aux.txt`、`x.`、`x `、`a//b`、`a/./b`、空段等）全部 `TryResolve == false`；6 条正常路径全部通过；兄弟前缀陷阱 `..\Out2\x` 被拒；`a/./b` 被拒 |
| 原子写入中断不留半成品 | `AtomicFileWriterTests` 8 项：提交后临时文件消失、未提交无残留、`Abort` 保留旧内容、覆盖替换、模拟中途异常保留旧文件且旧内容不变、自动建目录、8 线程并行写同一目标（最后一个赢、无异常）、二次 `Commit` 抛 |
| 取消在条目边界生效且不留临时文件 | `ExportQueueTests.Cancel_MidEntry_Leaves_No_Partial_File_And_No_Temp`：64 MiB 条目在首个 16 MiB 进度回调处取消 → `Canceled ≥ 1`、目标文件不存在、输出目录无 `*.tmp` |
| 暂停不产生副作用 | `Paused_Queue_Does_Not_Start_Work_Until_Resumed`：暂停 400 ms 内不写任何文件，`Resume` 后正常完成 |
| 单条目/单包失败隔离 | `Corrupt_Package_Fails_Without_Affecting_Other_Jobs`、`Summary_Accumulates_Failures_Across_Packages`：坏包 `FatalError`、好包照常导出、`ExportSummary.IsSuccess == false`（供 CLI 退出码使用） |
| 批量内存不失控 | 队列有界 + 每 worker 单句柄 + 1 MiB 块复制；`Concurrency_Two_Processes_All_Jobs`（`Concurrency=2`、`QueueCapacity=2`、4 包）全部完成 |
| 路径穿越在导出链路真正生效 | `Entry_With_Traversal_Path_Is_Skipped_And_Nothing_Escapes`：pkg 内含 `../escape.bin` → 计 `Skipped`、`_out\safe.bin` 正常写出、包目录外无 `escape.bin` |
| 日志可解析 | `JsonlExportLogTests` 4 项（逐行 `JsonDocument.Parse`、`bytes` 仅非零、8 线程 × 50 条并发不撕行、append 追加）+ `Writes_Jsonl_Log_With_Entry_And_Summary_Events` |

## 5. 本阶段发现并修掉的问题

1. `ExportQueue.DisposeAsync` 原先只关闭构造注入的 `IExportLog`，**按输出目录共享的 `JsonlExportLog` 句柄未关闭** → 导出结束后 `export-log.jsonl` 仍被本进程独占，外部工具读不到。已在 `DisposeAsync` 末尾逐个 `Dispose()` 并清空字典（`ExportQueue.cs`）。
2. `JsonlExportLog` 的属性名 `Path` 遮蔽 `System.IO.Path`，导致同类中使用 `Path.GetFullPath` 编译失败 → 改名 `FilePath`。
3. `RePKG.Application/Constants.cs` 为 `internal`，`RePKG.Shared` 无法读取 `DefaultCopyBufferSize` → 改为 `public`（不影响 ABI，属类型可见性放宽）。

## 6. 已知限制

- **路径长度**：`SafePath` 未对单段/整条路径做长度上限检查，超长路径交给文件系统报错（Windows 长路径需 `\\?\` 前缀，本阶段不主动支持）。条目声明的路径长度上限由阶段 2 的 `ReadStringI32Size(maxLength: 1024)` 控制。
- **符号链接检测**：只检查**已存在**的目录段；条目目标文件不存在时无法出现文件级链接逃逸（`AtomicFileWriter` 用 `FileMode.CreateNew` 创建临时文件、`File.Move` 落到目标，不会写入链接指向的外部文件）。真实 pkg 中不存在链接，此项属纵深防御。
- **`Concurrency > 1` 时进度事件来自多个线程**，订阅方需自行同步（GUI 侧用 `Dispatcher.UIThread.Post`）。
- **`PackageCatalog` 未实现**：批量清单/预览（读条目表但不导出）留到阶段 4/5，与具体 UI 需求一起设计，避免二次返工。
- **xUnit1051 告警（5 条）**：测试里直接调用带 `CancellationToken` 的同步 API，未传 `TestContext.Current.CancellationToken`。仅告警，不影响门禁。

## 7. 下一步（阶段 4：`RePKG.Cli`）

- 命令：`extract`（文件/目录/递归）、`info`、`tex`（TEX 转图片），参数兼容原版 `-o/-e/-i/-s/-r/-n/-c/--no-tex-convert/--overwrite`，**去掉 `interactive`**（决策 Q7）
- 目录递归 + 扩展名过滤 → 全部走 `ExportQueue`，CLI 自己**不写**条目循环
- 退出码：`0` 全部成功；`1` 有失败项；`2` 参数错误；`130` 用户取消
- 目标：真实/合成 pkg 批量解包通过（门禁）

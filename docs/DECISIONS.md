# 决策记录（ADR）

格式：编号 · 决策 · 理由 · 影响面 · 状态。日期用本地时区（America/La_Paz）。

---

## ADR-001 项目形态：独立新仓库，基于 RePKG 最小改动
- **决策**：`D:\BetterRePKG` 独立 Git 仓库，MIT，基线 `notscuffed/RePKG@8005eb2`。原版文件保留在 `src/` 下并保留原 header，改动文件额外加"二次修改"说明。
- **理由**：用户要求与本机其它项目隔离；上游 MIT 且停更，fork 后独立演进成本最低。
- **状态**：已定（阶段 0）。

## ADR-002 根因修复：条目表与数据区彻底分离
- **决策**：`PackageReader` 只读条目表（`FullPath/Offset/Length/Type`），删除 `PopulateEntriesWithData`；条目数据经 `PackageEntryStream` 按需 seek 后分块复制。
- **理由**：根因即 `PackageReader.cs:54-61` 的 `entry.Bytes = reader.ReadBytes(entry.Length)`，提取单条也要载入整包。
- **影响**：`PackageEntry.Bytes` 不再被解析流程填充；所有消费方改走 `PackageFileSource`。
- **状态**：已定（阶段 0）。

## ADR-003 偏移宽度：uint32 语义 + `long` 内部类型
- **决策**：`PackageEntry.Offset/Length` 由 `int` 改 `long`；读写按 `uint32` 解释。
- **理由**：原版 `int` 把可用数据区限制在 2 GiB 以内；用户要求支持 ≥2 GB 总数据量且不引入新格式。
- **影响**：破坏 Core 公开 API（本项目新仓库可接受）；`PackageWriter` 写回 `(uint)`。
- **状态**：已定（阶段 0，Q2）；阶段 1 记为 D2，待确认。

## ADR-004 TFM：Core 保持 netstandard2.0，其余 net10.0
- **决策**：`RePKG.Core` = netstandard2.0；`RePKG.Application`/`Shared`/`Cli`/`Gui`/测试 = net10.0。
- **理由**：Application 需要 `Span<byte>`、`BinaryPrimitives`、`NativeLibrary`、AOT 注解；Shared 需要 `NativeLibrary.SetDllImportResolver` 与 `System.Threading.Channels`。Core 是纯模型，无需上移。
- **影响**：Application 不再是 netstandard2.0 库（不再能被 .NET Framework 消费）——本项目无此需求。
- **状态**：阶段 1 记为 D1，待确认。

## ADR-005 图像栈：SkiaSharp 取代 ImageSharp
- **决策**：SkiaSharp 3.119.4（MIT），删除 `Texture/Helpers/RG88.cs` 的像素路径（改手工展开灰度/透明度）。
- **理由**：① 2.1.13 虽为 Apache-2.0（与 MIT 兼容），但 2.x 无 trim/AOT 注解；② 纯增载荷；③ 动画 GIF 必须物化全部帧；④ 2.1.13 是 2.x 末代。SkiaSharp 已被 Avalonia 依赖，AOT 实测 0 warning。
- **影响**：TEX→PNG/GIF 全链路重写；GIF 编码需自研（SkiaSharp `Encode(Gif)` 不可用，issue #1130）。
- **状态**：已定（阶段 0，Q8/Q10）。

## ADR-006 发布形态：平台差异化
- **决策**：Win/Linux = 单文件 AOT + 原生库内嵌首启解压；macOS = `.app` 包。
- **理由**：SDK 不支持 AOT + 单文件共存（dotnet/runtime#117986），去掉 `PublishAot` 才真单文件；macOS 本身要求 bundle 形态。
- **影响**：新增 `RePKG.Shared/Native/NativeBootstrap.cs`；首次启动有解压开销。
- **状态**：已定（阶段 0，Q1′）。

## ADR-007 批量导出：单一队列、有界、失败隔离
- **决策**：`RePKG.Shared/Export/ExportQueue` 为 CLI 与 GUI 的唯一导出通道；并发上限默认 2、队列上限 64；单条目失败记日志并继续；取消在条目边界生效、保留已完成。
- **理由**：用户硬约束（§四）；避免 GUI/CLI 各写一套循环导致口径漂移。
- **影响**：Cli/Gui 只能做"装配 + 呈现"，不得包含条目循环。
- **状态**：已定（阶段 0/1）。

## ADR-008 大文件测试口径
- **决策**：门禁断言用 `GC.GetTotalAllocatedBytes(precise: true)` 的增量；工作集仅作辅助。测试默认 `Skip`，`REPKG_BIG_TESTS=1` 启用。
- **理由**：本机 7.9 GB 内存，工作集受 OS 缓存与 GC 策略干扰，无法稳定断言。
- **状态**：已定（阶段 0，Q3/Q11）。

## ADR-009 取消 `PackageTable` / `IPackageTableReader` 抽象
- **决策**：`PackageReader.ReadFrom` 继续返回 `Core.Package.Package`，数据区起点由 `stream.Position` 直接给出；不引入额外的"表对象 + 读表接口"。
- **理由**：该抽象只是把已有对象再包一层，对"数据区不经内存"这一目标没有任何贡献，却新增 2 个公共类型与一层间接。真正的流式要求由 `PackageFileSource`（持句柄 + 偏移）满足。
- **影响**：阶段 1 清单中 `Package/PackageTable.cs`、`IPackageTableReader.cs` 两项不再创建。
- **状态**：已定（阶段 2，已确认）。

## ADR-010 分块复制只保留一处实现
- **决策**：分块 `Read/Write` 循环只存在于 `Application/Package/EntryExtractor.cs`；原计划放入 `Shared/IO/StreamPump.cs` 的实现**在阶段 2 不创建**。
- **理由**：阶段 1 自查规则"不得有第二处复制循环"要求唯一实现；提前在 `Shared` 建 `StreamPump` 会造成 Application 与 Shared 两份近似代码。等阶段 3 出现第二个真实调用点（原子写入落盘）时再抽公共件，并让 `EntryExtractor` 委托过去。
- **影响**：阶段 3 若抽出 `StreamPump`，`EntryExtractor.CopyStream` 改为转发，签名不变。
- **状态**：已定（阶段 2，已确认）。

## ADR-011 阶段 3 未抽 `StreamPump`、未建 `PackageCatalog`
- **决策**：阶段 3 结束时，分块复制仍只存在于 `Application/Package/EntryExtractor.cs`；`Shared/IO/StreamPump.cs` 与 `Shared/Catalog/PackageCatalog.cs` 均不创建。
- **理由**：`EntryExtractor.CopyEntry` 已经接收任意 `Stream` 目标（`AtomicFileWriter.Stream` 就是普通 `FileStream`），没有出现"第二处复制循环"，此时抽 `StreamPump` 只会多一层转发；`PackageCatalog`（批量清单/预览）在阶段 3 没有任何消费者，等 `RePKG.Cli` 的目录枚举与 GUI 的批量列表落地后，按真实需求一次性设计。
- **影响**：阶段 1 清单中 `Shared/IO/StreamPump.cs`、`Shared/Catalog/PackageCatalog.cs` 标注为"未建"；若阶段 4/5 出现第二调用点或真实清单需求，再引入并更新本 ADR。
- **状态**：已定（阶段 3）。

## ADR-012 导出日志句柄由队列统一关闭
- **决策**：`ExportQueue.DisposeAsync` 在 worker 全部结束后，逐个 `Dispose()` `_sharedLogs` 里的 `JsonlExportLog` 并清空字典，然后才 `Dispose()` 构造注入的 `IExportLog`。
- **理由**：按输出目录共享的日志（`append: true`，避免 GUI 连续导出互相截断）此前只在 `ExportQueue` 生命周期内有效，但 `DisposeAsync` 漏关，导致 `export-log.jsonl` 被本进程独占、外部工具读不到；这属于资源泄漏而非测试假象。
- **影响**：日志在 `ExportQueue` 释放后即可被读取；GUI 若需导出后立即展示日志，只需先 `DisposeAsync` 队列（阶段 5 用法）。
- **状态**：已定（阶段 3）。

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
- **状态**：已定（阶段 0，Q2）；阶段 1 记为 D2，已确认。

## ADR-004 TFM：Core 保持 netstandard2.0，其余 net10.0
- **决策**：`RePKG.Core` = netstandard2.0；`RePKG.Application`/`Shared`/`Cli`/`Gui`/测试 = net10.0。
- **理由**：Application 需要 `Span<byte>`、`BinaryPrimitives`、`NativeLibrary`、AOT 注解；Shared 需要 `NativeLibrary.SetDllImportResolver` 与 `System.Threading.Channels`。Core 是纯模型，无需上移。
- **影响**：Application 不再是 netstandard2.0 库（不再能被 .NET Framework 消费）——本项目无此需求。
- **状态**：阶段 1 记为 D1，已确认。

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

## ADR-013 TEX 转换以 `IEntryConverter` 注入 `ExportQueue`
- **决策**：`ExportQueue(ExportOptions, IExportLog?, IEntryConverter?)`；TEX → png/gif/mp4/json 的转换由 `Shared/Export/TexEntryConverter.cs` 实现，作为第三个构造参数注入。CLI 与将来的 GUI 用同一组合。
- **理由**：用户硬约束「CLI 与 GUI 共用同一导出队列，不得各写一套循环」。若把转换写在 `ExtractCommand` 里，GUI 必然复制一遍；若写死在队列里，`--no-tex-convert` 与将来的其他转换器又要加开关。
- **影响**：跳过判定的责任被切开——非转换条目由队列用 `File.Exists` 判；转换条目的真实目标扩展名要解析 TEX 头才知道，由转换器返回 `ConversionResult.Skip`。队列里因此多一个 `convertible` 分支，但条目循环仍只有一处。
- **状态**：已定（阶段 4）。

## ADR-014 GIF 编码器自研，逐帧不物化
- **决策**：`Texture/Gif/{GifWriter,ColorQuantizer,GifFrameEnumerator}.cs` 自研 GIF89a 编码（LZW + 中位切分量化 + 每帧局部位图色表），逐帧合成、写盘、释放。
- **理由**：SkiaSharp 的 `SKImage.Encode(SKEncodedImageFormat.Gif)` 在 3.119.4 返回 null（上游 issue #1130），ImageSharp 的 `GifEncoder` 要求全部帧同时在内存（帧数 × W × H × 4）。用户 Q8 选择 G1（自研）。
- **影响**：GIF 输出有损（≤255 色 + 1 透明索引）；只支持 raw mipmap 格式（与原版一致）。正确性由 `GifEncodingTests` 用 **SKCodec 独立解码**回读像素与帧延迟来保证，而不是自己验自己。LZW 码长增长规则经该测试确认（`nextCode > (1 << codeSize)` 即可，不需要 `>=` 变体）。
- **状态**：已定（阶段 4）。

## ADR-015 CLI 的目录约定与退出码
- **决策**：`repkg extract` 把每个 pkg 导出到 `<输出根>/<pkg 名>`（`-n` 用 `project.json` 的 title，非法名经 `SafePath`/`FileNameSanitizer` 处理）；退出码 `0` 全成功 / `1` 有失败项 / `2` 参数错误 / `130` 取消；不实现 `interactive` 子模式。
- **理由**：原版把多个 pkg 的条目摊平到同一输出目录，批量时同名条目会互相覆盖；每包一个子目录让批量天然可并发且可回溯。退出码要能被脚本判断（用户 §四/§九 要求失败计数可见）。
- **影响**：与原版 CLI 的输出布局不同（属**有意的兼容性差异**，已在 `--help` 里写明）；`-s/--singledir` 仍在**单包内**压平。
- **状态**：已定（阶段 4，Q7 决策的落地）。

## ADR-016 GUI 通过 `entrySelector` 复用 `ExportQueue`
- **决策**：`ExportQueue.EnqueueAsync/EnqueueManyAsync` 增加可选参数 `Func<PackageEntry, bool>? entrySelector`；GUI 只把「勾选中的 `FullPath` 集合」作为谓词传进队列，不在 GUI 里写任何条目循环、不自行打开 pkg、不自行落盘。
- **理由**：功能规范硬约束「CLI 与 GUI 必须共用同一流式内核与导出队列」。GUI 与 CLI 的唯一真实差异是「导出哪些条目」——把差异收敛成一个谓词，比复制一份循环（并随时间漂移）安全得多；同时避免了 GUI 侧出现第二份 `SafePath`/`AtomicFileWriter`/取消语义。
- **影响**：`WorkItem` 多带一个委托；条目循环里多一次 `selector(entry)` 调用（O(1) 哈希查表）。GUI 的暂停/取消/进度/失败隔离全部继承队列行为，没有第二套实现。
- **状态**：已定（阶段 5）。

## ADR-017 资源清单（manifest）归 `RePKG.Shared/Catalog`
- **决策**：新增 `PackageManifest`（`ToJson` / `Write`）作为清单 JSON 的唯一实现；`repkg info --json`、`repkg extract --manifest`、GUI「写 manifest 清单」三处共用；`ExportOptions.WriteManifest` 默认 `false`（CLI 显式开关，GUI 默认勾选）。
- **理由**：清单是「包内容的属性」，不是「导出器的属性」——放进 Shared 才能同时服务只读命令（info）与写盘命令（extract）。若两边各写一份 `Utf8JsonWriter`，字段名与数值类型必然漂移（阶段 4 的 `info --json` 就已经是一份手写实现）。
- **影响**：`ExportQueue` 在 job 结束时用 `AtomicFileWriter` 写 `<输出目录>/manifest.json`，失败只记 `manifest.failed` 警告（清单不是导出产物，不该把 job 判失败）；测试覆盖 Shared/CLI/GUI 三层。
- **状态**：已定（阶段 5）。

## ADR-018 Avalonia 12 拖拽迁移到 `IDataTransfer`
- **决策**：窗口拖拽改用 `DragEventArgs.DataTransfer`（`IDataTransfer`）+ `DataTransferExtensions.TryGetFiles()`；测试侧用 `DataTransfer` + `DataTransferItem.CreateFile` 构造载荷，并走无头的 `RawDragEventType.DragOver/Drop` 真实事件链。
- **理由**：Avalonia 12 移除了 `DragEventArgs.Data` 与 `DataFormats`（`CS0619: 'DataFormats' is obsolete: 'Use DataFormat instead'`），旧代码直接编译失败。用真实事件链而非直接调 VM，是因为「拖拽在 UI 层被正确接线」本身就是功能规范的一项，只能靠事件路径验证。
- **影响**：`DataFormatKind`/`DataFormat<T>` 成为文件载荷入口；未来若要支持「拖入图片作为纹理」等，也走同一套 `IDataTransfer` 抽象。
- **状态**：已定（阶段 5）。

## ADR-019 Windows 单文件 AOT：原生库内嵌 + 启动自解压预加载
- **决策**：不用 `PublishSingleFile`（对 `PublishAot` 无效），改为把 `libSkiaSharp.dll`（GUI 另加 `libHarfBuzzSharp.dll`、`av_libglesv2.dll`）作为 `EmbeddedResource` 编进程序集，启动时由 `NativeBootstrap.EnsureLoaded()` 解压到 `%LOCALAPPDATA%\RePKG-Enhanced\native\<版本>` 并 `NativeLibrary.Load` 预加载；随后 `DllImport("libSkiaSharp")` 的裸名加载命中已加载模块。MSBuild 目标必须挂 `BeforeTargets="SplitResourcesByCulture"`。
- **理由**：实测 `PublishAot` + `PublishSingleFile` 产物仍带三个原生 DLL（上游 dotnet/runtime#117986 明确「这些属性并不工作」）；.NET 10 又取消了单文件应用在 exe 目录搜索原生库的行为。两条叠加后唯一可靠路径就是「自己打包 + 自己加载」。目标挂载点是 `-v:diag` 才定位到的坑：`SplitResourcesByCulture` 是 `PrepareResourceNames` 的依赖，会在它之前把 `@(EmbeddedResource)` 快照进 `_CoreCompileResourceInputs`，挂 `PrepareResourceNames` 之前已经太晚，资源会被静默丢弃（现象是 `repkg.dll` 只有 31 KB）。
- **影响**：CLI 13.7 MiB / GUI 35.4 MiB 单文件；首次启动解压约 11.6 MB（CLI）或 19 MB（GUI），旧版本目录暂不自动清理；`NativeBootstrap.LastError` 承担「静默失败」的可观测性。Linux/macOS 不受影响（配置全部带 `Condition="'$(RuntimeIdentifier)' == 'win-x64'"`）。
- **状态**：已定（阶段 6）。

## ADR-020 发布流水线：单 job `windows-latest` + tag 触发 + `gh` 上传 Release
- **决策**：`.github/workflows/release.yml` 只有 `windows-x64` 一个 job；触发条件 `push.tags: v*` 与 `workflow_dispatch`（可指定 tag 覆盖已存在的 Release）；用 `gh release create/upload --clobber` 上传 `repkg-win-x64.exe`、`RePKG-Enhanced-win-x64.exe`、`SHA256SUMS.txt`；流水线内置两条「假单文件」护栏——产物目录出现任何 `.dll` 或 exe < 5 MB 直接失败。
- **理由**：用户本轮明确「只要 Windows x64、只跑 windows 流水线」，三平台矩阵此时只会制造看不到的红叉；用官方 `gh` CLI 而不是第三方 Release action，少一个供应链依赖；`vswhere` 提前验证 MSVC 工具链，避免 20 分钟 AOT 编译后在链接阶段才失败；产物校验把「原生库到底有没有进去」变成 CI 断言，而不是靠人肉看体积。
- **影响**：`global.json` 的 SDK 版本放宽为 `10.0.100` + `rollForward: latestFeature`，避免 runner 上只有 10.0.1xx 时直接报错。**已在真实 runner 上验证两次**：`v0.4.0`（Run #41187917612）与 `v1.0.0`（Run #41190337461）均 success，无需按日志微调。Linux/macOS 发布作业留待后续阶段恢复。
- **状态**：已定（阶段 6，已在真实 CI 上验证）。

## ADR-021 README 双语排布：英文为首页 + `README.zh-CN.md`
- **决策**：`README.md` 用英文（GitHub 首页默认展示），中文版放 `README.zh-CN.md`，两个文件顶部各有一行语言切换（`**English** | [简体中文](README.zh-CN.md)` 与反向链接），章节结构与数据表一一对应。
- **理由**：GitHub 的惯例是首页英文、本地语言另立文件（`README.<locale>.md`），这样仓库首屏对国际读者可读，同时中文读者一键切换；两版都放正文而非折叠块，避免单文件内中英文并列导致篇幅翻倍与锚点冲突（徽章里的 `#download` 只能指向一种语言）。
- **影响**：任何 README 改动必须**同时改两个文件**（数据、命令、限制条目保持一致），否则会出现版本漂移；`docs/01-file-inventory.md` 已登记两份文件。
- **状态**：已定（v1.0.0 之后）。

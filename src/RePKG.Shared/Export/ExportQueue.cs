using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using RePKG.Application.Package;
using RePKG.Shared.Catalog;
using RePKG.Core.Package;
using RePKG.Shared.IO;
using RePKG.Shared.Logging;
using RePKG.Shared.Paths;

namespace RePKG.Shared.Export
{
    /// <summary>
    /// 唯一的导出队列。CLI 与 GUI 都只能通过它导出——这是"禁止入口层各写一套循环"的落点。
    ///
    /// 约束（对应规格 §四）：
    /// <list type="bullet">
    /// <item>有界队列：<see cref="ExportOptions.QueueCapacity"/>，满则 <see cref="EnqueueAsync"/> 等待</item>
    /// <item>有限并发：默认 1，任一时刻每个 worker 只持有一个 pkg 句柄</item>
    /// <item>条目级失败隔离：单条失败记日志并继续，最后汇总到 <see cref="ExportJobResult"/></item>
    /// <item>暂停/取消在条目边界生效；取消时清理当前临时文件，已完成条目默认保留</item>
    /// <item>所有条目都经 <see cref="EntryExtractor"/> 分块复制，不整条载入内存</item>
    /// </list>
    /// </summary>
    public sealed class ExportQueue : IAsyncDisposable
    {
        private readonly ExportOptions _options;
        private readonly PackageEntryFilter _filter;
        private readonly IExportLog _log;
        private readonly IEntryConverter? _converter;
        private readonly Channel<WorkItem> _channel;
        private readonly List<Task> _workers = new List<Task>();
        private readonly ConcurrentBag<ExportJobResult> _results = new ConcurrentBag<ExportJobResult>();
        private readonly ConcurrentDictionary<string, JsonlExportLog> _sharedLogs =
            new ConcurrentDictionary<string, JsonlExportLog>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        private readonly object _startGate = new object();

        private int _started;
        private int _completed;
        private int _packagesEnqueued;
        private long _bytesWrittenTotal;
        private bool _disposed;

        public ExportQueue(ExportOptions options, IExportLog? log = null, IEntryConverter? converter = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _options.Validate();

            _filter = new PackageEntryFilter(_options.OnlyExtensions, _options.IgnoreExtensions);
            _log = log ?? NullExportLog.Instance;
            _converter = converter;
            Control = new ExportControl();

            _channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(_options.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        }

        public ExportControl Control { get; }

        public ExportQueueState State
        {
            get
            {
                if (Control.IsCancellationRequested) return ExportQueueState.Canceling;
                if (Control.IsPaused) return ExportQueueState.Paused;
                if (Volatile.Read(ref _started) == 0) return ExportQueueState.Idle;
                if (_channel.Reader.Completion.IsCompleted && Volatile.Read(ref _completed) > 0) return ExportQueueState.Completed;
                return ExportQueueState.Running;
            }
        }

        /// <summary>每处理完一个条目边界或每 16 MiB 触发一次（高频，调用方需自行节流渲染）。</summary>
        public event Action<ExportProgress>? ProgressChanged;

        /// <summary>单个 pkg 完成时触发（失败也会触发）。</summary>
        public event Action<ExportJobResult>? JobCompleted;

        /// <summary>
        /// 入队一个 pkg。队列满时等待；取消时返回已取消的 job 结果而不是抛异常。
        /// </summary>
        /// <param name="packagePath">pkg 绝对/相对路径。</param>
        /// <param name="outputDirectoryOverride">可选：覆盖 <see cref="ExportOptions.OutputDirectory"/>。</param>
        /// <param name="jobLogPath">可选：本次导出单独写一份 JSONL 日志。</param>
        /// <param name="entrySelector">可选：逐条目筛选（GUI 的勾选状态）；null 表示只按扩展名过滤。</param>
        /// <param name="cancellationToken">入队等待的取消令牌（不影响已入队任务）。</param>
        public async Task<ExportJobResult> EnqueueAsync(
            string packagePath,
            string? outputDirectoryOverride = null,
            string? jobLogPath = null,
            Func<PackageEntry, bool>? entrySelector = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
                throw new ArgumentException("pkg 路径为空", nameof(packagePath));

            EnsureStarted();

            var completion = new TaskCompletionSource<ExportJobResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var item = new WorkItem(Path.GetFullPath(packagePath), outputDirectoryOverride, jobLogPath, entrySelector, completion);

            Interlocked.Increment(ref _packagesEnqueued);

            try
            {
                await _channel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                var canceled = new ExportJobResult(
                    item.PackagePath,
                    outputDirectoryOverride ?? _options.OutputDirectory,
                    0, 0, 0, 0, 0, TimeSpan.Zero,
                    "任务在入队前被取消",
                    null);

                completion.TrySetResult(canceled);
                return canceled;
            }
            catch (ChannelClosedException)
            {
                var closed = new ExportJobResult(
                    item.PackagePath,
                    outputDirectoryOverride ?? _options.OutputDirectory,
                    0, 0, 0, 0, 0, TimeSpan.Zero,
                    "队列已关闭，任务未被接受",
                    null);

                return closed;
            }

            return await completion.Task.ConfigureAwait(false);
        }

        /// <summary>批量入队并等待全部完成（顺序无关）。</summary>
        public async Task<IReadOnlyList<ExportJobResult>> EnqueueManyAsync(
            IEnumerable<string> packagePaths,
            string? outputDirectoryOverride = null,
            Func<PackageEntry, bool>? entrySelector = null,
            CancellationToken cancellationToken = default)
        {
            var tasks = new List<Task<ExportJobResult>>();

            foreach (var path in packagePaths)
                tasks.Add(EnqueueAsync(path, outputDirectoryOverride, null, entrySelector, cancellationToken));

            return await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>关闭入队口并等待所有已入队任务完成，返回汇总。</summary>
        public async Task<ExportSummary> CompleteAsync()
        {
            _channel.Writer.TryComplete();

            if (Volatile.Read(ref _started) != 0)
            {
                Task[] snapshot;

                lock (_startGate)
                {
                    snapshot = _workers.ToArray();
                }

                await Task.WhenAll(snapshot).ConfigureAwait(false);
            }

            return BuildSummary();
        }

        /// <summary>请求取消，并等待当前条目收尾（不等待排队中的任务）。</summary>
        public async Task CancelAsync()
        {
            Control.Cancel();
            _channel.Writer.TryComplete();

            Task[] snapshot;

            lock (_startGate)
            {
                snapshot = _workers.ToArray();
            }

            if (snapshot.Length == 0) return;

            try
            {
                await Task.WhenAll(snapshot).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        public ExportSummary BuildSummary()
        {
            var packages = _results.Count;
            var succeeded = 0;
            var skipped = 0;
            var failed = 0;
            var canceled = 0;
            var bytes = 0L;
            var duration = TimeSpan.Zero;

            foreach (var result in _results)
            {
                succeeded += result.Succeeded;
                skipped += result.Skipped;
                failed += result.Failed + (result.FatalError != null ? 1 : 0);
                canceled += result.Canceled;
                bytes += result.BytesWritten;

                if (result.Duration > duration) duration = result.Duration;
            }

            return new ExportSummary(packages, succeeded, skipped, failed, canceled, bytes, duration);
        }

        private void EnsureStarted()
        {
            if (Volatile.Read(ref _started) != 0) return;

            lock (_startGate)
            {
                if (Volatile.Read(ref _started) != 0) return;

                var concurrency = Math.Max(1, _options.Concurrency);

                for (var i = 0; i < concurrency; i++)
                {
                    var worker = Task.Run(RunWorkerAsync);
                    _workers.Add(worker);
                }

                Volatile.Write(ref _started, 1);
            }
        }

        private async Task RunWorkerAsync()
        {
            try
            {
                while (await _channel.Reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (_channel.Reader.TryRead(out var item))
                    {
                        var result = await ProcessJobAsync(item).ConfigureAwait(false);

                        Interlocked.Increment(ref _completed);
                        _results.Add(result);

                        JobCompleted?.Invoke(result);
                        item.Completion.TrySetResult(result);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task<ExportJobResult> ProcessJobAsync(WorkItem item)
        {
            var started = DateTime.UtcNow;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var outputDirectory = Path.GetFullPath(item.OutputDirectoryOverride ?? _options.OutputDirectory);

            var succeeded = 0;
            var skipped = 0;
            var failed = 0;
            var canceled = 0;
            var bytesWritten = 0L;
            var writtenFiles = new List<string>();

            JsonlExportLog? jobLog = null;
            var jobLogOwned = false;

            try
            {
                jobLog = CreateJobLog(item, outputDirectory, out jobLogOwned);

                Directory.CreateDirectory(outputDirectory);

                _log.Info("package.start", item.PackagePath, message: outputDirectory);
                jobLog?.Info("package.start", item.PackagePath, message: outputDirectory);

                using var source = PackageFileSource.Open(item.PackagePath);

                var targets = new List<(PackageEntry Entry, string Target)>();
                var collisions = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

                foreach (var entry in source.Entries)
                {
                    if (!_filter.Matches(entry))
                        continue;

                    // GUI 的逐条目勾选：选中集合随 job 一起进来，避免队列知道任何 UI 概念
                    var selector = item.EntrySelector;
                    if (selector != null && !selector(entry))
                        continue;

                    var relative = _options.SingleDirectory
                        ? FileNameSanitizer.Flatten(entry.FullPath)
                        : entry.FullPath;

                    if (!SafePath.TryResolve(outputDirectory, relative, out var target, out var pathError))
                    {
                        skipped++;
                        _log.Warn("entry.unsafe-path", item.PackagePath, entry.FullPath, pathError);
                        jobLog?.Warn("entry.unsafe-path", item.PackagePath, entry.FullPath, pathError);
                        continue;
                    }

                    if (_options.SingleDirectory && !collisions.Add(target))
                    {
                        skipped++;
                        _log.Warn("entry.name-collision", item.PackagePath, entry.FullPath, "压平后与其他条目同名，已跳过");
                        jobLog?.Warn("entry.name-collision", item.PackagePath, entry.FullPath, "压平后与其他条目同名，已跳过");
                        continue;
                    }

                    targets.Add((entry, target));
                }

                var total = targets.Count;
                var done = 0;
                var canceledRest = false;

                foreach (var (entry, target) in targets)
                {
                    if (canceledRest)
                    {
                        canceled++;
                        continue;
                    }

                    try
                    {
                        await Control.WaitIfPausedAsync().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        canceled++;
                        canceledRest = true;
                        continue;
                    }

                    if (Control.IsCancellationRequested)
                    {
                        canceled++;
                        canceledRest = true;
                        continue;
                    }

                    var convertible = _converter != null && _converter.CanConvert(entry);

                    // 需要转换的条目由转换器自己判断目标是否已存在（目标扩展名要到解析 tex 头之后才知道）
                    if (!convertible && !_options.Overwrite && File.Exists(target))
                    {
                        skipped++;
                        done++;
                        ReportProgress(item, entry.FullPath, 0, entry.Length, done, total, bytesWritten);
                        continue;
                    }

                    var entryBytes = 0L;

                    try
                    {
                        if (convertible)
                        {
                            var conversion = _converter!.Convert(source, entry, target, _options, Control.Token);

                            if (conversion.Skipped)
                            {
                                skipped++;
                            }
                            else
                            {
                                entryBytes = conversion.BytesWritten;
                                succeeded++;
                                bytesWritten += entryBytes;
                                writtenFiles.Add(conversion.PrimaryPath!);

                                _log.Info("entry.ok", item.PackagePath, entry.FullPath, entryBytes);
                                jobLog?.Info("entry.ok", item.PackagePath, entry.FullPath, entryBytes);
                            }
                        }
                        else
                        {
                            // 同步回调（不用 Progress<T>）：后者会把回调甩到线程池，既乱序又多分配
                            var progress = new ThrottledProgress(copied =>
                                ReportProgress(item, entry.FullPath, copied, entry.Length, done, total, bytesWritten + copied));

                            var writer = AtomicFileWriter.Create(target);

                            try
                            {
                                entryBytes = EntryExtractor.CopyEntry(
                                    source,
                                    entry,
                                    writer.Stream,
                                    _options.CopyBufferSize,
                                    progress,
                                    Control.Token);

                                writer.Commit();
                            }
                            finally
                            {
                                writer.Dispose();
                            }

                            succeeded++;
                            bytesWritten += entryBytes;
                            writtenFiles.Add(target);

                            _log.Info("entry.ok", item.PackagePath, entry.FullPath, entryBytes);
                            jobLog?.Info("entry.ok", item.PackagePath, entry.FullPath, entryBytes);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        canceled++;
                        canceledRest = true;
                        _log.Warn("entry.canceled", item.PackagePath, entry.FullPath, "已取消，临时文件已清理");
                        jobLog?.Warn("entry.canceled", item.PackagePath, entry.FullPath, "已取消，临时文件已清理");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _log.Error("entry.failed", item.PackagePath, entry.FullPath, Describe(ex));
                        jobLog?.Error("entry.failed", item.PackagePath, entry.FullPath, Describe(ex));
                    }

                    done++;
                    ReportProgress(item, entry.FullPath, entryBytes, entry.Length, done, total, bytesWritten);
                }

                if (canceledRest && _options.RollbackOnCancel)
                {
                    foreach (var file in writtenFiles)
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch (IOException)
                        {
                        }
                        catch (UnauthorizedAccessException)
                        {
                        }
                    }

                    bytesWritten = 0;
                }

                stopwatch.Stop();

                if (_options.WriteManifest)
                    WriteManifest(source, item, outputDirectory, jobLog);

                var result = new ExportJobResult(
                    item.PackagePath,
                    outputDirectory,
                    succeeded,
                    skipped,
                    failed,
                    canceled,
                    bytesWritten,
                    stopwatch.Elapsed,
                    null,
                    jobLog?.FilePath);

                _log.Info("package.done", item.PackagePath, message: $"ok={succeeded} skipped={skipped} failed={failed} canceled={canceled}");
                jobLog?.Info("package.done", item.PackagePath, message: $"ok={succeeded} skipped={skipped} failed={failed} canceled={canceled}");

                Interlocked.Add(ref _bytesWrittenTotal, bytesWritten);
                return result;
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                return new ExportJobResult(item.PackagePath, outputDirectory, succeeded, skipped, failed, canceled + 1, bytesWritten,
                    stopwatch.Elapsed, "已取消", jobLog?.FilePath);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var reason = Describe(ex);

                _log.Error("package.failed", item.PackagePath, message: reason);
                jobLog?.Error("package.failed", item.PackagePath, message: reason);

                return new ExportJobResult(item.PackagePath, outputDirectory, succeeded, skipped, failed, canceled, bytesWritten,
                    stopwatch.Elapsed, reason, jobLog?.FilePath);
            }
            finally
            {
                if (jobLogOwned) jobLog?.Dispose();
                _ = started;
            }
        }

        /// <summary>
        /// 写资源清单。清单是「包内容的快照」，不是「导出结果」——
        /// 因此写失败只记警告，不把整个 job 判失败。
        /// </summary>
        private void WriteManifest(PackageFileSource source, WorkItem item, string outputDirectory, JsonlExportLog? jobLog)
        {
            var path = Path.Combine(outputDirectory, PackageManifest.FileName);

            try
            {
                var writer = AtomicFileWriter.Create(path);

                try
                {
                    PackageManifest.Write(source, writer.Stream);
                    writer.Commit();
                }
                finally
                {
                    writer.Dispose();
                }

                _log.Info("manifest.written", item.PackagePath, path);
                jobLog?.Info("manifest.written", item.PackagePath, path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _log.Warn("manifest.failed", item.PackagePath, path, Describe(ex));
                jobLog?.Warn("manifest.failed", item.PackagePath, path, Describe(ex));
            }
        }

        private JsonlExportLog? CreateJobLog(WorkItem item, string outputDirectory, out bool owned)        {
            if (!string.IsNullOrWhiteSpace(item.JobLogPath))
            {
                owned = true;
                return new JsonlExportLog(item.JobLogPath!);
            }

            if (!_options.WriteLog)
            {
                owned = false;
                return null;
            }

            // 同一输出目录共享一个日志实例：多个 worker 并发追加也不会因 FileShare 冲突失败
            owned = false;
            var path = Path.GetFullPath(Path.Combine(outputDirectory, _options.LogFileName));

            return _sharedLogs.GetOrAdd(path, static p => new JsonlExportLog(p, append: true));
        }

        private void ReportProgress(WorkItem item, string entryPath, long done, long total, int entriesDone, int entriesTotal, long bytesSoFar)
        {
            var handler = ProgressChanged;
            if (handler == null) return;

            try
            {
                handler(new ExportProgress(
                    item.PackagePath,
                    entryPath,
                    done,
                    total,
                    entriesDone,
                    entriesTotal,
                    Volatile.Read(ref _completed),
                    Volatile.Read(ref _packagesEnqueued),
                    bytesSoFar));
            }
            catch
            {
                // 订阅者异常不得影响导出
            }
        }

        private static string Describe(Exception ex)
        {
            var message = ex.Message;
            return ex.GetType().Name + ": " + message;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;

            _disposed = true;
            Control.Cancel();
            _channel.Writer.TryComplete();

            Task[] snapshot;

            lock (_startGate)
            {
                snapshot = _workers.ToArray();
            }

            if (snapshot.Length > 0)
            {
                try
                {
                    await Task.WhenAll(snapshot).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            Control.Dispose();

            // 按输出目录共享的日志句柄也要在这里关闭，否则日志文件会一直被本进程占用
            foreach (var pair in _sharedLogs) pair.Value.Dispose();
            _sharedLogs.Clear();

            _log.Dispose();
        }

        /// <summary>把 16 MiB 粒度的回调再抽稀到 8 MiB，且同步执行（不上线程池）。</summary>
        private sealed class ThrottledProgress : IProgress<long>
        {
            private const long Step = 8L * 1024 * 1024;

            private readonly Action<long> _callback;
            private long _last;

            public ThrottledProgress(Action<long> callback) => _callback = callback;

            public void Report(long value)
            {
                if (value - _last < Step) return;
                _last = value;
                _callback(value);
            }
        }

        private sealed class WorkItem
        {
            public WorkItem(string packagePath, string? outputDirectoryOverride, string? jobLogPath,
                Func<PackageEntry, bool>? entrySelector, TaskCompletionSource<ExportJobResult> completion)
            {
                PackagePath = packagePath;
                OutputDirectoryOverride = outputDirectoryOverride;
                JobLogPath = jobLogPath;
                EntrySelector = entrySelector;
                Completion = completion;
            }

            public string PackagePath { get; }
            public string? OutputDirectoryOverride { get; }
            public string? JobLogPath { get; }

            /// <summary>GUI 的逐条目勾选：返回 false 的条目直接不参与本次导出（CLI 传 null）。</summary>
            public Func<PackageEntry, bool>? EntrySelector { get; }

            public TaskCompletionSource<ExportJobResult> Completion { get; }
        }
    }
}

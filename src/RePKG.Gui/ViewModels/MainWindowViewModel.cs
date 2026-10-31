// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 主视图模型：批量加载清单 → 勾选/筛选 → 交给 RePKG.Shared 的 ExportQueue 导出。
// 这里**没有**任何条目复制循环，导出完全走阶段 2–4 的同一套内核。

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RePKG.Application.Package;
using RePKG.Gui.Services;
using RePKG.Shared.Export;
using RePKG.Shared.Logging;
using RePKG.Shared.Paths;

namespace RePKG.Gui.ViewModels
{
    public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
    {
        private readonly IDialogService _dialogs;
        private readonly Action<Action> _post;
        private CancellationTokenSource? _exportCancellation;
        private ExportQueue? _queue;
        private Bitmap? _previewImage;

        public MainWindowViewModel(IDialogService dialogs, Action<Action>? post = null)
        {
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _post = post ?? (action => Dispatcher.UIThread.Post(action));

            FilterOptions = new ObservableCollection<EntryKindFilter>
            {
                new("全部条目", null),
                new("图片", EntryKind.Image),
                new("文本", EntryKind.Text),
                new("视频", EntryKind.Video),
                new("其它", EntryKind.Other)
            };

            _selectedFilter = FilterOptions[0];
            OutputDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "RePKG-Enhanced");
        }

        public ObservableCollection<PackageViewModel> Packages { get; } = new();
        public ObservableCollection<EntryViewModel> VisibleEntries { get; } = new();
        public ObservableCollection<EntryKindFilter> FilterOptions { get; }

        [ObservableProperty]
        private PackageViewModel? _selectedPackage;

        [ObservableProperty]
        private EntryViewModel? _selectedEntry;

        [ObservableProperty]
        private EntryKindFilter _selectedFilter;

        [ObservableProperty]
        private string _outputDirectory;

        [ObservableProperty]
        private bool _recursive = true;

        [ObservableProperty]
        private bool _overwrite;

        [ObservableProperty]
        private bool _convertTex = true;

        [ObservableProperty]
        private bool _writeTexInfo = true;

        [ObservableProperty]
        private bool _writeManifest = true;

        [ObservableProperty]
        private int _concurrency = 1;

        [ObservableProperty]
        private string _statusText = "就绪。拖入 .pkg 文件，或点击「添加 pkg」开始。";

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private bool _isExporting;

        [ObservableProperty]
        private double _progressValue;

        [ObservableProperty]
        private string _progressText = string.Empty;

        [ObservableProperty]
        private string? _previewText;

        [ObservableProperty]
        private string? _previewNote;

        [ObservableProperty]
        private bool _isPaused;

        public Bitmap? PreviewImage
        {
            get => _previewImage;
            private set
            {
                var old = _previewImage;

                if (SetProperty(ref _previewImage, value))
                    old?.Dispose();
            }
        }

        public string PackageCountText => $"{Packages.Count} 个包";

        public bool HasPackages => Packages.Count > 0;

        public void Initialize()
        {
            if (!Directory.Exists(OutputDirectory))
            {
                try
                {
                    Directory.CreateDirectory(OutputDirectory);
                }
                catch (Exception ex)
                {
                    StatusText = $"输出目录不可用：{ex.Message}";
                }
            }
        }

        // ───────────────────────────── 加载 ─────────────────────────────

        public async Task HandleDroppedAsync(IReadOnlyList<string> paths)
        {
            var packages = new List<string>();

            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    packages.AddRange(CollectPackages(path, Recursive));
                }
                else if (File.Exists(path) &&
                         string.Equals(Path.GetExtension(path), ".pkg", StringComparison.OrdinalIgnoreCase))
                {
                    packages.Add(path);
                }
            }

            await LoadAsync(packages);
        }

        public static IEnumerable<string> CollectPackages(string directory, bool recursive)
        {
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            return Directory.EnumerateFiles(directory, "*.pkg", option)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        }

        public async Task LoadAsync(IEnumerable<string> paths)
        {
            var pending = paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => Packages.All(existing => !string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (pending.Count == 0)
            {
                StatusText = "没有新的 pkg 可加载。";

                return;
            }

            IsBusy = true;

            try
            {
                var loaded = 0;
                var failed = 0;

                foreach (var path in pending)
                {
                    StatusText = $"正在读取清单：{Path.GetFileName(path)}…";

                    var package = await Task.Run(() => LoadPackage(path)).ConfigureAwait(true);

                    if (package == null)
                    {
                        failed++;
                        continue;
                    }

                    Packages.Add(package);
                    loaded++;
                }

                OnPropertyChanged(nameof(PackageCountText));
                OnPropertyChanged(nameof(HasPackages));

                if (SelectedPackage == null && Packages.Count > 0)
                    SelectedPackage = Packages[0];

                StatusText = failed == 0
                    ? $"已加载 {loaded} 个包。"
                    : $"已加载 {loaded} 个包，{failed} 个读取失败（详见日志窗口/导出日志）。";

                ExportCommand.NotifyCanExecuteChanged();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private PackageViewModel? LoadPackage(string path)
        {
            try
            {
                using var source = PackageFileSource.Open(path);

                var entries = new List<EntryViewModel>(source.Entries.Count);

                foreach (var entry in source.Entries)
                    entries.Add(new EntryViewModel(entry.FullPath, entry.Length));

                string? title = null;

                try
                {
                    var projectEntry = source.FindEntry("project.json");

                    if (projectEntry != null && projectEntry.Length is > 0 and <= 4 * 1024 * 1024)
                    {
                        using var stream = source.OpenEntry(projectEntry);
                        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                        using var document = System.Text.Json.JsonDocument.Parse(reader.ReadToEnd());

                        if (document.RootElement.TryGetProperty("title", out var titleElement) &&
                            titleElement.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            title = titleElement.GetString();
                        }
                    }
                }
                catch
                {
                    // project.json 缺失/损坏不影响清单加载
                }

                return new PackageViewModel(path, title, entries, source.FileLength);
            }
            catch (Exception ex)
            {
                _post(() => StatusText = $"读取失败：{Path.GetFileName(path)} — {ex.Message}");

                return null;
            }
        }

        [RelayCommand]
        private async Task AddPackagesAsync()
        {
            var files = await _dialogs.PickPackagesAsync();
            await LoadAsync(files);
        }

        [RelayCommand]
        private async Task AddFolderAsync()
        {
            var folder = await _dialogs.PickFolderAsync("选择包含 pkg 的目录");

            if (folder == null)
                return;

            await LoadAsync(CollectPackages(folder, Recursive));
        }

        [RelayCommand]
        private void RemovePackage()
        {
            if (SelectedPackage == null)
                return;

            var removed = SelectedPackage;
            var index = Packages.IndexOf(removed);

            Packages.Remove(removed);
            removed.Detach();

            SelectedPackage = Packages.Count == 0
                ? null
                : Packages[Math.Min(index, Packages.Count - 1)];

            OnPropertyChanged(nameof(PackageCountText));
            OnPropertyChanged(nameof(HasPackages));
            ExportCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void ClearPackages()
        {
            Packages.Clear();
            VisibleEntries.Clear();
            SelectedPackage = null;
            OnPropertyChanged(nameof(PackageCountText));
            OnPropertyChanged(nameof(HasPackages));
            StatusText = "已清空列表。";
            ExportCommand.NotifyCanExecuteChanged();
        }

        // ───────────────────────────── 勾选与筛选 ─────────────────────────────

        [RelayCommand]
        private void SelectAll() => SetAllEntries(true);

        [RelayCommand]
        private void SelectNone() => SetAllEntries(false);

        [RelayCommand]
        private void InvertSelection()
        {
            if (SelectedPackage == null)
                return;

            foreach (var entry in SelectedPackage.Entries)
                entry.IsSelected = entry.IsSelected != true;

            SelectedPackage.RefreshSelectionState();
        }

        private void SetAllEntries(bool selected)
        {
            foreach (var package in Packages)
                package.IsSelected = selected;
        }

        partial void OnSelectedPackageChanged(PackageViewModel? value)
        {
            RefreshVisibleEntries();

            if (value == null)
                SelectedEntry = null;
        }

        partial void OnSelectedFilterChanged(EntryKindFilter value) => RefreshVisibleEntries();

        partial void OnSelectedEntryChanged(EntryViewModel? value) => _ = LoadPreviewAsync(value);

        public void RefreshVisibleEntries()
        {
            VisibleEntries.Clear();

            if (SelectedPackage == null)
                return;

            var kind = SelectedFilter.Kind;

            foreach (var entry in SelectedPackage.Entries)
            {
                if (kind == null || entry.Kind == kind)
                    VisibleEntries.Add(entry);
            }
        }

        private async Task LoadPreviewAsync(EntryViewModel? entry)
        {
            PreviewImage = null;
            PreviewText = null;
            PreviewNote = null;

            if (entry == null || SelectedPackage == null)
                return;

            var packagePath = SelectedPackage.Path;

            try
            {
                var data = await Task.Run(() => PreviewService.Load(packagePath, entry.FullPath, entry.Kind))
                    .ConfigureAwait(true);

                PreviewText = data.Text;
                PreviewNote = data.Note;

                if (data.ImageBytes is {Length: > 0})
                {
                    using var stream = new MemoryStream(data.ImageBytes);
                    PreviewImage = new Bitmap(stream);
                }
            }
            catch (Exception ex)
            {
                PreviewNote = $"预览失败：{ex.Message}";
            }
        }

        // ───────────────────────────── 导出 ─────────────────────────────

        private bool CanExport() => !IsExporting && Packages.Any(package => package.SelectedCount > 0);

        partial void OnIsExportingChanged(bool value)
        {
            ExportCommand.NotifyCanExecuteChanged();
            PauseCommand.NotifyCanExecuteChanged();
            ResumeCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanExport))]
        private async Task ExportAsync()
        {
            if (string.IsNullOrWhiteSpace(OutputDirectory))
            {
                StatusText = "请先选择输出目录。";

                return;
            }

            var targets = Packages.Where(package => package.SelectedCount > 0).ToList();

            if (targets.Count == 0)
                return;

            IsExporting = true;
            IsBusy = true;
            IsPaused = false;
            ProgressValue = 0;
            ProgressText = string.Empty;
            StatusText = $"正在导出 {targets.Count} 个包…";

            _exportCancellation = new CancellationTokenSource();

            var options = new ExportOptions
            {
                OutputDirectory = OutputDirectory,
                Overwrite = Overwrite,
                Concurrency = Math.Clamp(Concurrency, 1, 4),
                QueueCapacity = 16,
                WriteLog = true,
                WriteTexInfo = WriteTexInfo,
                WriteManifest = WriteManifest,
                RollbackOnCancel = false
            };

            IEntryConverter? converter = ConvertTex ? new TexEntryConverter() : null;

            try
            {
                var queue = new ExportQueue(options, NullExportLog.Instance, converter);
                _queue = queue;

                queue.ProgressChanged += progress => _post(() => ApplyProgress(progress));
                queue.JobCompleted += result => _post(() => ApplyJobResult(result));

                ExportCommand.NotifyCanExecuteChanged();

                foreach (var package in targets)
                {
                    var selected = new HashSet<string>(
                        package.SelectedEntries().Select(entry => entry.FullPath),
                        StringComparer.Ordinal);

                    var directoryName = SafeDirectoryName(package);
                    var packageOutput = Path.Combine(OutputDirectory, directoryName);

                    // 逐条目勾选通过 selector 传入队列 —— GUI 不复制导出循环
                    await queue.EnqueueAsync(
                        package.Path,
                        packageOutput,
                        jobLogPath: null,
                        entrySelector: entry => selected.Contains(entry.FullPath),
                        cancellationToken: _exportCancellation.Token);
                }

                var summary = await queue.CompleteAsync();

                // 取消也算「没成功」：先说取消，否则「导出完成 0 个文件」会误导
                if (summary.Canceled > 0)
                {
                    StatusText = $"已取消：成功 {summary.Succeeded} / 跳过 {summary.Skipped} / 失败 {summary.Failed} / 取消 {summary.Canceled}（已完成的条目已保留）。";
                }
                else
                {
                    StatusText = summary.IsSuccess
                        ? $"导出完成：{summary.Succeeded} 个文件，{ByteSize.Format(summary.BytesWritten)}，用时 {summary.Duration.TotalSeconds:0.0}s。"
                        : $"导出结束：成功 {summary.Succeeded} / 跳过 {summary.Skipped} / 失败 {summary.Failed} / 取消 {summary.Canceled}。";
                }

                if (summary.Failed > 0)
                    ProgressText = "存在失败项，详见各包目录下的 export-log.jsonl";

                ProgressValue = 100;
            }
            catch (OperationCanceledException)
            {
                StatusText = "已取消（已完成的条目已保留）。";
            }
            catch (Exception ex)
            {
                StatusText = $"导出失败：{ex.Message}";
            }
            finally
            {
                if (_queue != null)
                {
                    await _queue.DisposeAsync();
                    _queue = null;
                }

                _exportCancellation?.Dispose();
                _exportCancellation = null;

                IsExporting = false;
                IsBusy = false;
                IsPaused = false;
                ExportCommand.NotifyCanExecuteChanged();
            }
        }

        private static string SafeDirectoryName(PackageViewModel package)
        {
            var candidate = package.Title ?? package.Name;

            if (SafePath.TryResolve(Path.GetTempPath(), candidate, out _, out _))
                return candidate;

            return FileNameSanitizer.Flatten(candidate);
        }

        private void ApplyProgress(ExportProgress progress)
        {
            ProgressText = progress.PackagesTotal > 0
                ? $"包 {Math.Min(progress.PackagesDone + 1, progress.PackagesTotal)}/{progress.PackagesTotal} · {progress.CurrentEntry ?? "准备中"}"
                : "准备中…";

            ProgressValue = progress.PackagesTotal > 0
                ? (progress.PackagesDone + progress.EntryFraction) * 100.0 / progress.PackagesTotal
                : 0;
        }

        private void ApplyJobResult(ExportJobResult result)
        {
            var name = Path.GetFileName(result.PackagePath);
            ProgressText = $"{name}：成功 {result.Succeeded} / 跳过 {result.Skipped} / 失败 {result.Failed}";
        }

        [RelayCommand(CanExecute = nameof(IsExporting))]
        private void Pause()
        {
            _queue?.Control.Pause();
            IsPaused = true;
            StatusText = "已暂停（在条目边界生效）。";
        }

        [RelayCommand(CanExecute = nameof(IsExporting))]
        private void Resume()
        {
            _queue?.Control.Resume();
            IsPaused = false;
            StatusText = "已继续。";
        }

        [RelayCommand(CanExecute = nameof(IsExporting))]
        private void Cancel()
        {
            _queue?.Control.Cancel();
            StatusText = "正在取消…";
        }

        [RelayCommand]
        private async Task ChooseOutputAsync()
        {
            var folder = await _dialogs.PickFolderAsync("选择输出目录");

            if (folder != null)
                OutputDirectory = folder;
        }

        [RelayCommand]
        private void RevealOutput()
        {
            if (Directory.Exists(OutputDirectory))
                _dialogs.RevealInFileManager(OutputDirectory);
            else
                StatusText = "输出目录尚不存在（导出后自动创建）。";
        }

        public void Dispose()
        {
            _exportCancellation?.Cancel();
            _exportCancellation?.Dispose();
            _exportCancellation = null;

            PreviewImage = null;
        }
    }

    public sealed class EntryKindFilter
    {
        public EntryKindFilter(string name, EntryKind? kind)
        {
            Name = name;
            Kind = kind;
        }

        public string Name { get; }
        public EntryKind? Kind { get; }

        public override string ToString() => Name;
    }
}

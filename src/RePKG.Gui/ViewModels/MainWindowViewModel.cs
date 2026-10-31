// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 主视图模型：批量加载清单 → 勾选/筛选 → 交给 RePKG.Shared 的 ExportQueue 导出。
// 这里**没有**任何条目复制循环，导出完全走阶段 2–4 的同一套内核。
//
// 2.0.0：界面文案全部走 ILocalizer；换语言时先改资源字典（XAML 那侧），
// 再由本类刷新状态栏、筛选档、条目类型这些 C# 侧派生文案。

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
using RePKG.Shared.Localization;
using RePKG.Shared.Logging;
using RePKG.Shared.Paths;
using RePKG.Shared.Settings;

namespace RePKG.Gui.ViewModels
{
    public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
    {
        private readonly IDialogService _dialogs;
        private readonly ILocalizer _localizer;
        private readonly SettingsStore _settings;
        private readonly Action<Action> _post;
        private CancellationTokenSource? _exportCancellation;
        private ExportQueue? _queue;
        private Bitmap? _previewImage;
        private string _statusKey = "gui.status.ready";
        private object?[] _statusArgs = Array.Empty<object?>();

        /// <summary>最小构造（测试/无设置场景）：内部使用内存设置，不写用户目录。</summary>
        public MainWindowViewModel(IDialogService dialogs, Action<Action>? post = null)
            : this(dialogs, new Localizer(), SettingsStore.InMemory(), post)
        {
        }

        public MainWindowViewModel(
            IDialogService dialogs,
            ILocalizer localizer,
            SettingsStore settings,
            Action<Action>? post = null)
        {
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
            _settings = settings ?? SettingsStore.InMemory();
            _post = post ?? (action => Dispatcher.UIThread.Post(action));

            FilterOptions = new ObservableCollection<EntryKindFilter>();
            BuildFilterOptions();

            OutputDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "RePKG-Enhanced");

            _localizer.LanguageChanged += OnLanguageChanged;
            RenderStatus();
        }

        public ObservableCollection<PackageViewModel> Packages { get; } = new();
        public ObservableCollection<EntryViewModel> VisibleEntries { get; } = new();
        public ObservableCollection<EntryKindFilter> FilterOptions { get; }

        public ILocalizer Localizer => _localizer;

        [ObservableProperty]
        private PackageViewModel? _selectedPackage;

        [ObservableProperty]
        private EntryViewModel? _selectedEntry;

        [ObservableProperty]
        private EntryKindFilter _selectedFilter = null!;

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
        private string _statusText = string.Empty;

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

        public string PackageCountText => _localizer.Format("gui.packageCount", Packages.Count);

        public bool HasPackages => Packages.Count > 0;

        /// <summary>语言菜单当前选中项（"system"/"zh"/"en"），用于单选菜单打勾。</summary>
        public string LanguageSettingValue => AppLanguages.ToSettingValue(_localizer.Setting);

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
                    SetStatus("gui.status.outputMissing", ex.Message);
                }
            }
        }

        // ───────────────────────────── 语言 ─────────────────────────────

        [RelayCommand]
        private void SetLanguage(string? value)
        {
            var language = AppLanguages.FromSettingValue(value);

            _localizer.SetLanguage(language);
            _settings.Save(new AppSettings { Language = language });
            OnPropertyChanged(nameof(LanguageSettingValue));
        }

        private void OnLanguageChanged(object? sender, EventArgs e) => _post(RefreshLanguage);

        /// <summary>换语言后刷新所有 C# 侧派生文案（XAML 侧由资源字典负责）。</summary>
        public void RefreshLanguage()
        {
            var previousKind = SelectedFilter?.Kind;

            BuildFilterOptions(previousKind);

            foreach (var package in Packages)
                package.RefreshLanguage();

            RenderStatus();

            OnPropertyChanged(nameof(PackageCountText));
            OnPropertyChanged(nameof(LanguageSettingValue));
        }

        private void BuildFilterOptions(EntryKind? selected = null)
        {
            FilterOptions.Clear();
            FilterOptions.Add(new EntryKindFilter(_localizer.Get("gui.filter.all"), null));
            FilterOptions.Add(new EntryKindFilter(_localizer.Get("gui.filter.image"), EntryKind.Image));
            FilterOptions.Add(new EntryKindFilter(_localizer.Get("gui.filter.text"), EntryKind.Text));
            FilterOptions.Add(new EntryKindFilter(_localizer.Get("gui.filter.video"), EntryKind.Video));
            FilterOptions.Add(new EntryKindFilter(_localizer.Get("gui.filter.other"), EntryKind.Other));

            SelectedFilter = FilterOptions.FirstOrDefault(option => option.Kind == selected) ?? FilterOptions[0];
        }

        private void SetStatus(string key, params object?[] args)
        {
            _statusKey = key;
            _statusArgs = args;
            RenderStatus();
        }

        private void RenderStatus() => StatusText = _localizer.Format(_statusKey, _statusArgs);

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
                SetStatus("gui.status.noNewPackages");

                return;
            }

            IsBusy = true;

            try
            {
                var loaded = 0;
                var failed = 0;

                foreach (var path in pending)
                {
                    SetStatus("gui.status.readingManifests", Path.GetFileName(path));

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

                if (failed == 0)
                    SetStatus("gui.status.loaded", loaded);
                else
                    SetStatus("gui.status.loadedWithFailures", loaded, failed);

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
                    entries.Add(new EntryViewModel(entry.FullPath, entry.Length, _localizer));

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

                return new PackageViewModel(path, title, entries, source.FileLength, _localizer);
            }
            catch (Exception ex)
            {
                var name = Path.GetFileName(path);

                _post(() => SetStatus("gui.status.readFailed", $"{name} — {ex.Message}"));

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
            var folder = await _dialogs.PickFolderAsync(_localizer.Get("gui.dialog.pickPackageFolder"));

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
            SetStatus("gui.status.cleared");
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
                var data = await Task.Run(() => PreviewService.Load(packagePath, entry.FullPath, entry.Kind, _localizer))
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
                PreviewNote = _localizer.Format("gui.preview.failed", ex.Message);
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
                SetStatus("gui.status.selectOutput");

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
            SetStatus("gui.status.exporting", targets.Count);

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
                    SetStatus("gui.status.canceled", summary.Succeeded, summary.Skipped, summary.Failed, summary.Canceled);
                else if (summary.IsSuccess)
                    SetStatus("gui.status.exportDone", summary.Succeeded, ByteSize.Format(summary.BytesWritten), summary.Duration.TotalSeconds);
                else
                    SetStatus("gui.status.exportDoneWithFailures", summary.Succeeded, summary.Skipped, summary.Failed, ByteSize.Format(summary.BytesWritten), summary.Duration.TotalSeconds);

                if (summary.Failed > 0)
                    ProgressText = _localizer.Format("gui.status.exportFailedHint", summary.Failed);

                ProgressValue = 100;
            }
            catch (OperationCanceledException)
            {
                SetStatus("gui.status.canceledPlain");
            }
            catch (Exception ex)
            {
                SetStatus("gui.status.exportFailed", ex.Message);
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
            var current = progress.CurrentEntry ?? _localizer.Get("gui.progress.preparing");

            ProgressText = progress.PackagesTotal > 0
                ? _localizer.Format("gui.progress.package",
                    Math.Min(progress.PackagesDone + 1, progress.PackagesTotal),
                    progress.PackagesTotal,
                    current)
                : _localizer.Get("gui.progress.preparing");

            ProgressValue = progress.PackagesTotal > 0
                ? (progress.PackagesDone + progress.EntryFraction) * 100.0 / progress.PackagesTotal
                : 0;
        }

        private void ApplyJobResult(ExportJobResult result)
        {
            var name = Path.GetFileName(result.PackagePath);

            ProgressText = _localizer.Format("gui.progress.job", name, result.Succeeded, result.Skipped, result.Failed);
        }

        [RelayCommand(CanExecute = nameof(IsExporting))]
        private void Pause()
        {
            _queue?.Control.Pause();
            IsPaused = true;
            SetStatus("gui.status.paused");
        }

        [RelayCommand(CanExecute = nameof(IsExporting))]
        private void Resume()
        {
            _queue?.Control.Resume();
            IsPaused = false;
            SetStatus("gui.status.resumed");
        }

        [RelayCommand(CanExecute = nameof(IsExporting))]
        private void Cancel()
        {
            _queue?.Control.Cancel();
            SetStatus("gui.status.canceling");
        }

        [RelayCommand]
        private async Task ChooseOutputAsync()
        {
            var folder = await _dialogs.PickFolderAsync(_localizer.Get("gui.dialog.pickOutputFolder"));

            if (folder != null)
            {
                OutputDirectory = folder;
                SetStatus("gui.status.outputSet", folder);
            }
        }

        [RelayCommand]
        private void RevealOutput()
        {
            if (Directory.Exists(OutputDirectory))
                _dialogs.RevealInFileManager(OutputDirectory);
            else
                SetStatus("gui.status.outputNotCreated");
        }

        [RelayCommand]
        private Task ShowAboutAsync() => _dialogs.ShowAboutAsync();

        public void Dispose()
        {
            _localizer.LanguageChanged -= OnLanguageChanged;

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

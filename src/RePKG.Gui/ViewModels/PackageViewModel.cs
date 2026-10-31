// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 一个 pkg 的清单视图模型：条目列表 + 三态勾选 + 统计。

using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RePKG.Shared.Localization;

namespace RePKG.Gui.ViewModels
{
    public sealed partial class PackageViewModel : ObservableObject
    {
        private bool _suppressSelectionPropagation;
        private readonly ILocalizer? _localizer;

        public PackageViewModel(
            string path,
            string? title,
            IReadOnlyList<EntryViewModel> entries,
            long packageSize,
            ILocalizer? localizer = null)
        {
            Path = path;
            Title = string.IsNullOrWhiteSpace(title) ? null : title!.Trim();
            Entries = entries;
            PackageSize = packageSize;
            _localizer = localizer;

            foreach (var entry in entries)
                entry.PropertyChanged += OnEntryPropertyChanged;

            RefreshSelectionState();
        }

        public string Path { get; }

        /// <summary>project.json 的 title（可能为空）。</summary>
        public string? Title { get; }

        public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
        public string DisplayName => Title ?? Name;
        public string FileName => System.IO.Path.GetFileName(Path);
        public IReadOnlyList<EntryViewModel> Entries { get; }
        public long PackageSize { get; }

        public int EntryCount => Entries.Count;
        public long TotalBytes
        {
            get
            {
                var total = 0L;

                foreach (var entry in Entries)
                    total += entry.Length;

                return total;
            }
        }

        public string Summary => _localizer?.Format("gui.package.summary", EntryCount, ByteSize.Format(TotalBytes))
                                 ?? $"{EntryCount} 个条目 · {ByteSize.Format(TotalBytes)}";

        public string PathText => Path;

        [ObservableProperty]
        private bool? _isSelected = true;

        [ObservableProperty]
        private int _selectedCount;

        partial void OnIsSelectedChanged(bool? value)
        {
            if (_suppressSelectionPropagation || value == null)
                return;

            _suppressSelectionPropagation = true;

            try
            {
                foreach (var entry in Entries)
                    entry.IsSelected = value;
            }
            finally
            {
                _suppressSelectionPropagation = false;
            }

            SelectedCount = value == true ? Entries.Count : 0;
        }

        /// <summary>用户勾选单个条目后，重新计算包的三态。</summary>
        public void RefreshSelectionState()
        {
            if (_suppressSelectionPropagation)
                return;

            var selected = 0;

            foreach (var entry in Entries)
            {
                if (entry.IsSelected == true)
                    selected++;
            }

            _suppressSelectionPropagation = true;

            try
            {
                SelectedCount = selected;
                IsSelected = selected == 0
                    ? false
                    : selected == Entries.Count
                        ? true
                        : null;
            }
            finally
            {
                _suppressSelectionPropagation = false;
            }
        }

        public IEnumerable<EntryViewModel> SelectedEntries()
        {
            foreach (var entry in Entries)
            {
                if (entry.IsSelected == true)
                    yield return entry;
            }
        }

        /// <summary>从列表里移除时取消订阅，避免 VM 之间互相持有。</summary>
        public void Detach()
        {
            foreach (var entry in Entries)
                entry.PropertyChanged -= OnEntryPropertyChanged;
        }

        /// <summary>换语言后刷新派生文案（本包与所有条目）。</summary>
        public void RefreshLanguage()
        {
            OnPropertyChanged(nameof(Summary));

            foreach (var entry in Entries)
                entry.RefreshLanguage();
        }

        private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EntryViewModel.IsSelected))
                RefreshSelectionState();
        }
    }
}

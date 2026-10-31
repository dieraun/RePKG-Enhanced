// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 条目视图模型：只保存元数据（路径/长度/类型/勾选），不持有任何字节。
// 2.0.0：类型名走本地化表，换语言时由 RefreshLanguage 通知刷新。

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using RePKG.Shared.Localization;

namespace RePKG.Gui.ViewModels
{
    public sealed partial class EntryViewModel : ObservableObject
    {
        private readonly ILocalizer? _localizer;

        public EntryViewModel(string fullPath, long length, ILocalizer? localizer = null)
        {
            FullPath = fullPath;
            Length = length;
            Kind = EntryKinds.Classify(fullPath);
            DisplayName = fullPath;
            _localizer = localizer;
        }

        public string FullPath { get; }
        public long Length { get; }
        public EntryKind Kind { get; }
        public string DisplayName { get; }

        public string LengthText => ByteSize.Format(Length);

        public string KindText => _localizer?.Get(KindKey) ?? KindKey switch
        {
            "gui.kind.image" => "图片",
            "gui.kind.text" => "文本",
            "gui.kind.video" => "视频",
            _ => "其它"
        };

        /// <summary>本地化键名，供界面与测试共用。</summary>
        public string KindKey => Kind switch
        {
            EntryKind.Image => "gui.kind.image",
            EntryKind.Text => "gui.kind.text",
            EntryKind.Video => "gui.kind.video",
            _ => "gui.kind.other"
        };

        /// <summary>三态：true 导出 / false 不导出 / null 未定（批量勾选时中间态）。</summary>
        [ObservableProperty]
        private bool? _isSelected = true;

        /// <summary>缩进层级，用于在列表中体现 pkg 内目录结构（0 = 顶层）。</summary>
        public int Depth
        {
            get
            {
                var depth = 0;

                foreach (var ch in FullPath)
                {
                    if (ch == '/')
                        depth++;
                }

                return depth;
            }
        }

        public string Indent => Depth == 0 ? string.Empty : new string(' ', Depth * 2);

        public bool Matches(EntryKind kind) => kind == Kind;

        /// <summary>换语言后刷新派生文案。</summary>
        public void RefreshLanguage() => OnPropertyChanged(nameof(KindText));
    }
}

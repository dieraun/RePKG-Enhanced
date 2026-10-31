// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 条目视图模型：只保存元数据（路径/长度/类型/勾选），不持有任何字节。

using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RePKG.Gui.ViewModels
{
    public sealed partial class EntryViewModel : ObservableObject
    {
        public EntryViewModel(string fullPath, long length)
        {
            FullPath = fullPath;
            Length = length;
            Kind = EntryKinds.Classify(fullPath);
            DisplayName = fullPath;
        }

        public string FullPath { get; }
        public long Length { get; }
        public EntryKind Kind { get; }
        public string DisplayName { get; }

        public string LengthText => ByteSize.Format(Length);

        public string KindText => Kind switch
        {
            EntryKind.Image => "图片",
            EntryKind.Text => "文本",
            EntryKind.Video => "视频",
            _ => "其它"
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
    }
}

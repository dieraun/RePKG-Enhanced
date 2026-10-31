// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 窗口代码后置：只处理「拖拽导入」这类必须碰 Avalonia 事件的事，其余全在 ViewModel。
// Avalonia 12 把拖拽载荷换成了 IDataTransfer（DragEventArgs.Data 已移除），取文件用
// DataTransferExtensions.TryGetFiles(IDataTransfer) -> IStorageItem?[]

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using RePKG.Gui.ViewModels;

namespace RePKG.Gui.Views
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private static void OnDragOver(object? sender, DragEventArgs e)
        {
            // 只接受文件系统拖入
            e.DragEffects = HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDrop(object? sender, DragEventArgs e)
        {
            if (DataContext is not MainWindowViewModel viewModel)
                return;

            var items = e.DataTransfer.TryGetFiles();
            if (items == null)
                return;

            var paths = new List<string>(items.Length);
            foreach (var item in items)
            {
                var path = item.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path!);
            }

            if (paths.Count == 0)
                return;

            _ = viewModel.HandleDroppedAsync(paths);
        }

        private static bool HasFiles(DragEventArgs e)
        {
            var items = e.DataTransfer.TryGetFiles();
            return items != null && items.Length > 0;
        }
    }
}

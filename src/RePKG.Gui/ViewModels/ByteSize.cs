// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 字节数格式化：UI 层统一口径（二进制单位）。

using System.Globalization;

namespace RePKG.Gui.ViewModels
{
    public static class ByteSize
    {
        private static readonly string[] Units = {"B", "KiB", "MiB", "GiB", "TiB"};

        public static string Format(long bytes)
        {
            if (bytes < 0)
                return "0 B";

            double value = bytes;
            var unit = 0;

            while (value >= 1024 && unit < Units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0
                ? string.Create(CultureInfo.InvariantCulture, $"{bytes} B")
                : string.Create(CultureInfo.InvariantCulture, $"{value:0.##} {Units[unit]}");
        }
    }
}

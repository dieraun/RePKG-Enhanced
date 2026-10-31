// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 改动点：原版把每一帧都构造成 ImageSharp 的 Image 对象并全部驻留内存；
//         这里改为“逐帧量化 + 立即写盘”，只保留当前帧。

using System;
using System.Collections.Generic;

namespace RePKG.Application.Texture.Gif
{
    /// <summary>
    /// 中位切分（median cut）量化：把 RGBA8888 像素压到 ≤256 色，供 GIF89a 局部色表使用。
    /// 全程只处理一份索引图（W*H 字节）+ 一份 ≤256 色的调色板。
    /// </summary>
    internal static class ColorQuantizer
    {
        public sealed class Result
        {
            public byte[] Indices;
            public byte[] Palette;      // RGB 三元组，长度 = ColorCount * 3
            public int ColorCount;
            public int TransparentIndex; // -1 表示无透明色

            public int GetTableSize()
            {
                var size = 2;
                while (size < ColorCount) size <<= 1;
                return size;
            }
        }

        private const int BucketBits = 5;
        private const int BucketShift = 8 - BucketBits;
        private const int AlphaCutoff = 128;

        public static Result Quantize(ReadOnlySpan<byte> rgba, int pixelCount, bool allowTransparency)
        {
            if (pixelCount <= 0) throw new ArgumentOutOfRangeException(nameof(pixelCount));

            var transparent = allowTransparency && HasTransparency(rgba, pixelCount);
            var capacity = transparent ? 255 : 256;

            // 5 bit/通道分桶：最多 32768 个桶，避免逐像素全色直方图带来的内存与排序开销
            var buckets = new Dictionary<int, Bucket>(Math.Min(1 << 15, pixelCount));

            for (var i = 0; i < pixelCount; i++)
            {
                var offset = i * 4;
                var a = rgba[offset + 3];

                if (transparent && a < AlphaCutoff)
                    continue;

                var r = rgba[offset];
                var g = rgba[offset + 1];
                var b = rgba[offset + 2];
                var key = ((r >> BucketShift) << (BucketBits * 2)) | ((g >> BucketShift) << BucketBits) | (b >> BucketShift);

                if (buckets.TryGetValue(key, out var bucket))
                {
                    bucket.Count++;
                    bucket.R += r;
                    bucket.G += g;
                    bucket.B += b;
                    buckets[key] = bucket;
                }
                else
                {
                    buckets[key] = new Bucket { Count = 1, R = r, G = g, B = b };
                }
            }

            var items = new List<Item>(buckets.Count);

            foreach (var pair in buckets)
            {
                var bucket = pair.Value;
                items.Add(new Item
                {
                    R = (byte) ((bucket.R + bucket.Count / 2) / bucket.Count),
                    G = (byte) ((bucket.G + bucket.Count / 2) / bucket.Count),
                    B = (byte) ((bucket.B + bucket.Count / 2) / bucket.Count),
                    Count = bucket.Count
                });
            }

            var boxes = new List<Box> { new Box(0, items.Count) };
            Split(items, boxes, capacity);

            // 找到最近色时用的是紧凑调色板（从 0 开始），但 GIF 里的索引要整体后移一位给透明色让位，
            // 所以输出调色板必须同样后移：table[0] 留给透明，真实颜色从 table[1] 开始。
            var paletteOffset = transparent ? 1 : 0;
            var colors = new byte[boxes.Count * 3];

            for (var i = 0; i < boxes.Count; i++)
            {
                var color = AverageOf(items, boxes[i]);
                colors[i * 3] = color.R;
                colors[i * 3 + 1] = color.G;
                colors[i * 3 + 2] = color.B;
            }

            var palette = new byte[(boxes.Count + paletteOffset) * 3];

            if (paletteOffset > 0)
                Buffer.BlockCopy(colors, 0, palette, 3, colors.Length);
            else
                palette = colors;

            var indices = new byte[pixelCount];
            var cache = new Dictionary<int, byte>(Math.Min(1 << 16, Math.Max(16, boxes.Count * 64)));

            for (var i = 0; i < pixelCount; i++)
            {
                var offset = i * 4;

                if (transparent && rgba[offset + 3] < AlphaCutoff)
                {
                    indices[i] = 0;
                    continue;
                }

                var r = rgba[offset];
                var g = rgba[offset + 1];
                var b = rgba[offset + 2];
                var key = (r << 16) | (g << 8) | b;

                if (!cache.TryGetValue(key, out var index))
                {
                    index = (byte) (FindNearest(colors, boxes.Count, r, g, b) + paletteOffset);
                    cache[key] = index;
                }

                indices[i] = index;
            }

            return new Result
            {
                Indices = indices,
                Palette = palette,
                ColorCount = boxes.Count + paletteOffset,
                TransparentIndex = transparent ? 0 : -1
            };
        }

        private static bool HasTransparency(ReadOnlySpan<byte> rgba, int pixelCount)
        {
            for (var i = 0; i < pixelCount; i++)
            {
                if (rgba[i * 4 + 3] < AlphaCutoff) return true;
            }

            return false;
        }

        private struct Bucket
        {
            public int Count;
            public int R;
            public int G;
            public int B;
        }

        private struct Item
        {
            public byte R;
            public byte G;
            public byte B;
            public int Count;
        }

        private struct Box
        {
            public Box(int start, int end)
            {
                Start = start;
                End = end;
            }

            public int Start;
            public int End;

            public int Length => End - Start;
        }

        private static void Split(List<Item> items, List<Box> boxes, int capacity)
        {
            while (boxes.Count < capacity)
            {
                var target = -1;
                var targetRange = 0;
                var targetChannel = 0;

                for (var i = 0; i < boxes.Count; i++)
                {
                    var box = boxes[i];

                    if (box.Length < 2) continue;

                    var (rRange, gRange, bRange) = Ranges(items, box);
                    var best = rRange;
                    var channel = 0;

                    if (gRange > best)
                    {
                        best = gRange;
                        channel = 1;
                    }

                    if (bRange > best)
                    {
                        best = bRange;
                        channel = 2;
                    }

                    if (best > targetRange)
                    {
                        targetRange = best;
                        target = i;
                        targetChannel = channel;
                    }
                }

                if (target < 0 || targetRange == 0) break;

                var boxToSplit = boxes[target];
                var slice = items.GetRange(boxToSplit.Start, boxToSplit.Length);
                slice.Sort((x, y) => Channel(x, targetChannel).CompareTo(Channel(y, targetChannel)));

                for (var i = 0; i < slice.Count; i++) items[boxToSplit.Start + i] = slice[i];

                var total = 0;

                for (var i = boxToSplit.Start; i < boxToSplit.End; i++) total += items[i].Count;

                var running = 0;
                var splitAt = boxToSplit.Start + 1;

                for (var i = boxToSplit.Start; i < boxToSplit.End - 1; i++)
                {
                    running += items[i].Count;

                    if (running * 2 >= total)
                    {
                        splitAt = i + 1;
                        break;
                    }
                }

                if (splitAt <= boxToSplit.Start) splitAt = boxToSplit.Start + 1;
                if (splitAt >= boxToSplit.End) splitAt = boxToSplit.End - 1;

                boxes[target] = new Box(boxToSplit.Start, splitAt);
                boxes.Add(new Box(splitAt, boxToSplit.End));
            }
        }

        private static int Channel(in Item item, int channel)
        {
            switch (channel)
            {
                case 0: return item.R;
                case 1: return item.G;
                default: return item.B;
            }
        }

        private static (int R, int G, int B) Ranges(List<Item> items, Box box)
        {
            int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;

            for (var i = box.Start; i < box.End; i++)
            {
                var item = items[i];

                if (item.R < minR) minR = item.R;
                if (item.R > maxR) maxR = item.R;
                if (item.G < minG) minG = item.G;
                if (item.G > maxG) maxG = item.G;
                if (item.B < minB) minB = item.B;
                if (item.B > maxB) maxB = item.B;
            }

            return (maxR - minR, maxG - minG, maxB - minB);
        }

        private static (byte R, byte G, byte B) AverageOf(List<Item> items, Box box)
        {
            long r = 0, g = 0, b = 0, count = 0;

            for (var i = box.Start; i < box.End; i++)
            {
                var item = items[i];
                r += (long) item.R * item.Count;
                g += (long) item.G * item.Count;
                b += (long) item.B * item.Count;
                count += item.Count;
            }

            if (count == 0) return (0, 0, 0);

            return ((byte) ((r + count / 2) / count), (byte) ((g + count / 2) / count), (byte) ((b + count / 2) / count));
        }

        private static int FindNearest(byte[] palette, int colorCount, byte r, byte g, byte b)
        {
            var best = 0;
            var bestDistance = int.MaxValue;

            for (var i = 0; i < colorCount; i++)
            {
                var dr = palette[i * 3] - r;
                var dg = palette[i * 3 + 1] - g;
                var db = palette[i * 3 + 2] - b;
                var distance = dr * dr + dg * dg + db * db;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;

                    if (distance == 0) break;
                }
            }

            return best;
        }
    }
}

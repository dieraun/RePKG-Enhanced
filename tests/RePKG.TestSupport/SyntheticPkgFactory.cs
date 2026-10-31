using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using RePKG.Application.Package;

namespace RePKG.TestSupport
{
    /// <summary>
    /// 合成 pkg 生成器：在**常量内存**下产出任意大小的合法 pkg。
    ///
    /// pkg 布局（与原版 PackageReader/PackageWriter 一致）：
    ///   [int32 魔数字节数][魔数 UTF-8]
    ///   [int32 条目数]
    ///   每个条目：[int32 路径字节数][路径 UTF-8][uint32 偏移][uint32 长度]
    ///   [数据区：各条目字节连续排列]
    ///
    /// 之所以需要它：本项目没有真实 WE 样本（阶段 0 - Q9），大文件门禁必须靠可重复合成数据。
    /// </summary>
    public static class SyntheticPkgFactory
    {
        public const string DefaultMagic = "PKGV0001";

        /// <summary>描述一个合成条目。</summary>
        public readonly struct EntrySpec
        {
            public EntrySpec(string fullPath, long length)
            {
                FullPath = fullPath;
                Length = length;
            }

            public string FullPath { get; }
            public long Length { get; }
        }

        /// <summary>全部条目等长的便捷重载。</summary>
        public static void Create(string path, IReadOnlyList<string> entryPaths, long entryLength, ulong seed = 0x9E37_79B9_7F4A_7C15UL)
        {
            var specs = new List<EntrySpec>(entryPaths.Count);
            foreach (var p in entryPaths)
                specs.Add(new EntrySpec(p, entryLength));

            Create(path, specs, seed);
        }

        /// <summary>
        /// 生成 pkg。写入过程只占用一个 1 MiB 缓冲，因此条目总大小可以远超可用内存。
        /// </summary>
        public static void Create(string path, IReadOnlyList<EntrySpec> entries, ulong seed = 0x9E37_79B9_7F4A_7C15UL)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (entries.Count == 0) throw new ArgumentException("Need at least one entry", nameof(entries));

            var filler = new DeterministicFiller(seed);
            var header = BuildHeader(DefaultMagic, entries);
            var dataStart = header.Length;

            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.SequentialScan);
            stream.Write(header, 0, header.Length);

            var buffer = new byte[1 << 20];
            long absoluteOffset = 0;

            foreach (var entry in entries)
            {
                var remaining = entry.Length;
                while (remaining > 0)
                {
                    var chunk = (int) Math.Min(buffer.Length, remaining);
                    filler.Fill(buffer.AsSpan(0, chunk), absoluteOffset);
                    stream.Write(buffer, 0, chunk);
                    absoluteOffset += chunk;
                    remaining -= chunk;
                }
            }

            stream.Flush();
            if (stream.Length != dataStart + absoluteOffset)
                throw new InvalidOperationException("Synthetic pkg size mismatch");
        }

        /// <summary>只生成头部（用于构造截断/畸形包）。</summary>
        public static byte[] BuildHeader(string magic, IReadOnlyList<EntrySpec> entries)
        {
            var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                var magicBytes = Encoding.UTF8.GetBytes(magic);
                writer.Write(magicBytes.Length);
                writer.Write(magicBytes);

                writer.Write(entries.Count);

                long offset = 0;
                foreach (var entry in entries)
                {
                    var pathBytes = Encoding.UTF8.GetBytes(entry.FullPath);
                    writer.Write(pathBytes.Length);
                    writer.Write(pathBytes);
                    writer.Write((uint) offset);
                    writer.Write((uint) entry.Length);
                    offset += entry.Length;
                }
            }

            return ms.ToArray();
        }

        /// <summary>
        /// 计算某个条目应有的 SHA-256（流式，常量内存）。写入端与校验端共用同一 filler，故结果必须一致。
        /// </summary>
        public static byte[] ComputeExpectedHash(long length, ulong seed = 0x9E37_79B9_7F4A_7C15UL)
        {
            var filler = new DeterministicFiller(seed);
            using var sha = SHA256.Create();
            var buffer = new byte[1 << 20];
            long filled = 0;

            while (filled < length)
            {
                var chunk = (int) Math.Min(buffer.Length, length - filled);
                filler.Fill(buffer.AsSpan(0, chunk), filled);
                sha.TransformBlock(buffer, 0, chunk, null, 0);
                filled += chunk;
            }

            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return sha.Hash!;
        }

        /// <summary>流式计算文件（或文件的某个区间）的 SHA-256。</summary>
        public static byte[] ComputeFileHash(string path, long offset = 0, long length = -1)
        {
            using var sha = SHA256.Create();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
            if (length < 0) length = stream.Length - offset;

            stream.Seek(offset, SeekOrigin.Begin);
            var buffer = new byte[1 << 20];
            var remaining = length;

            while (remaining > 0)
            {
                var chunk = (int) Math.Min(buffer.Length, remaining);
                var read = stream.Read(buffer, 0, chunk);
                if (read <= 0) throw new EndOfStreamException();
                sha.TransformBlock(buffer, 0, read, null, 0);
                remaining -= read;
            }

            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return sha.Hash!;
        }

        /// <summary>返回一个用于大文件测试的临时工作目录（可用 REPKG_BIG_TMP 覆盖）。</summary>
        public static string CreateWorkDirectory(string name)
        {
            var root = Environment.GetEnvironmentVariable("REPKG_BIG_TMP");
            if (string.IsNullOrWhiteSpace(root))
                root = Path.Combine(Path.GetTempPath(), "repkg-bigfiles");

            var dir = Path.Combine(root, name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>尽力删除目录（测试清理不应因文件锁而失败）。</summary>
        public static void TryDeleteDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // 忽略：临时目录残留不影响断言
            }
        }

        /// <summary>通过 PackageFileSource 读取某个条目的字节（仅用于小样本）。</summary>
        public static byte[] ReadEntryBytes(string pkgPath, string fullPath)
        {
            using var source = PackageFileSource.Open(pkgPath);
            var entry = source.FindEntry(fullPath)
                        ?? throw new InvalidOperationException($"Entry `{fullPath}` not found");

            var buffer = new byte[entry.Length];
            using var entryStream = source.OpenEntry(entry);
            var read = 0;
            while (read < buffer.Length)
            {
                var n = entryStream.Read(buffer, read, buffer.Length - read);
                if (n <= 0) throw new EndOfStreamException();
                read += n;
            }

            return buffer;
        }
    }
}

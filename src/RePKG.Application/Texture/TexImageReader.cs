using System;
using System.IO;
using RePKG.Application.Exceptions;
using RePKG.Core.Texture;

namespace RePKG.Application.Texture
{
    public class TexImageReader : ITexImageReader
    {
        protected readonly ITexMipmapDecompressor _texMipmapDecompressor;
        public bool ReadMipmapBytes { get; set; } = true;
        public bool DecompressMipmapBytes { get; set; } = true;

        public TexImageReader(ITexMipmapDecompressor texMipmapDecompressor)
        {
            _texMipmapDecompressor = texMipmapDecompressor;
        }

        public ITexImage ReadFrom(
            BinaryReader reader,
            ITexImageContainer container,
            TexFormat texFormat)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            if (container == null) throw new ArgumentNullException(nameof(container));
            
            if (!texFormat.IsValid())
                throw new EnumNotValidException<TexFormat>(texFormat);

            var mipmapCount = reader.ReadInt32();
            
            if (mipmapCount > Constants.MaximumMipmapCount)
                throw new UnsafeTexException(
                    $"Mipmap count exceeds limit: {mipmapCount}/{Constants.MaximumMipmapCount}");
            
            var readFunction = PickMipmapReader(container.ImageContainerVersion);
            var format = TexMipmapFormatGetter.GetFormatForTex(container.ImageFormat, texFormat);
            var image = new TexImage();
            
            for (var i = 0; i < mipmapCount; i++)
            {
                var mipmap = readFunction(reader);
                mipmap.Format = format;

                if (DecompressMipmapBytes)
                    _texMipmapDecompressor.DecompressMipmap(mipmap);

                image.Mipmaps.Add(mipmap);
            }

            return image;
        }

        private TexMipmap ReadMipmapV1(BinaryReader reader)
        {
            return new TexMipmap
            {
                Width = reader.ReadInt32(),
                Height = reader.ReadInt32(),
                Bytes = ReadBytes(reader)
            };
        }

        private TexMipmap ReadMipmapV2And3(BinaryReader reader)
        {
            return new TexMipmap
            {
                Width = reader.ReadInt32(),
                Height = reader.ReadInt32(),
                IsLZ4Compressed = reader.ReadInt32() == 1,
                DecompressedBytesCount = reader.ReadInt32(),
                Bytes = ReadBytes(reader)
            };
        }
        private TexMipmap ReadMipmapV4(BinaryReader reader)
        {
            /**FIXME
             * The role of the following param* parameters cannot be confirmed, 
             * it may be a parameter used in the built-in display of the wallpaper editor and does not need to be processed
             */
            var param1 = reader.ReadInt32();
            if(param1 != 1)
            {
                throw new UnsafeTexException($"ReadMipmapV4 unknow param1 :{param1}");
            }
            var param2= reader.ReadInt32();
            if (param2 != 2)
            {
                throw new UnsafeTexException($"ReadMipmapV4 unknow param2 :{param2}");
            }
            var conditionJson = reader.ReadNString();
            
            var param3 = reader.ReadInt32();
            if (param3 != 1)
            {
                throw new UnsafeTexException($"ReadMipmapV4 unknow param3 :{param3}");
            }
            return new TexMipmap
            {
                Width = reader.ReadInt32(),
                Height = reader.ReadInt32(),
                IsLZ4Compressed = reader.ReadInt32() == 1,
                DecompressedBytesCount = reader.ReadInt32(),
                Bytes = ReadBytes(reader)
            };
        }
        private byte[] ReadBytes(BinaryReader reader)
        {
            var byteCount = reader.ReadInt32();

            if (byteCount < 0)
                throw new UnsafeTexException($"Detected invalid mipmap byte count: {byteCount}");

            if (byteCount > Constants.MaximumMipmapByteCount)
                throw new UnsafeTexException(
                    $"Mipmap byte count exceeds maximum size: {byteCount}/{Constants.MaximumMipmapByteCount}");

            var stream = reader.BaseStream;

            if (stream.CanSeek && stream.Position + byteCount > stream.Length)
                throw new UnsafeTexException("Detected invalid mipmap byte count - exceeds stream length");

            if (!ReadMipmapBytes)
            {
                SkipBytes(stream, byteCount);
                return null;
            }

            var bytes = new byte[byteCount];
            ReadExactly(stream, bytes);

            return bytes;
        }

        /// <summary>
        /// 上游用单次 Read 读取整块 mipmap：FileStream/NetworkStream 都可能短读，短读被当成错误。
        /// 这里改为循环读满，只有真正读到流末尾才报错。
        /// </summary>
        private static void ReadExactly(Stream stream, byte[] buffer)
        {
            var offset = 0;

            while (offset < buffer.Length)
            {
                var read = stream.Read(buffer, offset, buffer.Length - offset);

                if (read <= 0)
                    throw new EndOfStreamException(
                        $"Failed to read mipmap bytes: stream ended after {offset} of {buffer.Length} bytes");
                
                offset += read;
            }
        }

        private static void SkipBytes(Stream stream, int byteCount)
        {
            if (byteCount == 0) return;

            if (stream.CanSeek)
            {
                stream.Seek(byteCount, SeekOrigin.Current);
                return;
            }

            var buffer = new byte[Math.Min(byteCount, Constants.DefaultCopyBufferSize)];
            var remaining = byteCount;

            while (remaining > 0)
            {
                var read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));

                if (read <= 0)
                    throw new EndOfStreamException(
                        $"Failed to skip mipmap bytes: stream ended while skipping {byteCount} bytes");

                remaining -= read;
            }
        }

        private Func<BinaryReader, TexMipmap> PickMipmapReader(TexImageContainerVersion containerVersion)
        {
            switch (containerVersion)
            {
                case TexImageContainerVersion.Version1:
                    return ReadMipmapV1;

                case TexImageContainerVersion.Version2:
                case TexImageContainerVersion.Version3:
                    return ReadMipmapV2And3;
                    
                case TexImageContainerVersion.Version4:
                    return ReadMipmapV4;
                default:
                    throw new InvalidOperationException(
                        $"Tex image container version: {containerVersion} is not supported!");
            }
        }
    }
}

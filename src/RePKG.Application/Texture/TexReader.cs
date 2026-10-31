using System;
using System.IO;
using RePKG.Application.Exceptions;
using RePKG.Core.Texture;

namespace RePKG.Application.Texture
{
    public class TexReader : ITexReader
    {
        private readonly ITexHeaderReader _texHeaderReader;
        private readonly ITexImageContainerReader _texImageContainerReader;
        private readonly ITexFrameInfoContainerReader _texFrameInfoContainerReader;

        public TexReader(
            ITexHeaderReader texHeaderReader,
            ITexImageContainerReader texImageContainerReader,
            ITexFrameInfoContainerReader texFrameInfoContainerReader)
        {
            _texHeaderReader = texHeaderReader;
            _texImageContainerReader = texImageContainerReader;
            _texFrameInfoContainerReader = texFrameInfoContainerReader;
        }

        public static TexReader Default
        {
            get
            {
                var headerReader = new TexHeaderReader();
                var mipmapDecompressor = new TexMipmapDecompressor();
                var mipmapReader = new TexImageReader(mipmapDecompressor);
                var containerReader = new TexImageContainerReader(mipmapReader);
                var frameInfoReader = new TexFrameInfoContainerReader();

                return new TexReader(headerReader, containerReader, frameInfoReader);
            }
        }

        /// <summary>
        /// 便捷重载：直接从任意流读取一个 TEX（不复制整份字节，也不关闭调用方的流）。
        /// </summary>
        public ITex ReadFrom(Stream stream, bool leaveOpen = true)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen))
                return ReadFrom(reader);
        }

        public ITex ReadFrom(BinaryReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            var tex = new Tex {Magic1 = reader.ReadNString(maxLength: 16)};

            if (tex.Magic1 != "TEXV0005")
                throw new UnknownMagicException(nameof(TexReader), nameof(tex.Magic1), tex.Magic1);

            tex.Magic2 = reader.ReadNString(maxLength: 16);

            if (tex.Magic2 != "TEXI0001")
                throw new UnknownMagicException(nameof(TexReader), nameof(tex.Magic2), tex.Magic2);

            tex.Header = _texHeaderReader.ReadFrom(reader);
            tex.ImagesContainer = _texImageContainerReader.ReadFrom(reader, tex.Header.Format);

            if (tex.IsGif)
                tex.FrameInfoContainer = _texFrameInfoContainerReader.ReadFrom(reader);

            return tex;
        }
    }
}
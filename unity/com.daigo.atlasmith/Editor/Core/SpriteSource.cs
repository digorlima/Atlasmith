#nullable enable

using System;

namespace Atlasmith.Core
{
    /// <summary>Um sprite pronto para o atlas, em BGRA32 sem premultiplicação.</summary>
    public sealed class SpriteSource
    {
        public SpriteSource(string name, int width, int height, byte[] pixels, string fullPath = "")
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (pixels.Length != (long)width * height * 4) throw new ArgumentException("pixels.Length != width * height * 4", nameof(pixels));

            Name = name;
            Width = width;
            Height = height;
            Pixels = pixels;
            FullPath = fullPath;
        }

        /// <summary>Nome do sprite no atlas.</summary>
        public string Name { get; }

        public int Width { get; }
        public int Height { get; }

        /// <summary>Pixels BGRA32, stride = Width * 4.</summary>
        public byte[] Pixels { get; }

        /// <summary>De onde o sprite veio (o arquivo de imagem ou o .kra), para mensagens.</summary>
        public string FullPath { get; }

        public long Area => (long)Width * Height;
    }

    /// <summary>O resultado de ler a origem: os sprites, o que falhou e as observações.</summary>
    public sealed class LoadReport
    {
        public LoadReport(SpriteSource[] sprites, string[] failures, string[]? notes = null)
        {
            Sprites = sprites;
            Failures = failures;
            Notes = notes ?? Array.Empty<string>();
        }

        public SpriteSource[] Sprites { get; }

        /// <summary>Sprites que não puderam ser lidos ("arquivo: motivo").</summary>
        public string[] Failures { get; }

        /// <summary>Observações que não impedem a geração: camadas ignoradas, recursos não suportados.</summary>
        public string[] Notes { get; }
    }
}

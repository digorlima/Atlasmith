#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;

namespace Atlasmith.Core
{
    /// <summary>Monta o bitmap final do atlas (BGRA32).</summary>
    public static class AtlasComposer
    {
        /// <summary>Desenha os sprites nas posições calculadas. Devolve o buffer BGRA32 do atlas.</summary>
        public static byte[] Compose(
            IReadOnlyList<SpriteSource> sprites,
            PackResult result,
            int extrude,
            CancellationToken ct = default)
        {
            int atlasW = result.Width;
            int atlasH = result.Height;
            var buffer = new byte[(long)atlasW * atlasH * 4];
            int stride = atlasW * 4;

            foreach (var p in result.Placements)
            {
                ct.ThrowIfCancellationRequested();
                var sprite = sprites[p.Index];

                Blit(buffer, stride, atlasW, atlasH, sprite, p.X, p.Y);

                if (extrude > 0)
                    Extrude(buffer, stride, atlasW, atlasH, p, extrude);
            }

            return buffer;
        }

        private static void Blit(
            byte[] dst, int dstStride, int dstW, int dstH,
            SpriteSource sprite, int x, int y)
        {
            int srcStride = sprite.Width * 4;
            int copyBytes = Math.Min(srcStride, (dstW - x) * 4);
            if (copyBytes <= 0) return;

            for (int row = 0; row < sprite.Height; row++)
            {
                int destY = y + row;
                if (destY < 0 || destY >= dstH) continue;
                Buffer.BlockCopy(
                    sprite.Pixels, row * srcStride,
                    dst, destY * dstStride + x * 4,
                    copyBytes);
            }
        }

        /// <summary>
        /// Repete as bordas do sprite para fora do seu retângulo.
        /// Evita que o filtro bilinear puxe pixels do vizinho no atlas.
        /// </summary>
        private static void Extrude(
            byte[] buf, int stride, int atlasW, int atlasH, Placement p, int extrude)
        {
            // Laterais esquerda e direita.
            for (int row = 0; row < p.H; row++)
            {
                int y = p.Y + row;
                if (y < 0 || y >= atlasH) continue;
                int rowStart = y * stride;

                CopyPixelRun(buf, rowStart + p.X * 4, rowStart, p.X - extrude, extrude, atlasW, stride, y);
                CopyPixelRun(buf, rowStart + (p.X + p.W - 1) * 4, rowStart, p.X + p.W, extrude, atlasW, stride, y);
            }

            // Topo e base, já incluindo os cantos porque as laterais foram preenchidas antes.
            int left = Math.Max(0, p.X - extrude);
            int right = Math.Min(atlasW, p.X + p.W + extrude);
            int runBytes = (right - left) * 4;
            if (runBytes <= 0) return;

            for (int e = 1; e <= extrude; e++)
            {
                int top = p.Y - e;
                if (top >= 0)
                    Buffer.BlockCopy(buf, p.Y * stride + left * 4, buf, top * stride + left * 4, runBytes);

                int bottom = p.Y + p.H - 1 + e;
                if (bottom < atlasH)
                    Buffer.BlockCopy(buf, (p.Y + p.H - 1) * stride + left * 4, buf, bottom * stride + left * 4, runBytes);
            }
        }

        private static void CopyPixelRun(
            byte[] buf, int sourcePixelOffset, int rowStart,
            int startX, int count, int atlasW, int stride, int y)
        {
            for (int i = 0; i < count; i++)
            {
                int x = startX + i;
                if (x < 0 || x >= atlasW) continue;
                Buffer.BlockCopy(buf, sourcePixelOffset, buf, rowStart + x * 4, 4);
            }
        }
    }
}

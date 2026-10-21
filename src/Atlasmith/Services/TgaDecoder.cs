using System.IO;

using Atlasmith.Core;

namespace Atlasmith.Services;

/// <summary>
/// Decoder mínimo de TGA — o WIC não abre esse formato, e ele ainda é comum em pipelines
/// de arte para jogos. Cobre truecolor e grayscale, com e sem RLE.
/// </summary>
internal static class TgaDecoder
{
    public static SpriteSource Decode(string path, string name)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < 18) throw new InvalidDataException("TGA truncado.");

        byte idLength = data[0];
        byte colorMapType = data[1];
        byte imageType = data[2];
        ushort colorMapLength = BitConverter.ToUInt16(data, 5);
        byte colorMapEntrySize = data[7];
        int width = BitConverter.ToUInt16(data, 12);
        int height = BitConverter.ToUInt16(data, 14);
        byte depth = data[16];
        byte descriptor = data[17];

        if (colorMapType != 0)
            throw new NotSupportedException("TGA com paleta não é suportado.");
        if (imageType is not (2 or 3 or 10 or 11))
            throw new NotSupportedException($"TGA tipo {imageType} não é suportado.");
        if (depth is not (8 or 24 or 32))
            throw new NotSupportedException($"TGA de {depth} bits não é suportado.");
        if (width <= 0 || height <= 0)
            throw new InvalidDataException("TGA sem dimensões válidas.");

        int offset = 18 + idLength + colorMapLength * (colorMapEntrySize / 8);
        int bytesPerPixel = depth / 8;
        bool rle = imageType is 10 or 11;

        var pixels = new byte[(long)width * height * 4];
        var raw = new byte[(long)width * height * bytesPerPixel];

        if (rle)
            DecodeRle(data, offset, raw, bytesPerPixel);
        else
            Array.Copy(data, offset, raw, 0, Math.Min(raw.Length, data.Length - offset));

        // Bit 5 do descriptor: 0 = primeira linha do arquivo é a de baixo.
        bool topDown = (descriptor & 0x20) != 0;

        for (int y = 0; y < height; y++)
        {
            int srcRow = topDown ? y : height - 1 - y;
            int src = srcRow * width * bytesPerPixel;
            int dst = y * width * 4;

            for (int x = 0; x < width; x++)
            {
                byte b, g, r, a;
                if (bytesPerPixel == 1)
                {
                    b = g = r = raw[src];
                    a = 255;
                }
                else
                {
                    b = raw[src];
                    g = raw[src + 1];
                    r = raw[src + 2];
                    a = bytesPerPixel == 4 ? raw[src + 3] : (byte)255;
                }

                pixels[dst] = b;
                pixels[dst + 1] = g;
                pixels[dst + 2] = r;
                pixels[dst + 3] = a;

                src += bytesPerPixel;
                dst += 4;
            }
        }

        return new SpriteSource(name, width, height, pixels, path);
    }

    private static void DecodeRle(byte[] data, int offset, byte[] output, int bpp)
    {
        int src = offset;
        int dst = 0;

        while (dst < output.Length && src < data.Length)
        {
            byte packet = data[src++];
            int count = (packet & 0x7F) + 1;

            if ((packet & 0x80) != 0)
            {
                // Run-length: um pixel repetido count vezes.
                if (src + bpp > data.Length) break;
                for (int i = 0; i < count && dst < output.Length; i++)
                {
                    Array.Copy(data, src, output, dst, Math.Min(bpp, output.Length - dst));
                    dst += bpp;
                }
                src += bpp;
            }
            else
            {
                // Raw: count pixels literais.
                int bytes = Math.Min(count * bpp, Math.Min(output.Length - dst, data.Length - src));
                Array.Copy(data, src, output, dst, bytes);
                src += count * bpp;
                dst += bytes;
            }
        }
    }
}

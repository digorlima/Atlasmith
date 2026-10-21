#nullable enable

using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace Atlasmith.Core
{
    /// <summary>
    /// Grava PNG RGBA de 8 bits sem depender de biblioteca de imagem: o mesmo código
    /// serve ao app e ao Unity, então o atlas sai igual dos dois lados.
    /// Cada linha usa o filtro que deixa os bytes menores (heurística da soma dos
    /// valores absolutos, a mesma da libpng), e o zlib é o deflate do .NET.
    /// </summary>
    public static class PngWriter
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        public static void Save(string path, byte[] bgra, int width, int height)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            Write(stream, bgra, width, height);
        }

        public static byte[] Encode(byte[] bgra, int width, int height)
        {
            using var stream = new MemoryStream();
            Write(stream, bgra, width, height);
            return stream.ToArray();
        }

        public static void Write(Stream output, byte[] bgra, int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (bgra.Length != (long)width * height * 4) throw new ArgumentException("bgra.Length != width * height * 4", nameof(bgra));

            output.Write(Signature, 0, Signature.Length);

            var header = new byte[13];
            WriteUInt32(header, 0, (uint)width);
            WriteUInt32(header, 4, (uint)height);
            header[8] = 8;  // bits por canal
            header[9] = 6;  // RGBA
            header[10] = 0; // deflate
            header[11] = 0; // filtros adaptativos
            header[12] = 0; // sem entrelaçamento
            WriteChunk(output, "IHDR", header, header.Length);

            byte[] filtered = Filter(bgra, width, height);
            byte[] zlib = Deflate(filtered);
            WriteChunk(output, "IDAT", zlib, zlib.Length);

            WriteChunk(output, "IEND", Array.Empty<byte>(), 0);
        }

        /// <summary>As linhas já filtradas, cada uma precedida do byte do filtro escolhido.</summary>
        private static byte[] Filter(byte[] bgra, int width, int height)
        {
            int stride = width * 4;
            var rgba = new byte[(long)stride * height];

            for (int i = 0; i < rgba.Length; i += 4)
            {
                rgba[i] = bgra[i + 2];
                rgba[i + 1] = bgra[i + 1];
                rgba[i + 2] = bgra[i];
                rgba[i + 3] = bgra[i + 3];
            }

            var output = new byte[(long)(stride + 1) * height];

            // Cada linha depende só dos bytes crus dela e da anterior: dá para filtrar em paralelo.
            Parallel.For(0, height, () => new byte[5 * stride], (y, _, scratch) =>
            {
                int row = y * stride;
                int prev = (y - 1) * stride;
                long best = long.MaxValue;
                int bestFilter = 0;

                for (int f = 0; f < 5; f++)
                {
                    long sum = 0;
                    int o = f * stride;

                    for (int x = 0; x < stride; x++)
                    {
                        int raw = rgba[row + x];
                        int a = x >= 4 ? rgba[row + x - 4] : 0;
                        int b = y > 0 ? rgba[prev + x] : 0;
                        int c = x >= 4 && y > 0 ? rgba[prev + x - 4] : 0;

                        int value;
                        switch (f)
                        {
                            case 0: value = raw; break;
                            case 1: value = raw - a; break;
                            case 2: value = raw - b; break;
                            case 3: value = raw - ((a + b) >> 1); break;
                            default: value = raw - Paeth(a, b, c); break;
                        }

                        byte v = (byte)value;
                        scratch[o + x] = v;
                        sum += v < 128 ? v : 256 - v;
                    }

                    if (sum < best)
                    {
                        best = sum;
                        bestFilter = f;
                    }
                }

                long dst = (long)y * (stride + 1);
                output[dst] = (byte)bestFilter;
                Buffer.BlockCopy(scratch, bestFilter * stride, output, (int)dst + 1, stride);
                return scratch;
            }, _ => { });

            return output;
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return a;
            return pb <= pc ? b : c;
        }

        /// <summary>Fluxo zlib: cabeçalho, deflate cru e Adler-32 dos dados originais.</summary>
        private static byte[] Deflate(byte[] data)
        {
            using var buffer = new MemoryStream(data.Length / 2 + 64);
            buffer.WriteByte(0x78);
            buffer.WriteByte(0xDA);

            using (var deflate = new DeflateStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
                deflate.Write(data, 0, data.Length);

            uint adler = Adler32(data);
            buffer.WriteByte((byte)(adler >> 24));
            buffer.WriteByte((byte)(adler >> 16));
            buffer.WriteByte((byte)(adler >> 8));
            buffer.WriteByte((byte)adler);
            return buffer.ToArray();
        }

        private static uint Adler32(byte[] data)
        {
            const uint Mod = 65521;
            uint a = 1, b = 0;
            int i = 0;

            while (i < data.Length)
            {
                // 5552 é o maior bloco que não estoura 32 bits antes do módulo.
                int end = Math.Min(data.Length, i + 5552);
                for (; i < end; i++)
                {
                    a += data[i];
                    b += a;
                }
                a %= Mod;
                b %= Mod;
            }

            return (b << 16) | a;
        }

        private static void WriteChunk(Stream output, string type, byte[] data, int length)
        {
            var head = new byte[8];
            WriteUInt32(head, 0, (uint)length);
            for (int i = 0; i < 4; i++) head[4 + i] = (byte)type[i];
            output.Write(head, 0, 8);
            output.Write(data, 0, length);

            uint crc = Crc32.Update(0xFFFFFFFFu, head, 4, 4);
            crc = Crc32.Update(crc, data, 0, length) ^ 0xFFFFFFFFu;

            var tail = new byte[4];
            WriteUInt32(tail, 0, crc);
            output.Write(tail, 0, 4);
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static class Crc32
        {
            private static readonly uint[] Table = BuildTable();

            private static uint[] BuildTable()
            {
                var table = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++)
                        c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    table[n] = c;
                }
                return table;
            }

            public static uint Update(uint crc, byte[] data, int offset, int count)
            {
                for (int i = offset; i < offset + count; i++)
                    crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
                return crc;
            }
        }
    }
}

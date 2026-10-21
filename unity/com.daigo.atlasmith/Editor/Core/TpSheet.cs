#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Atlasmith.Core
{
    /// <summary>
    /// O .tpsheet, a planilha lida pelo TexturePackerImporter do Unity. O Atlasmith
    /// grava esse formato só por compatibilidade (projetos que já usam o importer) e o
    /// lê para converter atlas antigos para o .atlasmith.json.
    ///
    /// Linha de sprite: nome;x;y;largura;altura; pivôX;pivôY; esquerda;direita;cima;baixo
    /// com y e pivô contados a partir de baixo, como no Unity.
    /// </summary>
    public static class TpSheet
    {
        public const string Extension = ".tpsheet";

        private const string WriterMark = "written by " + AtlasmithInfo.Name;

        // Cabeçalho próprio: o Atlasmith não é o TexturePacker e não finge ser.
        private const string Header =
            "#\n" +
            "# Sprite sheet data for Unity, " + WriterMark + ".\n" +
            "# Same layout as the sheets read by TexturePackerImporter.\n" +
            "#\n" +
            ":format=40300\n";

        public static string PathFor(string folder, string atlasName) => Path.Combine(folder, atlasName + Extension);

        public static bool IsSheetFile(string path) => path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

        public static string Build(AtlasSheet sheet)
        {
            var c = CultureInfo.InvariantCulture;
            bool borders = sheet.Sprites.Any(s => s.HasBorder);

            var sb = new StringBuilder(4096);
            sb.Append(Header);
            sb.Append(":texture=").Append(sheet.Texture).Append('\n');
            sb.Append(":size=").Append(sheet.Width.ToString(c)).Append('x').Append(sheet.Height.ToString(c)).Append('\n');
            sb.Append(":pivotpoints=enabled\n");
            sb.Append(":borders=").Append(borders ? "enabled" : "disabled").Append('\n');
            sb.Append(":alphahandling=ClearTransparentPixels\n");

            foreach (var s in sheet.Sprites.OrderBy(s => s.Name, StringComparer.Ordinal))
            {
                var r = s.Rect;
                int y = sheet.Height - r.Y - r.H;

                sb.Append(Escape(s.Name)).Append(';')
                  .Append(r.X.ToString(c)).Append(';')
                  .Append(y.ToString(c)).Append(';')
                  .Append(r.W.ToString(c)).Append(';')
                  .Append(r.H.ToString(c)).Append("; ")
                  .Append(s.PivotX.ToString("R", c)).Append(';')
                  .Append((1 - s.PivotY).ToString("R", c)).Append("; ")
                  .Append(s.BorderLeft.ToString(c)).Append(';')
                  .Append(s.BorderRight.ToString(c)).Append(';')
                  .Append(s.BorderTop.ToString(c)).Append(';')
                  .Append(s.BorderBottom.ToString(c)).Append('\n');
            }

            return sb.ToString();
        }

        public static void Save(string path, AtlasSheet sheet) => AtomicFile.WriteAllText(path, Build(sheet));

        /// <summary>
        /// O .tpsheet existe e não foi gravado pelo Atlasmith: veio do TexturePacker ou das
        /// betas, que copiavam o cabeçalho dele. No Unity, é o TexturePacker Importer quem
        /// lê esse arquivo, e ele recusa planilhas gravadas por outros programas.
        /// </summary>
        public static bool IsFromAnotherTool(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                using (var reader = new StreamReader(path, Encoding.UTF8))
                {
                    // O cabeçalho fica nas primeiras linhas de comentário.
                    for (int i = 0; i < 8; i++)
                    {
                        string? line = reader.ReadLine();
                        if (line == null) break;
                        if (line.Contains(WriterMark)) return false;
                    }
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>Null se o arquivo não existir ou não parecer um .tpsheet.</summary>
        public static AtlasSheet? TryRead(string path)
        {
            try
            {
                return File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.UTF8)) : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Converte para o modelo do Atlasmith (origem no topo, pivô com y para baixo).</summary>
        public static AtlasSheet? Parse(string content)
        {
            string texture = "";
            int width = 0, height = 0;
            var raw = new List<(string Name, RectI Rect, double Px, double Py, int L, int R, int T, int B)>();

            foreach (string rawLine in content.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                if (line[0] == ':')
                {
                    if (line.StartsWith(":texture=", StringComparison.Ordinal))
                    {
                        texture = line.Substring(":texture=".Length).Trim();
                    }
                    else if (line.StartsWith(":size=", StringComparison.Ordinal))
                    {
                        string[] size = line.Substring(":size=".Length).Split('x');
                        if (size.Length != 2 || !TryInt(size[0], out width) || !TryInt(size[1], out height)) return null;
                    }
                    continue;
                }

                string[] f = line.Split(';');
                if (f.Length < 5 ||
                    !TryInt(f[1], out int x) || !TryInt(f[2], out int y) ||
                    !TryInt(f[3], out int w) || !TryInt(f[4], out int h))
                {
                    continue;
                }

                double px = f.Length >= 7 && TryDouble(f[5], out double p1) ? p1 : 0.5;
                double py = f.Length >= 7 && TryDouble(f[6], out double p2) ? p2 : 0.5;

                int left = 0, right = 0, top = 0, bottom = 0;
                if (f.Length >= 11)
                {
                    TryInt(f[7], out left);
                    TryInt(f[8], out right);
                    TryInt(f[9], out top);
                    TryInt(f[10], out bottom);
                }

                raw.Add((Unescape(f[0].Trim()), new RectI(x, y, w, h), px, py, left, right, top, bottom));
            }

            if (width <= 0 || height <= 0) return null;

            var sheet = new AtlasSheet { Generator = "", Texture = texture, Width = width, Height = height };
            foreach (var s in raw)
            {
                // No .tpsheet o y conta a partir da borda de baixo.
                var rect = new RectI(s.Rect.X, height - s.Rect.Y - s.Rect.H, s.Rect.W, s.Rect.H);
                sheet.Sprites.Add(new AtlasSheetSprite(s.Name, rect)
                {
                    PivotX = s.Px,
                    PivotY = 1 - s.Py,
                    BorderLeft = s.L,
                    BorderRight = s.R,
                    BorderTop = s.T,
                    BorderBottom = s.B
                });
            }
            return sheet;
        }

        // O TexturePacker escapa no nome os caracteres que quebrariam a linha.
        private static string Escape(string name) =>
            name.Replace("%", "%25").Replace("#", "%23").Replace(":", "%3A").Replace(";", "%3B");

        private static string Unescape(string name) =>
            name.Replace("%23", "#").Replace("%3A", ":").Replace("%3B", ";").Replace("%25", "%");

        private static bool TryInt(string text, out int value) =>
            int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        private static bool TryDouble(string text, out double value) =>
            double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Atlasmith.Core
{
    public static class AtlasmithInfo
    {
        public const string Name = "Atlasmith";
        public const string Version = "1.0.0";
        public const string Generator = Name + " " + Version;
    }

    /// <summary>Um sprite da planilha. Coordenadas com a origem no canto superior esquerdo do PNG, y para baixo.</summary>
    public sealed class AtlasSheetSprite
    {
        public AtlasSheetSprite(string name, RectI rect)
        {
            Name = name;
            Rect = rect;
        }

        public string Name { get; set; }
        public RectI Rect { get; set; }

        /// <summary>Pivô normalizado dentro do sprite: (0, 0) é o canto superior esquerdo, (0.5, 0.5) o centro.</summary>
        public double PivotX { get; set; } = 0.5;
        public double PivotY { get; set; } = 0.5;

        /// <summary>Bordas de 9-slice, em pixels.</summary>
        public int BorderLeft { get; set; }
        public int BorderTop { get; set; }
        public int BorderRight { get; set; }
        public int BorderBottom { get; set; }

        public bool HasCustomPivot => PivotX != 0.5 || PivotY != 0.5;
        public bool HasBorder => BorderLeft != 0 || BorderTop != 0 || BorderRight != 0 || BorderBottom != 0;

        /// <summary>Leva pivô e bordas de outro sprite (o de mesmo nome na planilha anterior).</summary>
        public void CopyMetadataFrom(AtlasSheetSprite other)
        {
            PivotX = other.PivotX;
            PivotY = other.PivotY;
            BorderLeft = other.BorderLeft;
            BorderTop = other.BorderTop;
            BorderRight = other.BorderRight;
            BorderBottom = other.BorderBottom;
        }
    }

    public enum AtlasSourceKind
    {
        Krita,
        Folder
    }

    /// <summary>De onde o atlas saiu, para poder gerar de novo com a mesma receita.</summary>
    public sealed class AtlasSource
    {
        public AtlasSourceKind Kind { get; set; } = AtlasSourceKind.Krita;

        /// <summary>Relativo à pasta da planilha, com "/" (ou absoluto, se estiver em outro disco).</summary>
        public string Path { get; set; } = "";

        public bool IgnoreHiddenLayers { get; set; } = true;

        public static AtlasSource For(AtlasSourceKind kind, string absolutePath, string sheetFolder, bool ignoreHiddenLayers = true) =>
            new AtlasSource
            {
                Kind = kind,
                Path = AtlasPaths.Relative(sheetFolder, absolutePath),
                IgnoreHiddenLayers = ignoreHiddenLayers
            };

        public string Resolve(string sheetFolder) => AtlasPaths.Resolve(sheetFolder, Path);
    }

    /// <summary>
    /// O .atlasmith.json: a planilha que acompanha o PNG. Formato aberto e simples,
    /// pensado para qualquer engine ler — veja docs/atlas-format.md.
    /// </summary>
    public sealed class AtlasSheet
    {
        public const string FormatName = "atlasmith";
        public const int FormatVersion = 1;
        public const string Extension = ".atlasmith.json";

        public string Generator { get; set; } = AtlasmithInfo.Generator;

        /// <summary>Nome do PNG, na mesma pasta da planilha.</summary>
        public string Texture { get; set; } = "";

        public int Width { get; set; }
        public int Height { get; set; }

        public AtlasSource? Source { get; set; }

        /// <summary>As opções com que o atlas foi gerado. Null numa planilha convertida de outro formato.</summary>
        public PackSettings? Settings { get; set; }

        public List<AtlasSheetSprite> Sprites { get; } = new List<AtlasSheetSprite>();

        /// <summary>"pasta/menu-atlas.atlasmith.json" para "pasta/menu-atlas.png" ou para o nome "menu-atlas".</summary>
        public static string PathFor(string folder, string atlasName) => System.IO.Path.Combine(folder, atlasName + Extension);

        public static bool IsSheetFile(string path) => path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

        /// <summary>"menu-atlas.atlasmith.json" → "menu-atlas".</summary>
        public static string AtlasNameOf(string sheetPath)
        {
            string file = System.IO.Path.GetFileName(sheetPath);
            return IsSheetFile(file) ? file.Substring(0, file.Length - Extension.Length) : System.IO.Path.GetFileNameWithoutExtension(file);
        }

        public AtlasSheetSprite? Find(string name)
        {
            foreach (var sprite in Sprites)
                if (string.Equals(sprite.Name, name, StringComparison.Ordinal)) return sprite;
            return null;
        }

        // ------------------------------------------------------------------
        // Leitura
        // ------------------------------------------------------------------

        public static AtlasSheet Read(string path) => Parse(File.ReadAllText(path, Encoding.UTF8));

        /// <summary>Null se o arquivo não existir ou não for uma planilha válida.</summary>
        public static AtlasSheet? TryRead(string path)
        {
            try
            {
                return File.Exists(path) ? Read(path) : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                return null;
            }
        }

        public static AtlasSheet Parse(string json)
        {
            var text = CoreText.Current;
            if (!(MiniJson.Parse(json) is JsonObject root) || root.GetString("format") != FormatName)
                throw new InvalidDataException(text.NotAnAtlasFile);

            int version = root.GetInt("version") ?? 0;
            if (version > FormatVersion) throw new InvalidDataException(text.NewerAtlasFormat(version));
            if (version < 1) throw new InvalidDataException(text.NotAnAtlasFile);

            var size = root.GetObject("size");
            var sheet = new AtlasSheet
            {
                Generator = root.GetString("generator") ?? "",
                Texture = root.GetString("texture") ?? "",
                Width = size?.GetInt("w") ?? 0,
                Height = size?.GetInt("h") ?? 0
            };

            if (sheet.Texture.Length == 0 || sheet.Width <= 0 || sheet.Height <= 0)
                throw new InvalidDataException(text.NotAnAtlasFile);

            if (root.GetObject("source") is { } source)
            {
                sheet.Source = new AtlasSource
                {
                    Kind = source.GetString("type") == "folder" ? AtlasSourceKind.Folder : AtlasSourceKind.Krita,
                    Path = source.GetString("path") ?? "",
                    IgnoreHiddenLayers = source.GetBool("ignoreHiddenLayers") ?? true
                };
            }

            if (root.GetObject("settings") is { } settings)
            {
                var defaults = new PackSettings();
                sheet.Settings = new PackSettings
                {
                    Padding = settings.GetInt("padding") ?? defaults.Padding,
                    BorderPadding = settings.GetInt("borderPadding") ?? defaults.BorderPadding,
                    Extrude = settings.GetInt("extrude") ?? defaults.Extrude,
                    MaxSize = settings.GetInt("maxSize") ?? defaults.MaxSize,
                    PowerOfTwo = settings.GetBool("powerOfTwo") ?? defaults.PowerOfTwo,
                    ForceSquare = settings.GetBool("forceSquare") ?? defaults.ForceSquare
                }.Clamped();
            }

            foreach (object? item in root.GetArray("sprites") ?? new List<object?>())
            {
                if (!(item is JsonObject s)) throw new InvalidDataException(text.NotAnAtlasFile);

                string name = s.GetString("name") ?? "";
                int? x = s.GetInt("x"), y = s.GetInt("y"), w = s.GetInt("w"), h = s.GetInt("h");
                if (name.Length == 0 || x is null || y is null || w is null || h is null || w <= 0 || h <= 0)
                    throw new InvalidDataException(text.NotAnAtlasFile);

                var sprite = new AtlasSheetSprite(name, new RectI(x.Value, y.Value, w.Value, h.Value));

                if (s.GetObject("pivot") is { } pivot)
                {
                    sprite.PivotX = pivot.GetNumber("x") ?? 0.5;
                    sprite.PivotY = pivot.GetNumber("y") ?? 0.5;
                }

                if (s.GetObject("border") is { } border)
                {
                    sprite.BorderLeft = border.GetInt("left") ?? 0;
                    sprite.BorderTop = border.GetInt("top") ?? 0;
                    sprite.BorderRight = border.GetInt("right") ?? 0;
                    sprite.BorderBottom = border.GetInt("bottom") ?? 0;
                }

                sheet.Sprites.Add(sprite);
            }

            return sheet;
        }

        // ------------------------------------------------------------------
        // Gravação
        // ------------------------------------------------------------------

        public string ToJson()
        {
            var root = new JsonObject
            {
                { "format", FormatName },
                { "version", FormatVersion },
                { "generator", Generator },
                { "texture", Texture },
                { "size", new JsonObject { { "w", Width }, { "h", Height } } }
            };

            if (Source != null)
            {
                root.Add("source", new JsonObject
                {
                    { "type", Source.Kind == AtlasSourceKind.Folder ? "folder" : "krita" },
                    { "path", Source.Path },
                    { "ignoreHiddenLayers", Source.IgnoreHiddenLayers }
                });
            }

            if (Settings != null)
            {
                root.Add("settings", new JsonObject
                {
                    { "padding", Settings.Padding },
                    { "borderPadding", Settings.BorderPadding },
                    { "extrude", Settings.Extrude },
                    { "maxSize", Settings.MaxSize },
                    { "powerOfTwo", Settings.PowerOfTwo },
                    { "forceSquare", Settings.ForceSquare }
                });
            }

            // Ordem alfabética deixa o arquivo estável entre gerações e fácil de comparar.
            var sprites = new List<object?>();
            foreach (var sprite in Sprites.OrderBy(s => s.Name, StringComparer.Ordinal))
            {
                var item = new JsonObject
                {
                    { "name", sprite.Name },
                    { "x", sprite.Rect.X },
                    { "y", sprite.Rect.Y },
                    { "w", sprite.Rect.W },
                    { "h", sprite.Rect.H }
                };

                // Pivô no centro e borda zero são o padrão e ficam de fora.
                if (sprite.HasCustomPivot)
                    item.Add("pivot", new JsonObject { { "x", sprite.PivotX }, { "y", sprite.PivotY } });

                if (sprite.HasBorder)
                {
                    item.Add("border", new JsonObject
                    {
                        { "left", sprite.BorderLeft },
                        { "top", sprite.BorderTop },
                        { "right", sprite.BorderRight },
                        { "bottom", sprite.BorderBottom }
                    });
                }

                sprites.Add(item);
            }
            root.Add("sprites", sprites);

            return MiniJson.Write(root);
        }

        public void Save(string path) => AtomicFile.WriteAllText(path, ToJson());
    }

    public static class AtlasPaths
    {
        /// <summary>Caminho relativo com "/", ou o absoluto quando não há caminho relativo (outro disco).</summary>
        public static string Relative(string fromFolder, string path)
        {
            string relative;
            try
            {
                relative = Path.GetRelativePath(Path.GetFullPath(fromFolder), Path.GetFullPath(path));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                relative = path;
            }
            return relative.Replace('\\', '/');
        }

        public static string Resolve(string fromFolder, string stored)
        {
            if (stored.Length == 0) return "";
            string native = stored.Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.IsPathRooted(native) ? native : Path.Combine(fromFolder, native));
        }
    }

    /// <summary>Gravação sem risco de deixar o arquivo pela metade.</summary>
    public static class AtomicFile
    {
        public static void WriteAllText(string path, string content) =>
            WriteAllBytes(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content));

        /// <summary>
        /// Escreve num temporário ao lado e troca de uma vez: se o processo cair no
        /// meio, sobra o arquivo antigo inteiro em vez de um truncado.
        /// </summary>
        public static void WriteAllBytes(string path, byte[] content)
        {
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            // Um temporário por escrita: duas janelas abertas não disputam o mesmo arquivo.
            // Nome curto e fixo, que não soma ao do destino (o limite de 260 caracteres do
            // Windows pega fácil em projetos fundos); começar com "." e terminar em ".tmp"
            // faz o Unity ignorá-lo se sobrar de uma queda.
            string temp = Path.Combine(folder ?? "", "." + Guid.NewGuid().ToString("N").Substring(0, 12) + ".tmp");
            try
            {
                File.WriteAllBytes(temp, content);
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(temp, path, null);
                    }
                    catch (IOException)
                    {
                        // Alguns sistemas de arquivos (rede, FAT) não fazem a troca atômica.
                        File.Copy(temp, path, overwrite: true);
                        File.Delete(temp);
                    }
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }

        public static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}

#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Atlasmith.Core
{
    /// <summary>Uma camada ou máscara como descrita no maindoc.xml.</summary>
    public sealed class KraNode
    {
        public string Name { get; set; } = "";

        /// <summary>"paintlayer", "grouplayer", "shapelayer", "transparencymask"…</summary>
        public string NodeType { get; set; } = "";

        public string FileName { get; set; } = "";
        public string ColorSpace { get; set; } = "";
        public string CompositeOp { get; set; } = "normal";

        /// <summary>Canais ativos na ordem da memória (B, G, R, A). "1110" é o "herdar alfa".</summary>
        public string ChannelFlags { get; set; } = "";

        public int Opacity { get; set; } = 255;

        /// <summary>Deslocamento da camada: posição no documento = posição nos tiles + (X, Y).</summary>
        public int X { get; set; }
        public int Y { get; set; }

        /// <summary>O olho da camada no Krita.</summary>
        public bool Visible { get; set; } = true;
        public bool HasLayerStyle { get; set; }
        public bool IsAnimated { get; set; }

        /// <summary>Subcamadas de um grupo, de cima para baixo (a ordem do XML).</summary>
        public List<KraNode> Children { get; } = new List<KraNode>();

        public List<KraNode> Masks { get; } = new List<KraNode>();

        public bool IsPaintLayer => NodeType == "paintlayer";
        public bool IsGroup => NodeType == "grouplayer";
    }

    public sealed class KraDocument
    {
        public string ImageName { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public string ColorSpace { get; set; } = "RGBA";
        public string Profile { get; set; } = "";

        /// <summary>Camadas da raiz, de cima para baixo.</summary>
        public List<KraNode> Layers { get; set; } = new List<KraNode>();
    }

    /// <summary>Uma camada que vira sprite. LayerPath inclui os grupos: "Botões/menu-play".</summary>
    public sealed class KraSprite
    {
        public KraSprite(KraNode node, string spriteName, string layerPath)
        {
            Node = node;
            SpriteName = spriteName;
            LayerPath = layerPath;
        }

        public KraNode Node { get; }
        public string SpriteName { get; }
        public string LayerPath { get; }
    }

    public enum KraSkipKind
    {
        /// <summary>O caso normal: nome fora do padrão ("Plano de fundo").</summary>
        OutOfPattern,

        /// <summary>Camada desativada (olho fechado), ela mesma ou um grupo acima dela.</summary>
        Hidden,

        /// <summary>Camada com nome de sprite que não pôde entrar — quase sempre engano.</summary>
        Problem
    }

    /// <summary>Camada que não vira sprite, e por quê.</summary>
    public sealed class KraSkipped
    {
        public KraSkipped(string layerPath, string reason, KraSkipKind kind)
        {
            LayerPath = layerPath;
            Reason = reason;
            Kind = kind;
        }

        public string LayerPath { get; }
        public string Reason { get; }
        public KraSkipKind Kind { get; }
    }

    /// <summary>O que entra no atlas e o que fica de fora — decidido só pelo maindoc.xml, sem decodificar pixels.</summary>
    public sealed class KraPlan
    {
        public KraPlan(KraDocument document, List<KraSprite> sprites, List<KraSkipped> skipped)
        {
            Document = document;
            Sprites = sprites;
            Skipped = skipped;
        }

        public KraDocument Document { get; }
        public List<KraSprite> Sprites { get; }
        public List<KraSkipped> Skipped { get; }

        /// <summary>Nomes de sprite das camadas que ficaram de fora só por estarem desativadas.</summary>
        public HashSet<string> HiddenSpriteNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var skip in Skipped)
            {
                if (skip.Kind != KraSkipKind.Hidden) continue;
                int slash = skip.LayerPath.LastIndexOf('/');
                names.Add(KraReader.ToSpriteName(slash < 0 ? skip.LayerPath : skip.LayerPath.Substring(slash + 1)));
            }
            return names;
        }
    }

    /// <summary>Pixels BGRA32 sem premultiplicação, posicionados em coordenadas do documento.</summary>
    public sealed class KraImage
    {
        public RectI Rect { get; set; }
        public byte[] Pixels { get; set; } = Array.Empty<byte>();
    }

    public sealed class KraReadOptions
    {
        /// <summary>Camadas com o olho fechado (ou dentro de um grupo fechado) ficam de fora.</summary>
        public bool IgnoreHiddenLayers { get; set; } = true;

        public static KraReadOptions Default => new KraReadOptions();
    }

    /// <summary>
    /// Lê camadas direto do arquivo do Krita (.kra ou .krz), sem passar pelo Batch Exporter.
    ///
    /// Os dois são zip: maindoc.xml descreve a árvore de camadas e cada camada de
    /// pintura guarda seus pixels em tiles de 64x64 comprimidos com LZF. O .krz é a
    /// versão de arquivamento: mesmo conteúdo, compressão maior e sem o mergedimage.png.
    /// O recorte de cada sprite segue o node.bounds() do Krita — o mesmo que o Batch
    /// Exporter usa — então os tamanhos batem com os PNGs que ele exportaria.
    ///
    /// Regra de seleção: entra toda camada cujo nome está no padrão nome-nome
    /// ("menu-tab-active"). "Plano de fundo", "Camada 1" e afins ficam de fora.
    /// Um grupo com nome no padrão vira um sprite só (a composição dos filhos);
    /// um grupo com outro nome é só organização e o leitor procura dentro dele.
    /// </summary>
    public static class KraReader
    {
        /// <summary>Perfil padrão do Krita, o mesmo que o Batch Exporter assume.</summary>
        private const string DefaultProfile = "sRGB-elle-V2-srgbtrc.icc";

        public static readonly string[] Extensions = { ".kra", ".krz" };

        // Duas ou mais partes sem espaço, separadas por hífen: "menu-tab", "speech-ballon-A".
        private static readonly Regex SpriteNamePattern =
            new Regex(@"^[^\s-]+(?:-[^\s-]+)+$", RegexOptions.CultureInvariant);

        // Mesma higienização do Batch Exporter, para os nomes baterem com os PNGs que ele gerava.
        private static readonly Regex UnsafeNameChars =
            new Regex(@"[^a-zA-Z0-9_.-]", RegexOptions.CultureInvariant);

        /// <summary>.kra ou .krz. O "arquivo.kra~" de backup do Krita não conta.</summary>
        public static bool IsKritaFile(string path)
        {
            string ext = Path.GetExtension(path);
            return ext.Equals(".kra", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".krz", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Autosave do Krita: ".nome-autosave.kra", sempre começando com ponto.</summary>
        public static bool IsAutosave(string path) => Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal);

        public static bool IsSpriteLayerName(string name) => SpriteNamePattern.IsMatch(name.Trim());

        public static string ToSpriteName(string layerName) => UnsafeNameChars.Replace(layerName.Trim(), "_");

        /// <summary>Lê só o maindoc.xml. Rápido o bastante para atualizar a interface a cada mudança.</summary>
        public static KraPlan Inspect(string kritaPath, KraReadOptions? options = null)
        {
            using var zip = KraPackage.OpenArchive(kritaPath);
            return BuildPlan(KraPackage.ReadDocument(zip), options);
        }

        public static KraPlan BuildPlan(KraDocument document, KraReadOptions? options = null)
        {
            var opts = options ?? KraReadOptions.Default;
            var text = CoreText.Current;
            var sprites = new List<KraSprite>();
            var skipped = new List<KraSkipped>();

            // Nomes repetidos se resolvem depois da varredura, para que a camada visível
            // vença a oculta de mesmo nome esteja ela acima ou abaixo na pilha.
            var candidates = new List<(KraSprite Sprite, bool Hidden)>();

            // hiddenGroup: o grupo desativado mais externo acima do nó, se houver.
            void Walk(List<KraNode> nodes, string prefix, string? hiddenGroup)
            {
                foreach (var node in nodes)
                {
                    string path = prefix + node.Name.Trim();

                    if (!IsSpriteLayerName(node.Name))
                    {
                        // Grupo fora do padrão é pasta de organização: os filhos ainda podem entrar.
                        if (node.IsGroup) Walk(node.Children, path + "/", hiddenGroup ?? (node.Visible ? null : path));
                        else skipped.Add(new KraSkipped(path, text.SkipOutOfPattern, KraSkipKind.OutOfPattern));
                        continue;
                    }

                    if (opts.IgnoreHiddenLayers && (hiddenGroup is not null || !node.Visible))
                    {
                        string reason = hiddenGroup is null ? text.SkipHidden : text.SkipInsideHiddenGroup(hiddenGroup);
                        skipped.Add(new KraSkipped(path, reason, KraSkipKind.Hidden));
                        continue;
                    }

                    if (!node.IsPaintLayer && !node.IsGroup)
                    {
                        skipped.Add(new KraSkipped(path, text.SkipUnsupportedType(DescribeType(node.NodeType)), KraSkipKind.Problem));
                        continue;
                    }

                    candidates.Add((new KraSprite(node, ToSpriteName(node.Name), path), hiddenGroup is not null || !node.Visible));
                }
            }

            Walk(document.Layers, "", null);

            var winners = new Dictionary<string, (KraSprite Sprite, bool Hidden)>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in candidates)
            {
                string name = candidate.Sprite.SpriteName;
                if (!winners.TryGetValue(name, out var best) || (best.Hidden && !candidate.Hidden))
                    winners[name] = candidate;
            }

            foreach (var candidate in candidates)
            {
                var winner = winners[candidate.Sprite.SpriteName].Sprite;
                if (ReferenceEquals(winner, candidate.Sprite))
                    sprites.Add(candidate.Sprite);
                else
                    skipped.Add(new KraSkipped(candidate.Sprite.LayerPath, text.SkipDuplicateName(winner.LayerPath), KraSkipKind.Problem));
            }

            return new KraPlan(document, sprites, skipped);
        }

        public static LoadReport LoadSprites(
            string kritaPath,
            KraReadOptions? options = null,
            Action<int, int>? progress = null,
            CancellationToken ct = default)
        {
            var text = CoreText.Current;
            var package = KraPackage.Open(kritaPath);
            var plan = BuildPlan(package.Document, options);

            var notes = new List<string>();
            foreach (var skip in plan.Skipped)
                notes.Add(text.NoteSkipped(skip.LayerPath, skip.Reason));

            var doc = package.Document;
            if (!string.IsNullOrEmpty(doc.Profile) &&
                !doc.Profile.Equals(DefaultProfile, StringComparison.OrdinalIgnoreCase))
            {
                notes.Add(text.NoteColorProfile(doc.Profile));
            }

            var loaded = new SpriteSource?[plan.Sprites.Count];
            var failures = new ConcurrentBag<string>();
            var warnings = new ConcurrentQueue<string>();
            var empty = new ConcurrentBag<string>();
            int done = 0;

            var parallel = new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount)
            };

            Parallel.For(0, plan.Sprites.Count, parallel, i =>
            {
                var item = plan.Sprites[i];
                try
                {
                    var image = package.Render(item.Node, item.LayerPath, warnings.Enqueue);
                    if (image is null)
                    {
                        empty.Add(item.LayerPath);
                        return;
                    }

                    loaded[i] = new SpriteSource(item.SpriteName, image.Rect.W, image.Rect.H, image.Pixels, kritaPath);
                }
                catch (Exception ex)
                {
                    failures.Add($"{item.LayerPath}: {ex.Message}");
                }
                finally
                {
                    progress?.Invoke(Interlocked.Increment(ref done), plan.Sprites.Count);
                }
            });

            foreach (string path in empty.OrderBy(p => p, StringComparer.Ordinal))
                notes.Add(text.NoteEmptyLayer(path));
            notes.AddRange(warnings.Distinct().Select(text.NoteWarning));

            // Mesma ordem do modo pasta (caminho do PNG em ordem alfabética), para que
            // trocar de modo não embaralhe o atlas quando a arte é a mesma.
            var sprites = loaded
                .Where(s => s is not null)
                .Select(s => s!)
                .OrderBy(s => s.Name + ".png", StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new LoadReport(sprites, failures.OrderBy(f => f, StringComparer.Ordinal).ToArray(), notes.ToArray());
        }

        public static string DescribeType(string nodeType) => CoreText.Current.LayerType(nodeType);
    }

    /// <summary>
    /// Um .kra/.krz aberto: a árvore de camadas e os bytes de cada camada já em memória.
    /// Depois de aberto não toca mais no disco, então Render pode rodar em paralelo.
    /// </summary>
    public sealed class KraPackage
    {
        private readonly Dictionary<string, byte[]> _layerFiles;

        public string FilePath { get; }
        public KraDocument Document { get; }

        private KraPackage(string filePath, KraDocument document, Dictionary<string, byte[]> layerFiles)
        {
            FilePath = filePath;
            Document = document;
            _layerFiles = layerFiles;
        }

        public static KraPackage Open(string path)
        {
            using var zip = OpenArchive(path);
            var document = ReadDocument(zip);

            // As camadas ficam em "<nome da imagem>/layers/". O nome da imagem vem do
            // maindoc.xml e pode diferir do nome do arquivo; se não bater, aceita
            // qualquer pasta layers/.
            string prefix = document.ImageName + "/layers/";
            var entries = zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            if (entries.Count == 0)
                entries = zip.Entries.Where(e => e.FullName.IndexOf("/layers/", StringComparison.Ordinal) >= 0).ToList();

            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry.FullName.EndsWith(".icc", StringComparison.OrdinalIgnoreCase)) continue;

                string key = entry.FullName.Substring(entry.FullName.IndexOf("/layers/", StringComparison.Ordinal) + 8);
                using var input = entry.Open();
                using var buffer = new MemoryStream(checked((int)Math.Max(0, entry.Length)));
                input.CopyTo(buffer);
                files[key] = buffer.ToArray();
            }

            return new KraPackage(path, document, files);
        }

        internal static ZipArchive OpenArchive(string path)
        {
            // FileShare amplo: o Krita pode estar com o arquivo aberto ou salvando.
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            try
            {
                return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            }
            catch (InvalidDataException)
            {
                stream.Dispose();
                throw new InvalidDataException(CoreText.Current.NotKritaArchive);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        internal static KraDocument ReadDocument(ZipArchive zip)
        {
            var text = CoreText.Current;
            var entry = zip.GetEntry("maindoc.xml") ?? throw new InvalidDataException(text.MissingMainDoc);

            string xml;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                xml = reader.ReadToEnd();

            var root = MiniXml.Parse(xml);
            var image = root.Child("DOC")?.Child("IMAGE") ?? throw new InvalidDataException(text.MissingImageElement);

            string colorSpace = NonEmpty(image.Attr("colorspacename")) ?? "RGBA";
            var layers = image.Child("layers");

            return new KraDocument
            {
                ImageName = image.Attr("name") ?? "",
                Width = ParseInt(image.Attr("width"), 0),
                Height = ParseInt(image.Attr("height"), 0),
                ColorSpace = colorSpace,
                Profile = image.Attr("profile") ?? "",
                Layers = layers is null ? new List<KraNode>() : ParseLayers(layers, colorSpace)
            };
        }

        private static List<KraNode> ParseLayers(MiniXmlElement layersElement, string parentColorSpace)
        {
            var nodes = new List<KraNode>();
            foreach (var element in layersElement.ChildrenNamed("layer"))
            {
                var node = ParseNode(element, parentColorSpace);

                if (element.Child("layers") is { } children)
                    node.Children.AddRange(ParseLayers(children, node.ColorSpace));

                if (element.Child("masks") is { } masks)
                    foreach (var mask in masks.ChildrenNamed("mask"))
                        node.Masks.Add(ParseNode(mask, "ALPHA"));

                nodes.Add(node);
            }
            return nodes;
        }

        private static KraNode ParseNode(MiniXmlElement e, string fallbackColorSpace) => new KraNode
        {
            Name = e.Attr("name") ?? "",
            NodeType = e.Attr("nodetype") ?? "",
            FileName = e.Attr("filename") ?? "",
            ColorSpace = NonEmpty(e.Attr("colorspacename")) ?? fallbackColorSpace,
            CompositeOp = NonEmpty(e.Attr("compositeop")) ?? "normal",
            ChannelFlags = e.Attr("channelflags") ?? "",
            Opacity = Math.Clamp(ParseInt(e.Attr("opacity"), 255), 0, 255),
            X = ParseInt(e.Attr("x"), 0),
            Y = ParseInt(e.Attr("y"), 0),
            Visible = e.Attr("visible") != "0",
            HasLayerStyle = !string.IsNullOrEmpty(e.Attr("layerstyle")),
            IsAnimated = !string.IsNullOrEmpty(e.Attr("keyframes"))
        };

        private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        private static int ParseInt(string? value, int fallback) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;

        // ------------------------------------------------------------------
        // Renderização
        // ------------------------------------------------------------------

        /// <summary>
        /// A imagem de uma camada como o Batch Exporter a exportaria:
        /// node.projectionPixelData() dentro de node.bounds(). Isso quer dizer que a
        /// opacidade, o modo de mesclagem e a visibilidade da própria camada NÃO entram
        /// (só valem ao compor dentro de um grupo), e que máscaras não encolhem o recorte.
        /// Devolve null para camada sem nenhum pixel visível.
        /// </summary>
        public KraImage? Render(KraNode node, string layerPath, Action<string> warn)
        {
            var text = CoreText.Current;
            if (node.IsAnimated) warn(text.WarnAnimated(layerPath));
            if (node.HasLayerStyle) warn(text.WarnLayerStyle(layerPath));

            if (node.IsPaintLayer) return RenderPaintLayer(node, layerPath, warn);
            if (node.IsGroup) return RenderGroup(node, layerPath, warn);

            throw new NotSupportedException(text.TypeNotSupported(KraReader.DescribeType(node.NodeType)));
        }

        /// <summary>Composição de todas as camadas visíveis sobre o canvas. Serve para conferir contra o mergedimage.png.</summary>
        public KraImage RenderCanvas(Action<string> warn)
        {
            var canvas = new KraImage
            {
                Rect = new RectI(0, 0, Document.Width, Document.Height),
                Pixels = new byte[(long)Document.Width * Document.Height * 4]
            };
            CompositeChildren(canvas, Document.Layers, "", warn);
            return canvas;
        }

        private KraImage? RenderPaintLayer(KraNode node, string layerPath, Action<string> warn)
        {
            var device = ReadDevice(node.FileName, node.ColorSpace, node.X, node.Y);
            var bounds = device.ContentBounds(Document.Width, Document.Height);
            if (bounds is null) return null;

            var image = device.Render(bounds.Value);
            ApplyMasks(node, image, layerPath, warn);
            return image;
        }

        private KraImage? RenderGroup(KraNode group, string layerPath, Action<string> warn)
        {
            // Primeiro descobre a área: a união das camadas que podem pôr alfa no grupo.
            var children = new List<(KraNode Node, KraImage Image)>();
            for (int i = group.Children.Count - 1; i >= 0; i--)
            {
                var child = group.Children[i];
                var image = RenderChild(child, $"{layerPath}/{child.Name}", warn);
                if (image is not null) children.Add((child, image));
            }

            RectI? area = null;
            foreach (var (node, image) in children)
                if (ChannelMask.Parse(node.ChannelFlags).A)
                    area = area is null ? image.Rect : Union(area.Value, image.Rect);

            if (area is null) return null;

            var canvas = new KraImage
            {
                Rect = area.Value,
                Pixels = new byte[(long)area.Value.W * area.Value.H * 4]
            };

            foreach (var (node, image) in children)
                Blender.Composite(canvas, image, node, $"{layerPath}/{node.Name}", warn);

            // Como nas camadas de pintura, o recorte vem antes das máscaras do grupo.
            var cropped = CropToContent(canvas);
            if (cropped is not null) ApplyMasks(group, cropped, layerPath, warn);
            return cropped;
        }

        private void CompositeChildren(KraImage canvas, List<KraNode> children, string prefix, Action<string> warn)
        {
            for (int i = children.Count - 1; i >= 0; i--)
            {
                var child = children[i];
                string path = prefix + child.Name;
                var image = RenderChild(child, path, warn);
                if (image is not null) Blender.Composite(canvas, image, child, path, warn);
            }
        }

        /// <summary>
        /// Uma subcamada vista de dentro do grupo: oculta não conta (como no próprio
        /// Krita, com ou sem a opção de ignorar desativadas), e efeitos não suportados viram aviso.
        /// </summary>
        private KraImage? RenderChild(KraNode child, string path, Action<string> warn)
        {
            if (!child.Visible) return null;

            var text = CoreText.Current;
            if (child.HasLayerStyle) warn(text.WarnLayerStyleInGroup(path));

            if (child.IsPaintLayer) return RenderPaintLayer(child, path, warn);
            if (child.IsGroup) return RenderGroup(child, path, warn);

            warn(text.WarnUnsupportedInGroup(path, KraReader.DescribeType(child.NodeType)));
            return null;
        }

        private void ApplyMasks(KraNode node, KraImage image, string layerPath, Action<string> warn)
        {
            var text = CoreText.Current;
            foreach (var mask in node.Masks)
            {
                if (!mask.Visible) continue;

                switch (mask.NodeType)
                {
                    case "transparencymask":
                        string file = mask.FileName + ".pixelselection";
                        if (!_layerFiles.ContainsKey(file))
                        {
                            warn(text.WarnEmptyMask(mask.Name, layerPath));
                            continue;
                        }
                        ApplyTransparencyMask(image, ReadDevice(file, "ALPHA", mask.X, mask.Y));
                        break;

                    case "selectionmask":
                        break; // Seleção salva não afeta a imagem.

                    default:
                        warn(text.WarnUnsupportedMask(KraReader.DescribeType(mask.NodeType), mask.Name, layerPath));
                        break;
                }
            }
        }

        /// <summary>
        /// Reproduz o KisMask::apply do Krita: cópia com a seleção como máscara sobre um
        /// fundo transparente, na aritmética de 8 bits dele.
        /// </summary>
        private static void ApplyTransparencyMask(KraImage image, Device mask)
        {
            var r = image.Rect;
            byte[] px = image.Pixels;

            for (int y = r.Y; y < r.Bottom; y++)
            {
                int localY = y - mask.OffsetY;
                int row = FloorDiv(localY, mask.TileH);
                int ty = localY - row * mask.TileH;

                int x = r.X;
                while (x < r.Right)
                {
                    int localX = x - mask.OffsetX;
                    int col = FloorDiv(localX, mask.TileW);
                    int tx = localX - col * mask.TileW;
                    int span = Math.Min(mask.TileW - tx, r.Right - x);

                    mask.Tiles.TryGetValue((col, row), out byte[]? tile);
                    int tileIndex = ty * mask.TileW + tx;
                    int p = ((y - r.Y) * r.W + (x - r.X)) * 4;

                    for (int i = 0; i < span; i++, p += 4)
                    {
                        int m = tile is null ? mask.Default[0] : tile[tileIndex + i];
                        if (m == 255) continue;

                        int alpha = px[p + 3];
                        int newAlpha = m == 0 ? 0 : Blender.Mul(alpha, m);
                        if (newAlpha == 0)
                        {
                            px[p] = px[p + 1] = px[p + 2] = px[p + 3] = 0;
                            continue;
                        }

                        for (int c = 0; c < 3; c++)
                            px[p + c] = (byte)Blender.Div(Blender.Mul(Blender.Mul(px[p + c], alpha), m), newAlpha);
                        px[p + 3] = (byte)newAlpha;
                    }

                    x += span;
                }
            }
        }

        private static KraImage? CropToContent(KraImage image)
        {
            var r = image.Rect;
            byte[] px = image.Pixels;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

            for (int y = 0; y < r.H; y++)
            {
                int row = y * r.W * 4;
                for (int x = 0; x < r.W; x++)
                {
                    if (px[row + x * 4 + 3] == 0) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    maxY = y;
                }
            }

            if (maxX < minX) return null;

            var crop = new RectI(r.X + minX, r.Y + minY, maxX - minX + 1, maxY - minY + 1);
            if (crop.W == r.W && crop.H == r.H) return image;

            var output = new byte[(long)crop.W * crop.H * 4];
            for (int y = 0; y < crop.H; y++)
                Buffer.BlockCopy(px, ((minY + y) * r.W + minX) * 4, output, y * crop.W * 4, crop.W * 4);

            return new KraImage { Rect = crop, Pixels = output };
        }

        private static RectI Union(RectI a, RectI b)
        {
            int x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
            return new RectI(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
        }

        internal static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        // ------------------------------------------------------------------
        // Tiles
        // ------------------------------------------------------------------

        private enum PixelLayout { Bgra8, Bgra16, GrayA8, GrayA16, Alpha8 }

        /// <summary>Os pixels de uma camada (ou máscara), já convertidos para BGRA8 (ou A8, nas máscaras).</summary>
        private sealed class Device
        {
            public Device(int offsetX, int offsetY, int tileW, int tileH, byte[] defaultPixel, Dictionary<(int Col, int Row), byte[]> tiles)
            {
                OffsetX = offsetX;
                OffsetY = offsetY;
                TileW = tileW;
                TileH = tileH;
                Default = defaultPixel;
                Tiles = tiles;
            }

            public int OffsetX { get; }
            public int OffsetY { get; }
            public int TileW { get; }
            public int TileH { get; }

            /// <summary>Pixel de quem não tem tile. Quase sempre transparente; no "Plano de fundo" costuma ser branco.</summary>
            public byte[] Default { get; }

            /// <summary>Chave = (coluna, linha) na grade de tiles da camada.</summary>
            public Dictionary<(int Col, int Row), byte[]> Tiles { get; }

            /// <summary>
            /// O exactBounds() do Krita: a caixa dos pixels com alfa &gt; 0. Se o pixel
            /// padrão não é transparente, a camada cobre o canvas inteiro mais o que
            /// diferir do padrão fora dele.
            /// </summary>
            public RectI? ContentBounds(int canvasW, int canvasH)
            {
                bool transparentDefault = Default[3] == 0;
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

                foreach (var pair in Tiles)
                {
                    byte[] tile = pair.Value;
                    int baseX = pair.Key.Col * TileW + OffsetX;
                    int baseY = pair.Key.Row * TileH + OffsetY;

                    for (int y = 0; y < TileH; y++)
                    {
                        int p = y * TileW * 4;
                        for (int x = 0; x < TileW; x++, p += 4)
                        {
                            bool content = transparentDefault
                                ? tile[p + 3] != 0
                                : tile[p] != Default[0] || tile[p + 1] != Default[1] ||
                                  tile[p + 2] != Default[2] || tile[p + 3] != Default[3];
                            if (!content) continue;

                            int ix = baseX + x, iy = baseY + y;
                            if (ix < minX) minX = ix;
                            if (ix > maxX) maxX = ix;
                            if (iy < minY) minY = iy;
                            if (iy > maxY) maxY = iy;
                        }
                    }
                }

                if (!transparentDefault && canvasW > 0 && canvasH > 0)
                {
                    minX = Math.Min(minX, 0);
                    minY = Math.Min(minY, 0);
                    maxX = Math.Max(maxX, canvasW - 1);
                    maxY = Math.Max(maxY, canvasH - 1);
                }

                if (maxX < minX || maxY < minY) return null;
                return new RectI(minX, minY, maxX - minX + 1, maxY - minY + 1);
            }

            public KraImage Render(RectI rect)
            {
                var px = new byte[(long)rect.W * rect.H * 4];

                if (Default.Any(b => b != 0))
                    for (int i = 0; i < px.Length; i += 4)
                        Buffer.BlockCopy(Default, 0, px, i, 4);

                foreach (var pair in Tiles)
                {
                    byte[] tile = pair.Value;
                    int tileX = pair.Key.Col * TileW + OffsetX;
                    int tileY = pair.Key.Row * TileH + OffsetY;

                    int x0 = Math.Max(rect.X, tileX), x1 = Math.Min(rect.Right, tileX + TileW);
                    int y0 = Math.Max(rect.Y, tileY), y1 = Math.Min(rect.Bottom, tileY + TileH);
                    if (x0 >= x1 || y0 >= y1) continue;

                    int bytes = (x1 - x0) * 4;
                    for (int y = y0; y < y1; y++)
                    {
                        Buffer.BlockCopy(
                            tile, ((y - tileY) * TileW + (x0 - tileX)) * 4,
                            px, ((y - rect.Y) * rect.W + (x0 - rect.X)) * 4,
                            bytes);
                    }
                }

                return new KraImage { Rect = rect, Pixels = px };
            }
        }

        private Device ReadDevice(string fileName, string colorSpace, int offsetX, int offsetY)
        {
            var text = CoreText.Current;
            if (!_layerFiles.TryGetValue(fileName, out byte[]? data))
                throw new InvalidDataException(text.LayerDataMissing(fileName));

            PixelLayout layout;
            switch (colorSpace)
            {
                case "RGBA": layout = PixelLayout.Bgra8; break;
                case "RGBA16": layout = PixelLayout.Bgra16; break;
                case "GRAYA": layout = PixelLayout.GrayA8; break;
                case "GRAYA16": layout = PixelLayout.GrayA16; break;
                case "ALPHA": layout = PixelLayout.Alpha8; break;
                default: throw new NotSupportedException(text.UnsupportedColorSpace(colorSpace));
            }

            int rawSize = layout switch
            {
                PixelLayout.Bgra8 => 4,
                PixelLayout.Bgra16 => 8,
                PixelLayout.GrayA8 => 2,
                PixelLayout.GrayA16 => 4,
                _ => 1
            };
            int outSize = layout == PixelLayout.Alpha8 ? 1 : 4;

            // Cabeçalho em texto: VERSION, TILEWIDTH, TILEHEIGHT, PIXELSIZE e DATA <n>.
            int pos = 0, version = 0, tileW = 0, tileH = 0, pixelSize = 0, count = -1;
            while (count < 0)
            {
                string line = ReadLine(data, ref pos) ?? throw Corrupt(fileName);
                int space = line.IndexOf(' ');
                if (space <= 0 || !int.TryParse(line.Substring(space + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                    throw Corrupt(fileName);

                switch (line.Substring(0, space))
                {
                    case "VERSION": version = value; break;
                    case "TILEWIDTH": tileW = value; break;
                    case "TILEHEIGHT": tileH = value; break;
                    case "PIXELSIZE": pixelSize = value; break;
                    case "DATA": count = value; break;
                    default: throw Corrupt(fileName);
                }
            }

            if (version != 2)
                throw new NotSupportedException(text.UnsupportedLayerVersion(version));
            if (pixelSize != rawSize)
                throw new InvalidDataException(text.PixelSizeMismatch(fileName, pixelSize, colorSpace, rawSize));
            if (tileW is <= 0 or > 4096 || tileH is <= 0 or > 4096)
                throw Corrupt(fileName);

            int tileBytes = tileW * tileH * pixelSize;
            var raw = new byte[tileBytes];
            var linear = new byte[tileBytes];
            var tiles = new Dictionary<(int Col, int Row), byte[]>(count);

            for (int t = 0; t < count; t++)
            {
                string header = ReadLine(data, ref pos) ?? throw Corrupt(fileName);
                string[] f = header.Split(',');
                if (f.Length != 4 || f[2] != "LZF" ||
                    !int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
                    !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) ||
                    !int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int size) ||
                    size <= 0 || pos + size > data.Length)
                {
                    throw Corrupt(fileName);
                }

                DecompressTile(new ReadOnlySpan<byte>(data, pos, size), raw, linear, pixelSize, fileName);
                pos += size;

                var converted = new byte[tileW * tileH * outSize];
                Convert(raw, converted, layout);
                tiles[(FloorDiv(x, tileW), FloorDiv(y, tileH))] = converted;
            }

            var defaultPixel = new byte[outSize];
            if (_layerFiles.TryGetValue(fileName + ".defaultpixel", out byte[]? defaultRaw) && defaultRaw.Length == rawSize)
                Convert(defaultRaw, defaultPixel, layout);

            return new Device(offsetX, offsetY, tileW, tileH, defaultPixel, tiles);
        }

        /// <summary>
        /// Primeiro byte: 1 = LZF sobre os bytes "linearizados" (todos os bytes 0 de
        /// cada pixel, depois todos os bytes 1…), 0 = pixels crus, intercalados.
        /// </summary>
        private static void DecompressTile(ReadOnlySpan<byte> payload, byte[] raw, byte[] linear, int pixelSize, string fileName)
        {
            switch (payload[0])
            {
                case 1:
                    if (Lzf.Decompress(payload.Slice(1), linear) != linear.Length) throw Corrupt(fileName);

                    int pixels = raw.Length / pixelSize;
                    for (int c = 0; c < pixelSize; c++)
                    {
                        int src = c * pixels;
                        for (int p = 0, dst = c; p < pixels; p++, dst += pixelSize)
                            raw[dst] = linear[src + p];
                    }
                    break;

                case 0:
                    if (payload.Length - 1 < raw.Length) throw Corrupt(fileName);
                    payload.Slice(1, raw.Length).CopyTo(raw);
                    break;

                default:
                    throw Corrupt(fileName);
            }
        }

        private static void Convert(ReadOnlySpan<byte> raw, Span<byte> output, PixelLayout layout)
        {
            switch (layout)
            {
                case PixelLayout.Bgra8:
                case PixelLayout.Alpha8:
                    raw.Slice(0, output.Length).CopyTo(output);
                    break;

                case PixelLayout.Bgra16:
                    for (int i = 0; i < output.Length; i++)
                        output[i] = Scale16(BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(i * 2)));
                    break;

                case PixelLayout.GrayA8:
                    for (int p = 0, o = 0; o < output.Length; p += 2, o += 4)
                    {
                        output[o] = output[o + 1] = output[o + 2] = raw[p];
                        output[o + 3] = raw[p + 1];
                    }
                    break;

                case PixelLayout.GrayA16:
                    for (int p = 0, o = 0; o < output.Length; p += 4, o += 4)
                    {
                        output[o] = output[o + 1] = output[o + 2] = Scale16(BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(p)));
                        output[o + 3] = Scale16(BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(p + 2)));
                    }
                    break;
            }
        }

        private static byte Scale16(ushort v) => (byte)((v * 255 + 32767) / 65535);

        private static string? ReadLine(byte[] data, ref int pos)
        {
            if (pos >= data.Length) return null;
            int end = Array.IndexOf(data, (byte)'\n', pos);
            if (end < 0) return null;

            string line = Encoding.ASCII.GetString(data, pos, end - pos).TrimEnd('\r');
            pos = end + 1;
            return line;
        }

        private static InvalidDataException Corrupt(string fileName) =>
            new InvalidDataException(CoreText.Current.LayerDataCorrupt(fileName));
    }

    /// <summary>Descompressor LZF (o formato da liblzf, que o Krita usa nos tiles).</summary>
    internal static class Lzf
    {
        public static int Decompress(ReadOnlySpan<byte> input, Span<byte> output)
        {
            int ip = 0, op = 0;

            while (ip < input.Length)
            {
                int ctrl = input[ip++];

                if (ctrl < 32)
                {
                    // Sequência literal de ctrl + 1 bytes.
                    int len = ctrl + 1;
                    if (ip + len > input.Length || op + len > output.Length) return -1;
                    input.Slice(ip, len).CopyTo(output.Slice(op));
                    ip += len;
                    op += len;
                    continue;
                }

                // Referência para trás: comprimento nos 3 bits altos, distância no resto.
                int length = ctrl >> 5;
                int reference = op - ((ctrl & 0x1F) << 8) - 1;

                if (length == 7)
                {
                    if (ip >= input.Length) return -1;
                    length += input[ip++];
                }

                if (ip >= input.Length) return -1;
                reference -= input[ip++];
                length += 2;

                if (reference < 0 || op + length > output.Length) return -1;

                if (op - reference >= length)
                {
                    output.Slice(reference, length).CopyTo(output.Slice(op));
                    op += length;
                }
                else
                {
                    // A origem sobrepõe o destino: copiar byte a byte repete o padrão.
                    for (int i = 0; i < length; i++) output[op++] = output[reference++];
                }
            }

            return op;
        }
    }

    /// <summary>Canais ativos de uma camada, na ordem da memória do Krita (B, G, R, A).</summary>
    internal readonly struct ChannelMask
    {
        public ChannelMask(bool b, bool g, bool r, bool a)
        {
            B = b;
            G = g;
            R = r;
            A = a;
        }

        public bool B { get; }
        public bool G { get; }
        public bool R { get; }
        public bool A { get; }

        public bool All => B && G && R && A;

        public static ChannelMask Parse(string flags)
        {
            if (flags.Length == 4)
                return new ChannelMask(flags[0] != '0', flags[1] != '0', flags[2] != '0', flags[3] != '0');

            // Escala de cinza: (cinza, alfa).
            if (flags.Length == 2)
                return new ChannelMask(flags[0] != '0', flags[0] != '0', flags[0] != '0', flags[1] != '0');

            return new ChannelMask(true, true, true, true);
        }
    }

    /// <summary>
    /// Composição de uma camada sobre o grupo, imitando a aritmética de 8 bits do
    /// Krita: o Normal segue o KoOptimizedCompositeOpOver (em float) e os demais modos,
    /// a fórmula genérica dos KoCompositeOpGeneric. Diferenças de ±1 são esperadas.
    /// </summary>
    internal static class Blender
    {
        private enum Mode { Normal, Multiply, Screen, Add, Subtract, Darken, Lighten, Overlay, Difference, Erase }

        private static Mode? ParseMode(string compositeOp)
        {
            switch (compositeOp)
            {
                case "normal": return Mode.Normal;
                case "multiply": return Mode.Multiply;
                case "screen": return Mode.Screen;
                case "add":
                case "linear_dodge": return Mode.Add;
                case "subtract": return Mode.Subtract;
                case "darken": return Mode.Darken;
                case "lighten": return Mode.Lighten;
                case "overlay": return Mode.Overlay;
                case "diff": return Mode.Difference;
                case "erase": return Mode.Erase;
                default: return null;
            }
        }

        public static void Composite(KraImage dst, KraImage src, KraNode layer, string layerPath, Action<string> warn)
        {
            Mode? parsed = ParseMode(layer.CompositeOp);
            if (parsed is null)
                warn(CoreText.Current.WarnBlendMode(layer.CompositeOp, layerPath));

            var mode = parsed ?? Mode.Normal;
            var flags = ChannelMask.Parse(layer.ChannelFlags);

            var d = dst.Rect;
            var s = src.Rect;
            int x0 = Math.Max(d.X, s.X), x1 = Math.Min(d.Right, s.Right);
            int y0 = Math.Max(d.Y, s.Y), y1 = Math.Min(d.Bottom, s.Bottom);
            if (x0 >= x1 || y0 >= y1) return;

            for (int y = y0; y < y1; y++)
            {
                int si = ((y - s.Y) * s.W + (x0 - s.X)) * 4;
                int di = ((y - d.Y) * d.W + (x0 - d.X)) * 4;

                for (int x = x0; x < x1; x++, si += 4, di += 4)
                {
                    if (mode == Mode.Normal)
                        Over(src.Pixels, si, dst.Pixels, di, layer.Opacity, flags);
                    else
                        Generic(src.Pixels, si, dst.Pixels, di, layer.Opacity, flags, mode);
                }
            }
        }

        /// <summary>KoOptimizedCompositeOpOver32::compositeOnePixelScalar.</summary>
        private static void Over(byte[] src, int si, byte[] dst, int di, int opacity, ChannelMask flags)
        {
            const float rec = 1f / 255f;

            float srcAlpha = src[si + 3] * (opacity * rec);
            if (srcAlpha == 0f) return;

            bool alphaLocked = !flags.A;
            float dstAlpha = dst[di + 3];
            float blend;

            if (alphaLocked || dstAlpha == 255f)
            {
                blend = srcAlpha * rec;
            }
            else if (dstAlpha == 0f)
            {
                dstAlpha = srcAlpha;
                blend = 1f;
                if (!flags.All) dst[di] = dst[di + 1] = dst[di + 2] = 0;
            }
            else
            {
                dstAlpha += (255f - dstAlpha) * srcAlpha * rec;
                blend = srcAlpha / dstAlpha;
            }

            if (blend == 1f)
            {
                if (flags.B) dst[di] = src[si];
                if (flags.G) dst[di + 1] = src[si + 1];
                if (flags.R) dst[di + 2] = src[si + 2];
            }
            else if (blend != 0f)
            {
                if (flags.B) dst[di] = Round(dst[di], src[si], blend);
                if (flags.G) dst[di + 1] = Round(dst[di + 1], src[si + 1], blend);
                if (flags.R) dst[di + 2] = Round(dst[di + 2], src[si + 2], blend);
            }

            if (!alphaLocked) dst[di + 3] = (byte)Math.Clamp((int)MathF.Round(dstAlpha, MidpointRounding.AwayFromZero), 0, 255);
        }

        private static byte Round(byte a, byte b, float t) =>
            (byte)Math.Clamp((int)MathF.Round((b - a) * t + a, MidpointRounding.AwayFromZero), 0, 255);

        /// <summary>KoCompositeOpGenericSC::composeColorChannels, mais o Erase.</summary>
        private static void Generic(byte[] src, int si, byte[] dst, int di, int opacity, ChannelMask flags, Mode mode)
        {
            int srcAlpha = Mul3(src[si + 3], 255, opacity);
            if (srcAlpha == 0) return;

            int dstAlpha = dst[di + 3];
            bool alphaLocked = !flags.A;

            if (mode == Mode.Erase)
            {
                if (!alphaLocked) dst[di + 3] = (byte)Mul(255 - srcAlpha, dstAlpha);
                return;
            }

            if (alphaLocked)
            {
                if (dstAlpha == 0) return;
                for (int c = 0; c < 3; c++)
                {
                    if (!Enabled(flags, c)) continue;
                    int cf = Blend(mode, src[si + c], dst[di + c]);
                    dst[di + c] = (byte)Lerp(dst[di + c], cf, srcAlpha);
                }
                return;
            }

            // Sobre um pixel vazio qualquer modo resulta na própria origem. O Krita copia
            // direto; pela fórmula, o arredondamento em 8 bits estragaria a cor das bordas
            // quase transparentes.
            if (dstAlpha == 0)
            {
                if (flags.B) dst[di] = src[si];
                if (flags.G) dst[di + 1] = src[si + 1];
                if (flags.R) dst[di + 2] = src[si + 2];
                dst[di + 3] = (byte)srcAlpha;
                return;
            }

            int newAlpha = srcAlpha + dstAlpha - Mul(srcAlpha, dstAlpha);
            if (newAlpha != 0)
            {
                for (int c = 0; c < 3; c++)
                {
                    if (!Enabled(flags, c)) continue;
                    int s = src[si + c], d = dst[di + c];
                    int result = Mul3(255 - srcAlpha, dstAlpha, d) +
                                 Mul3(255 - dstAlpha, srcAlpha, s) +
                                 Mul3(srcAlpha, dstAlpha, Blend(mode, s, d));
                    dst[di + c] = (byte)Div(result, newAlpha);
                }
            }
            dst[di + 3] = (byte)newAlpha;
        }

        private static bool Enabled(ChannelMask flags, int channel) => channel switch
        {
            0 => flags.B,
            1 => flags.G,
            _ => flags.R
        };

        private static int Blend(Mode mode, int src, int dst) => mode switch
        {
            Mode.Multiply => Mul(src, dst),
            Mode.Screen => src + dst - Mul(src, dst),
            Mode.Add => Math.Min(255, src + dst),
            Mode.Subtract => Math.Max(0, dst - src),
            Mode.Darken => Math.Min(src, dst),
            Mode.Lighten => Math.Max(src, dst),
            Mode.Difference => Math.Abs(src - dst),
            Mode.Overlay => HardLight(dst, src),
            _ => src
        };

        private static int HardLight(int src, int dst)
        {
            int src2 = src + src;
            if (src > 127)
            {
                src2 -= 255;
                return src2 + dst - Mul(src2, dst);
            }
            return Mul(src2, dst);
        }

        // Aritmética de 8 bits do Krita (KoColorSpaceMaths / kis_global).

        public static int Mul(int a, int b)
        {
            int c = a * b + 0x80;
            return ((c >> 8) + c) >> 8;
        }

        public static int Mul3(int a, int b, int c)
        {
            int t = a * b * c + 0x7F5B;
            return ((t >> 7) + t) >> 16;
        }

        public static int Div(int a, int b) => b == 0 ? 0 : Math.Min(255, (a * 255 + b / 2) / b);

        private static int Lerp(int a, int b, int t)
        {
            int c = (b - a) * t + 0x80;
            c = ((c >> 8) + c) >> 8;
            return a + c;
        }
    }
}

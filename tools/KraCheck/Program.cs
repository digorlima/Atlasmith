using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Atlasmith.Core;
using Atlasmith.Services;

namespace KraCheck;

/// <summary>
/// Confere o leitor de .kra/.krz contra referências que não dependem dele:
///
///   mergedimage.png  a composição que o próprio Krita grava dentro do .kra
///                    (o .krz não traz, e a verificação é pulada);
///   --oracle DIR     saída do tools/krita/kra_oracle.py: o Krita exportando cada
///                    camada exatamente como o Batch Exporter (node.bounds() +
///                    projectionPixelData);
///   --atlas FILE     um .tpsheet ou .atlasmith.json já gerado: cada sprite recortado
///                    do atlas tem que bater pixel a pixel com a camada lida do .kra.
///
/// E sempre: a opção das camadas desativadas (ligada e desligada) e uma geração
/// completa numa pasta temporária, relida nos dois formatos de planilha.
/// --include-hidden lê como o app com "Ignorar camadas desativadas" desligado.
///
/// Camada de pintura simples tem que sair idêntica. Composições (grupos, máscaras)
/// aceitam ±2 por canal, porque o Krita compõe em float com SIMD.
/// </summary>
internal static class Program
{
    private const int CompositeTolerance = 2;

    private static int _failures;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0 || !File.Exists(args[0]))
        {
            Console.WriteLine("Uso: KraCheck <arquivo.kra|.krz> [--oracle <pasta>] [--atlas <atlas.tpsheet|.atlasmith.json>] [--include-hidden]");
            return 2;
        }

        string kraPath = args[0];
        string? oracleDir = Option(args, "--oracle");
        string? atlasPath = Option(args, "--atlas");
        var options = new KraReadOptions { IgnoreHiddenLayers = !args.Contains("--include-hidden") };

        Console.WriteLine($"Arquivo: {Path.GetFullPath(kraPath)}");
        var package = KraPackage.Open(kraPath);
        var doc = package.Document;
        Console.WriteLine($"{doc.Width}x{doc.Height} {doc.ColorSpace} {doc.Profile}");
        Console.WriteLine();

        Console.WriteLine(options.IgnoreHiddenLayers
            ? "Camadas desativadas: ignoradas (padrão do app)"
            : "Camadas desativadas: incluídas (--include-hidden)");
        PrintPlan(KraReader.BuildPlan(doc, options));
        CheckHiddenOption(doc);
        var report = CheckLoad(kraPath, options);
        CheckCanvas(kraPath, package);
        CheckRoundtrip(report);

        Dictionary<string, OracleNode>? oracle = null;
        if (oracleDir is not null)
        {
            oracle = LoadOracle(oracleDir);
            CheckOracle(package, oracle, oracleDir);
        }

        if (atlasPath is not null)
            CheckAtlas(atlasPath, report, oracle, oracleDir);

        Console.WriteLine();
        if (_failures == 0)
        {
            Console.WriteLine("TODAS AS VERIFICAÇÕES PASSARAM.");
            return 0;
        }

        Console.WriteLine($"{_failures} VERIFICAÇÃO(ÕES) FALHARAM.");
        return 1;
    }

    private static void Fail(string message)
    {
        _failures++;
        Console.WriteLine($"  FALHA: {message}");
    }

    private static void Pass(string message) => Console.WriteLine($"  ok — {message}");

    private static string? Option(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    // ------------------------------------------------------------------

    private static void PrintPlan(KraPlan plan)
    {
        Console.WriteLine("Seleção de camadas");
        foreach (var s in plan.Sprites)
            Console.WriteLine($"  entra   {s.LayerPath}  ->  {s.SpriteName}" + (s.Node.Visible ? "" : "  (oculta)"));
        foreach (var s in plan.Skipped)
        {
            string kind = s.Kind switch
            {
                KraSkipKind.Hidden => "oculta",
                KraSkipKind.Problem => "PROBLEMA",
                _ => "fora"
            };
            Console.WriteLine($"  {kind,-8}{s.LayerPath}  ({s.Reason})");
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Desligar "Ignorar camadas desativadas" tem que manter todas as visíveis e trazer
    /// as ocultas — menos as que repetem o nome de uma visível, que perdem para ela.
    /// </summary>
    private static void CheckHiddenOption(KraDocument doc)
    {
        Console.WriteLine("Opção das camadas desativadas");

        var ignoring = KraReader.BuildPlan(doc, new KraReadOptions { IgnoreHiddenLayers = true });
        var including = KraReader.BuildPlan(doc, new KraReadOptions { IgnoreHiddenLayers = false });

        var hidden = ignoring.Skipped.Where(s => s.Kind == KraSkipKind.Hidden).Select(s => s.LayerPath).ToHashSet();
        var without = ignoring.Sprites.Select(s => s.LayerPath).ToHashSet();
        var withHidden = including.Sprites.Select(s => s.LayerPath).ToHashSet();
        var duplicates = including.Skipped.Where(s => s.Kind == KraSkipKind.Problem).Select(s => s.LayerPath).ToHashSet();

        if (including.Skipped.Any(s => s.Kind == KraSkipKind.Hidden))
            Fail("com a opção desligada, ainda há camadas puladas por estarem ocultas");

        var lostVisible = without.Except(withHidden).ToList();
        var added = withHidden.Except(without).ToHashSet();
        var notHidden = added.Except(hidden).ToList();
        var missing = hidden.Except(added).Where(p => !duplicates.Contains(p)).ToList();
        int shadowed = hidden.Count - added.Count;

        if (lostVisible.Count > 0) Fail("camadas visíveis saíram ao incluir as ocultas: " + string.Join(", ", lostVisible));
        if (notHidden.Count > 0) Fail("entraram camadas que não estavam ocultas: " + string.Join(", ", notHidden));
        if (missing.Count > 0) Fail("ocultas que não entraram nem são nome repetido: " + string.Join(", ", missing));

        if (lostVisible.Count + notHidden.Count + missing.Count == 0)
        {
            Pass(hidden.Count == 0
                ? "o arquivo não tem camada de sprite desativada"
                : $"{hidden.Count} ocultas ficam de fora por padrão; com a opção desligada entram {added.Count}" +
                  (shadowed > 0 ? $" e {shadowed} perdem para a visível de mesmo nome" : ""));
        }

        Console.WriteLine();
    }

    private static LoadReport CheckLoad(string kraPath, KraReadOptions options)
    {
        Console.WriteLine("KraReader.LoadSprites");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var report = KraReader.LoadSprites(kraPath, options);
        watch.Stop();

        foreach (string note in report.Notes) Console.WriteLine($"  {note}");
        foreach (string failure in report.Failures) Fail(failure);

        Pass($"{report.Sprites.Length} sprites em {watch.ElapsedMilliseconds} ms");
        foreach (var s in report.Sprites)
            Console.WriteLine($"    {s.Name,-44} {s.Width}x{s.Height}");

        var names = report.Sprites.Select(s => s.Name).ToArray();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length) Fail("nomes repetidos");
        if (names.Any(n => n.Contains(' ') || n.Contains(';'))) Fail("nome com espaço ou ';'");

        Console.WriteLine();
        return report;
    }

    private static void CheckCanvas(string kraPath, KraPackage package)
    {
        Console.WriteLine("Composição completa x mergedimage.png");

        byte[]? png;
        using (var zip = ZipFile.OpenRead(kraPath))
        {
            var entry = zip.GetEntry("mergedimage.png");
            if (entry is null)
            {
                Console.WriteLine("  (o arquivo não tem mergedimage.png)");
                Console.WriteLine();
                return;
            }

            using var input = entry.Open();
            using var buffer = new MemoryStream();
            input.CopyTo(buffer);
            png = buffer.ToArray();
        }

        var (w, h, merged) = DecodePng(png);
        var warnings = new List<string>();
        var canvas = package.RenderCanvas(warnings.Add);
        foreach (string warning in warnings.Distinct()) Console.WriteLine($"  aviso — {warning}");

        if (w != canvas.Rect.W || h != canvas.Rect.H)
        {
            Fail($"mergedimage.png tem {w}x{h}, o canvas {canvas.Rect.W}x{canvas.Rect.H}");
        }
        else
        {
            var diff = Compare(canvas.Pixels, merged, w * h);
            Report("canvas", diff, CompositeTolerance);
        }

        Console.WriteLine();
    }

    // ------------------------------------------------------------------
    // Oráculo do Krita
    // ------------------------------------------------------------------

    private sealed record OracleNode(string Path, string Type, int X, int Y, int W, int H, string? File);

    private static Dictionary<string, OracleNode> LoadOracle(string dir)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "oracle.json")));
        var result = new Dictionary<string, OracleNode>(StringComparer.Ordinal);
        var stack = new List<string>();

        foreach (var n in json.RootElement.GetProperty("nodes").EnumerateArray())
        {
            int depth = n.GetProperty("depth").GetInt32();
            string name = n.GetProperty("name").GetString() ?? "";
            if (stack.Count > depth) stack.RemoveRange(depth, stack.Count - depth);
            stack.Add(name);

            string path = string.Join("/", stack);
            result[path] = new OracleNode(
                path,
                n.GetProperty("type").GetString() ?? "",
                n.GetProperty("x").GetInt32(),
                n.GetProperty("y").GetInt32(),
                n.GetProperty("w").GetInt32(),
                n.GetProperty("h").GetInt32(),
                n.TryGetProperty("file", out var f) ? f.GetString() : null);
        }

        return result;
    }

    private static void CheckOracle(KraPackage package, Dictionary<string, OracleNode> oracle, string dir)
    {
        Console.WriteLine("Cada camada x Krita (node.bounds + projectionPixelData)");

        foreach (var node in oracle.Values)
        {
            if (node.File is null) continue;

            var kraNode = FindNode(package.Document.Layers, node.Path.Split('/'));
            if (kraNode is null)
            {
                Fail($"{node.Path}: o leitor não encontrou essa camada");
                continue;
            }

            var warnings = new List<string>();
            var image = package.Render(kraNode, node.Path, warnings.Add);
            if (image is null)
            {
                Fail($"{node.Path}: o leitor achou a camada vazia; o Krita diz {node.W}x{node.H}");
                continue;
            }

            var r = image.Rect;
            if (r.X != node.X || r.Y != node.Y || r.W != node.W || r.H != node.H)
            {
                Fail($"{node.Path}: recorte ({r.X},{r.Y} {r.W}x{r.H}), Krita ({node.X},{node.Y} {node.W}x{node.H})");
                continue;
            }

            var expected = ImageLoader.Load(Path.Combine(dir, node.File), node.Path);
            bool composite = kraNode.IsGroup || kraNode.Masks.Count > 0;
            var diff = Compare(image.Pixels, expected.Pixels, r.W * r.H);
            Report($"{node.Path} ({r.W}x{r.H} em {r.X},{r.Y})", diff, composite ? CompositeTolerance : 0);
            if (Environment.GetEnvironmentVariable("KRACHECK_VERBOSE") is "1" or "2" && diff.MaxDelta > (Environment.GetEnvironmentVariable("KRACHECK_VERBOSE") == "2" ? 0 : (composite ? CompositeTolerance : 0)))
                PrintSamples(image.Pixels, expected.Pixels, r);

            foreach (string warning in warnings.Distinct()) Console.WriteLine($"      aviso — {warning}");
        }

        Console.WriteLine();
    }

    private static void PrintSamples(byte[] mine, byte[] krita, RectI r)
    {
        int shown = 0;
        var histogram = new SortedDictionary<int, int>();
        for (int i = 0; i < r.W * r.H; i++)
        {
            int p = i * 4;
            if (mine[p + 3] == 0 && krita[p + 3] == 0) continue;
            int d = 0;
            for (int c = 0; c < 4; c++) d = Math.Max(d, Math.Abs(mine[p + c] - krita[p + c]));
            if (d == 0) continue;
            histogram[d] = histogram.GetValueOrDefault(d) + 1;
            if ((Environment.GetEnvironmentVariable("KRACHECK_VERBOSE") == "2" || d > CompositeTolerance) && shown++ < 40)
                Console.WriteLine($"      ({r.X + i % r.W},{r.Y + i / r.W}) meu BGRA {mine[p]},{mine[p+1]},{mine[p+2]},{mine[p+3]}  krita {krita[p]},{krita[p+1]},{krita[p+2]},{krita[p+3]}");
        }
        Console.WriteLine("      histograma: " + string.Join(" ", histogram.Select(kv => $"{kv.Key}:{kv.Value}")));
    }

    private static KraNode? FindNode(List<KraNode> nodes, string[] path, int depth = 0)
    {
        foreach (var node in nodes)
        {
            if (node.Name != path[depth]) continue;
            if (depth == path.Length - 1) return node;

            var found = FindNode(node.Children, path, depth + 1);
            if (found is not null) return found;
        }
        return null;
    }

    // ------------------------------------------------------------------
    // Atlas existente
    // ------------------------------------------------------------------

    private static void CheckAtlas(
        string sheetPath, LoadReport report,
        Dictionary<string, OracleNode>? oracle, string? oracleDir)
    {
        Console.WriteLine($"Sprites do atlas existente ({Path.GetFileName(sheetPath)})");

        var sheet = AtlasSheet.IsSheetFile(sheetPath)
            ? AtlasSheet.Read(sheetPath)
            : TpSheet.TryRead(sheetPath) ?? throw new InvalidDataException($"{sheetPath}: planilha ilegível");
        var atlas = ImageLoader.Load(Path.Combine(Path.GetDirectoryName(sheetPath)!, sheet.Texture), "atlas");
        if (atlas.Width != sheet.Width || atlas.Height != sheet.Height)
            Fail($"a planilha diz {sheet.Width}x{sheet.Height}, o PNG tem {atlas.Width}x{atlas.Height}");

        var byName = report.Sprites.ToDictionary(s => s.Name, StringComparer.Ordinal);
        var oracleByName = oracle?.Values
            .Where(n => n.File is not null && KraReader.IsSpriteLayerName(n.Path.Split('/')[^1]))
            .GroupBy(n => KraReader.ToSpriteName(n.Path.Split('/')[^1]))
            .ToDictionary(g => g.Key, g => g.First());

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in sheet.Sprites)
        {
            string name = entry.Name;
            var (x, y, w, h) = (entry.Rect.X, entry.Rect.Y, entry.Rect.W, entry.Rect.H);
            seen.Add(name);

            byte[] region = Crop(atlas, x, y, w, h);

            if (!byName.TryGetValue(name, out var sprite))
            {
                Fail($"{name}: está no atlas mas não saiu do .kra");
                continue;
            }

            if (sprite.Width != w || sprite.Height != h)
                Fail($"{name}: .kra dá {sprite.Width}x{sprite.Height}, atlas tem {w}x{h} (o .kra pode ter mudado depois)");
            else
                Report($"{name} x atlas", Compare(sprite.Pixels, region, w * h), 0);

            if (oracleByName is not null && oracleByName.TryGetValue(name, out var o) &&
                o.W == w && o.H == h)
            {
                var krita = ImageLoader.Load(Path.Combine(oracleDir!, o.File!), name);
                var diff = Compare(krita.Pixels, region, w * h);
                Console.WriteLine(diff.Pixels == 0
                    ? $"      (o export do Krita é idêntico ao atlas)"
                    : $"      (o export do Krita difere do atlas em {diff.Pixels} px — o .kra mudou depois do atlas?)");
            }
        }

        foreach (var sprite in report.Sprites.Where(s => !seen.Contains(s.Name)))
            Console.WriteLine($"  novo — {sprite.Name} sai do .kra mas não está no atlas atual");

        Console.WriteLine();
    }

    // ------------------------------------------------------------------
    // Geração completa
    // ------------------------------------------------------------------

    /// <summary>
    /// Gera o atlas como o app gera (PNG + as duas planilhas) numa pasta temporária e
    /// relê tudo: cada recorte do PNG tem que ser o sprite lido, e o .tpsheet tem que
    /// descrever os mesmos retângulos que o .atlasmith.json.
    /// </summary>
    private static void CheckRoundtrip(LoadReport report)
    {
        Console.WriteLine("Geração completa (PNG + .atlasmith.json + .tpsheet) e releitura");
        if (report.Sprites.Length == 0)
        {
            Console.WriteLine("  (nenhum sprite para gerar)");
            Console.WriteLine();
            return;
        }

        string folder = Path.Combine(Path.GetTempPath(), "kracheck-" + Guid.NewGuid().ToString("N"));
        try
        {
            var request = new AtlasRequest(folder, "kracheck", new PackSettings()) { Format = SheetFormat.Both };
            var outcome = AtlasBuilder.Build(report, request);
            if (outcome.Status != AtlasBuildStatus.Saved)
            {
                Fail($"a geração terminou em {outcome.Status}");
                return;
            }

            var json = AtlasSheet.Read(request.SheetPath);
            var tp = TpSheet.TryRead(request.TpSheetPath);
            var png = ImageLoader.Load(request.PngPath, "atlas");
            var byName = report.Sprites.ToDictionary(s => s.Name, StringComparer.Ordinal);

            if (json.Width != png.Width || json.Height != png.Height)
                Fail($"json diz {json.Width}x{json.Height}, PNG tem {png.Width}x{png.Height}");
            if (json.Sprites.Count != report.Sprites.Length)
                Fail($"json tem {json.Sprites.Count} sprites, foram lidos {report.Sprites.Length}");

            int identical = 0;
            foreach (var entry in json.Sprites)
            {
                if (!byName.TryGetValue(entry.Name, out var sprite))
                {
                    Fail($"{entry.Name}: está no json mas não foi lido");
                    continue;
                }

                var r = entry.Rect;
                if (r.W != sprite.Width || r.H != sprite.Height)
                {
                    Fail($"{entry.Name}: json {r.W}x{r.H}, sprite {sprite.Width}x{sprite.Height}");
                    continue;
                }

                var diff = Compare(sprite.Pixels, Crop(png, r.X, r.Y, r.W, r.H), r.W * r.H);
                if (diff.Pixels == 0) identical++;
                else Fail($"{entry.Name}: recorte do PNG difere em {diff.Pixels} px");
            }
            if (identical == json.Sprites.Count) Pass($"{identical} recortes do PNG idênticos aos sprites lidos");

            if (tp is null)
            {
                Fail(".tpsheet gerado não pôde ser lido");
            }
            else
            {
                var tpByName = tp.Sprites.ToDictionary(s => s.Name, StringComparer.Ordinal);
                int matching = json.Sprites.Count(j =>
                    tpByName.TryGetValue(j.Name, out var t) && t.Rect.Equals(j.Rect) &&
                    Math.Abs(t.PivotX - j.PivotX) < 1e-6 && Math.Abs(t.PivotY - j.PivotY) < 1e-6);

                if (matching == json.Sprites.Count && tp.Sprites.Count == json.Sprites.Count)
                    Pass($".tpsheet e .atlasmith.json descrevem os mesmos {matching} retângulos e pivôs");
                else
                    Fail($".tpsheet confere com o json em {matching} de {json.Sprites.Count} sprites");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // Pasta temporária: o Windows limpa depois.
            }
            Console.WriteLine();
        }
    }

    private static byte[] Crop(SpriteSource image, int x, int y, int w, int h)
    {
        var output = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
            Buffer.BlockCopy(image.Pixels, ((y + row) * image.Width + x) * 4, output, row * w * 4, w * 4);
        return output;
    }

    // ------------------------------------------------------------------

    private readonly record struct Diff(int Pixels, int MaxDelta);

    /// <summary>Diferença por pixel, ignorando a cor onde os dois alfas são zero (cor invisível).</summary>
    private static Diff Compare(byte[] a, byte[] b, int pixelCount)
    {
        int differing = 0, max = 0;
        for (int i = 0, p = 0; i < pixelCount; i++, p += 4)
        {
            if (a[p + 3] == 0 && b[p + 3] == 0) continue;

            int d = Math.Max(
                Math.Max(Math.Abs(a[p] - b[p]), Math.Abs(a[p + 1] - b[p + 1])),
                Math.Max(Math.Abs(a[p + 2] - b[p + 2]), Math.Abs(a[p + 3] - b[p + 3])));

            if (d > 0) differing++;
            if (d > max) max = d;
        }
        return new Diff(differing, max);
    }

    private static void Report(string label, Diff diff, int tolerance)
    {
        if (diff.Pixels == 0)
            Pass($"{label}: idêntico");
        else if (diff.MaxDelta <= tolerance)
            Pass($"{label}: {diff.Pixels} px com diferença de até {diff.MaxDelta} (tolerância {tolerance})");
        else
            Fail($"{label}: {diff.Pixels} px diferentes, até {diff.MaxDelta} por canal");
    }

    private static (int W, int H, byte[] Pixels) DecodePng(byte[] data)
    {
        using var stream = new MemoryStream(data);
        var decoder = BitmapDecoder.Create(
            stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);

        BitmapSource frame = decoder.Frames[0];
        if (frame.Format != PixelFormats.Bgra32)
            frame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

        var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4];
        frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
        return (frame.PixelWidth, frame.PixelHeight, pixels);
    }
}

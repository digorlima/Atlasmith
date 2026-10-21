using System.Diagnostics;
using System.Globalization;
using System.IO;
using Atlasmith.Core;
using Atlasmith.Services;

namespace PackerBench;

/// <summary>
/// Roda o pipeline completo sobre uma pasta de sprites e valida o resultado:
/// nada pode se sobrepor, tudo tem que caber no atlas, a margem pedida tem que
/// ser respeitada e as duas planilhas têm que bater com as posições calculadas.
/// Também compara com o empacotamento por linhas da versão antiga e, com uma
/// terceira pasta, com um atlas de referência (PNG pixel a pixel e .tpsheet).
///
/// Uso: PackerBench [entrada] [saída] [referência]
/// </summary>
internal static class Program
{
    private static int _failures;

    [STAThread]
    private static int Main(string[] args)
    {
        string input = args.Length > 0 ? args[0] : "testset";
        string output = args.Length > 1 ? args[1] : "bench-out";
        string? reference = args.Length > 2 ? args[2] : null;

        if (!Directory.Exists(input))
        {
            Console.WriteLine($"Pasta de entrada não encontrada: {input}");
            return 2;
        }

        Console.WriteLine($"Entrada: {Path.GetFullPath(input)}");
        var loadWatch = Stopwatch.StartNew();
        var report = ImageLoader.LoadFolder(input);
        loadWatch.Stop();

        foreach (string failure in report.Failures)
            Console.WriteLine($"  falha ao carregar — {failure}");

        var sprites = report.Sprites;
        Console.WriteLine($"{sprites.Length} sprites carregados em {loadWatch.ElapsedMilliseconds} ms");

        long spriteArea = sprites.Sum(s => s.Area);
        Console.WriteLine($"Área somada dos sprites: {spriteArea:N0} px");
        Console.WriteLine();

        CheckNaming(sprites, input);

        var settings = new PackSettings
        {
            Padding = 2,
            BorderPadding = 0,
            Extrude = 0,
            MaxSize = 4096,
            PowerOfTwo = true,
            ForceSquare = false
        };

        Console.WriteLine("MaxRects (Atlasmith)");
        var packWatch = Stopwatch.StartNew();
        var result = AtlasPacker.Pack(
            sprites.Select(s => (s.Width, s.Height)).ToArray(),
            settings,
            msg => Console.WriteLine($"  {msg}"));
        packWatch.Stop();

        if (result is null)
        {
            Console.WriteLine("FALHA: o packer não encontrou nenhum atlas.");
            return 1;
        }

        Console.WriteLine($"  => {result.Width}x{result.Height}  " +
                          $"ocupação {result.Occupancy * 100:0.0}%  " +
                          $"({result.Strategy})  {packWatch.ElapsedMilliseconds} ms");
        Console.WriteLine();

        Console.WriteLine("Shelf / row-based (versão antiga, para comparação)");
        var shelf = ShelfReference.Pack(sprites, settings.Padding, settings.MaxSize);
        Console.WriteLine(shelf is null
            ? "  => não coube em nenhum tamanho"
            : $"  => {shelf.Value.W}x{shelf.Value.H}  " +
              $"ocupação {(double)spriteArea / ((long)shelf.Value.W * shelf.Value.H) * 100:0.0}%");
        Console.WriteLine();

        ValidatePlacements(sprites, result, settings);

        // A gravação passa pelo mesmo AtlasBuilder do app e do Unity.
        var request = new AtlasRequest(Path.GetFullPath(output), "BenchAtlas", settings) { Format = SheetFormat.Both };
        var outcome = AtlasBuilder.Build(report, request, line => Console.WriteLine(line));
        if (outcome.Status != AtlasBuildStatus.Saved || outcome.Pack is null)
        {
            Console.WriteLine($"FALHA: a geração terminou em {outcome.Status}.");
            return 1;
        }

        if (outcome.Pack.Width != result.Width || outcome.Pack.Height != result.Height ||
            !outcome.Pack.Placements.Select(p => (p.Index, p.X, p.Y)).SequenceEqual(result.Placements.Select(p => (p.Index, p.X, p.Y))))
            Fail("o AtlasBuilder empacotou diferente do AtlasPacker com as mesmas opções");

        foreach (string written in outcome.Written)
            Console.WriteLine($"Gravado {written} ({new FileInfo(written).Length:N0} bytes)");
        Console.WriteLine();

        ValidateSheet(request.TpSheetPath, sprites, result);
        ValidateJson(request.SheetPath, sprites, result);
        if (reference is not null) CompareWithReference(request, reference);

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

    // ------------------------------------------------------------------

    private static void CheckNaming(SpriteSource[] sprites, string root)
    {
        Console.WriteLine("Regra de nomenclatura (pasta vira '-', espaço vira '_')");

        var byName = sprites.ToDictionary(s => s.Name, s => s);

        string[] expected =
        [
            "Ally_Bar-Ornament_Borders_m012",
            "Action_Order-Crystal_m012",
            "Global_Mana-Mask_Third_m012",
            "Skill_Label-Ornament_m012"
        ];

        // Esses nomes só existem no testset do Ingame_Panel; em outras pastas a
        // verificação abaixo (sem espaços, sem separador, sem extensão) já cobre.
        if (expected.Any(byName.ContainsKey))
        {
            foreach (string name in expected)
            {
                if (byName.ContainsKey(name)) Pass(name);
                else Fail($"esperava um sprite chamado {name}");
            }
        }
        else
        {
            Pass("conjunto genérico — checagem de nomes fixos ignorada");
        }

        foreach (var s in sprites)
        {
            if (s.Name.Contains(' ')) Fail($"nome com espaço: {s.Name}");
            if (s.Name.Contains(Path.DirectorySeparatorChar)) Fail($"nome com separador: {s.Name}");
            if (s.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                Fail($"nome manteve a extensão: {s.Name}");
        }

        int duplicates = sprites.Length - sprites.Select(s => s.Name).Distinct().Count();
        if (duplicates > 0) Fail($"{duplicates} nomes duplicados");
        else Pass("todos os nomes são únicos");

        Console.WriteLine();
    }

    private static void ValidatePlacements(SpriteSource[] sprites, PackResult result, PackSettings settings)
    {
        Console.WriteLine("Validação geométrica");

        var p = result.Placements;

        if (p.Length != sprites.Length) Fail($"{p.Length} posições para {sprites.Length} sprites");
        else Pass($"todos os {sprites.Length} sprites foram posicionados");

        bool sizesOk = true;
        foreach (var item in p)
        {
            var s = sprites[item.Index];
            if (item.W != s.Width || item.H != s.Height)
            {
                Fail($"{s.Name}: posição diz {item.W}x{item.H}, imagem é {s.Width}x{s.Height}");
                sizesOk = false;
            }
        }
        if (sizesOk) Pass("as dimensões batem com as imagens de origem");

        bool insideOk = true;
        foreach (var item in p)
        {
            if (item.X < 0 || item.Y < 0 ||
                item.X + item.W > result.Width || item.Y + item.H > result.Height)
            {
                Fail($"{sprites[item.Index].Name} sai do atlas: {item.X},{item.Y} {item.W}x{item.H}");
                insideOk = false;
            }

            if (item.X < settings.BorderPadding || item.Y < settings.BorderPadding ||
                item.X + item.W > result.Width - settings.BorderPadding ||
                item.Y + item.H > result.Height - settings.BorderPadding)
            {
                Fail($"{sprites[item.Index].Name} invade a margem da borda");
                insideOk = false;
            }
        }
        if (insideOk) Pass("nenhum sprite ultrapassa os limites do atlas");

        int overlaps = 0;
        int tooClose = 0;
        for (int i = 0; i < p.Length; i++)
        {
            for (int j = i + 1; j < p.Length; j++)
            {
                bool overlap =
                    p[i].X < p[j].X + p[j].W && p[i].X + p[i].W > p[j].X &&
                    p[i].Y < p[j].Y + p[j].H && p[i].Y + p[i].H > p[j].Y;

                if (overlap)
                {
                    if (overlaps < 5)
                        Console.WriteLine($"    {sprites[p[i].Index].Name} x {sprites[p[j].Index].Name}");
                    overlaps++;
                    continue;
                }

                // Distância entre as caixas nos dois eixos; a separação real é a maior delas.
                int gapX = Math.Max(p[j].X - (p[i].X + p[i].W), p[i].X - (p[j].X + p[j].W));
                int gapY = Math.Max(p[j].Y - (p[i].Y + p[i].H), p[i].Y - (p[j].Y + p[j].H));
                if (Math.Max(gapX, gapY) < settings.Padding) tooClose++;
            }
        }

        if (overlaps > 0) Fail($"{overlaps} par(es) de sprites se sobrepõem");
        else Pass("nenhuma sobreposição");

        if (tooClose > 0) Fail($"{tooClose} par(es) mais próximos que a margem de {settings.Padding}px");
        else Pass($"margem de {settings.Padding}px respeitada entre todos os pares");

        if (settings.PowerOfTwo)
        {
            bool pow2 = IsPowerOfTwo(result.Width) && IsPowerOfTwo(result.Height);
            if (pow2) Pass($"dimensões em potência de 2 ({result.Width}x{result.Height})");
            else Fail($"{result.Width}x{result.Height} não são potências de 2");
        }

        double ratio = (double)Math.Max(result.Width, result.Height) / Math.Min(result.Width, result.Height);
        if (ratio > 4) Fail($"formato muito alongado: proporção {ratio:0.0}:1");
        else Pass($"proporção equilibrada ({ratio:0.0}:1)");

        Console.WriteLine();
    }

    private static void ValidateSheet(string path, SpriteSource[] sprites, PackResult result)
    {
        Console.WriteLine("Validação do .tpsheet");

        string[] lines = File.ReadAllLines(path);
        var header = lines.TakeWhile(l => l.StartsWith('#') || l.StartsWith(':')).ToArray();
        var entries = lines.Skip(header.Length).Where(l => l.Length > 0).ToArray();

        void Expect(string prefix, string expected)
        {
            string? line = header.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
            if (line == expected) Pass(expected);
            else Fail($"esperava \"{expected}\", veio \"{line ?? "(ausente)"}\"");
        }

        Expect(":format=", ":format=40300");
        Expect(":texture=", ":texture=BenchAtlas.png");
        Expect(":size=", $":size={result.Width}x{result.Height}");
        Expect(":pivotpoints=", ":pivotpoints=enabled");
        Expect(":borders=", ":borders=disabled");
        Expect(":alphahandling=", ":alphahandling=ClearTransparentPixels");

        if (entries.Length == sprites.Length) Pass($"{entries.Length} linhas de sprite");
        else Fail($"{entries.Length} linhas para {sprites.Length} sprites");

        var placementByName = result.Placements.ToDictionary(
            p => sprites[p.Index].Name, p => p);

        bool linesOk = true;
        foreach (string line in entries)
        {
            string[] parts = line.Split(';');
            if (parts.Length != 11)
            {
                Fail($"linha com {parts.Length} campos (esperado 11): {line}");
                linesOk = false;
                continue;
            }

            string name = parts[0];
            if (!placementByName.TryGetValue(name, out var placement))
            {
                Fail($"nome desconhecido no arquivo: {name}");
                linesOk = false;
                continue;
            }

            int x = int.Parse(parts[1], CultureInfo.InvariantCulture);
            int y = int.Parse(parts[2], CultureInfo.InvariantCulture);
            int w = int.Parse(parts[3], CultureInfo.InvariantCulture);
            int h = int.Parse(parts[4], CultureInfo.InvariantCulture);

            // O arquivo usa origem no canto inferior esquerdo.
            int expectedY = result.Height - placement.Y - placement.H;

            if (x != placement.X || y != expectedY || w != placement.W || h != placement.H)
            {
                Fail($"{name}: arquivo diz {x};{y};{w};{h}, esperado {placement.X};{expectedY};{placement.W};{placement.H}");
                linesOk = false;
            }

            if (parts[5] != " 0.5" || parts[6] != "0.5")
            {
                Fail($"{name}: pivô inesperado ({parts[5]};{parts[6]})");
                linesOk = false;
            }

            if (y < 0 || y + h > result.Height)
            {
                Fail($"{name}: Y invertido cai fora do atlas ({y})");
                linesOk = false;
            }
        }

        if (linesOk) Pass("todas as linhas conferem, com Y a partir do canto inferior esquerdo");

        bool sorted = entries
            .Select(l => l.Split(';')[0])
            .SequenceEqual(entries.Select(l => l.Split(';')[0]).OrderBy(n => n, StringComparer.Ordinal));
        if (sorted) Pass("linhas em ordem alfabética");
        else Fail("linhas fora de ordem");
    }

    private static void ValidateJson(string path, SpriteSource[] sprites, PackResult result)
    {
        Console.WriteLine("Validação do .atlasmith.json");

        var sheet = AtlasSheet.Read(path);
        if (sheet.Width == result.Width && sheet.Height == result.Height && sheet.Texture == "BenchAtlas.png")
            Pass($"textura BenchAtlas.png, {sheet.Width}x{sheet.Height}");
        else
            Fail($"cabeçalho: {sheet.Texture} {sheet.Width}x{sheet.Height}");

        var byName = sheet.Sprites.ToDictionary(s => s.Name, StringComparer.Ordinal);
        int ok = 0;
        foreach (var p in result.Placements)
        {
            string name = sprites[p.Index].Name;
            if (!byName.TryGetValue(name, out var entry))
            {
                Fail($"{name}: ausente do json");
                continue;
            }

            // O json usa origem no canto superior esquerdo, como o PNG.
            if (entry.Rect.Equals(new RectI(p.X, p.Y, p.W, p.H)) && !entry.HasCustomPivot && !entry.HasBorder) ok++;
            else Fail($"{name}: json diz {entry.Rect}, esperado {p.X},{p.Y} {p.W}x{p.H}");
        }

        if (ok == result.Placements.Length && sheet.Sprites.Count == sprites.Length)
            Pass($"{ok} retângulos conferem, com Y a partir do canto superior esquerdo");

        // Reler e regravar não pode mudar uma vírgula: o formato é estável no git.
        string text = File.ReadAllText(path);
        if (AtlasSheet.Parse(text).ToJson() == text) Pass("reler e regravar dá o mesmo arquivo");
        else Fail("reler e regravar muda o arquivo");
    }

    /// <summary>PNG idêntico pixel a pixel e .tpsheet com as mesmas linhas de sprite.</summary>
    private static void CompareWithReference(AtlasRequest request, string folder)
    {
        Console.WriteLine($"Comparação com a referência ({Path.GetFullPath(folder)})");

        var mine = ImageLoader.Load(request.PngPath, "novo");
        var theirs = ImageLoader.Load(Path.Combine(folder, "BenchAtlas.png"), "referência");
        if (mine.Width != theirs.Width || mine.Height != theirs.Height)
            Fail($"PNG {mine.Width}x{mine.Height}, referência {theirs.Width}x{theirs.Height}");
        else if (mine.Pixels.AsSpan().SequenceEqual(theirs.Pixels))
            Pass($"PNG idêntico pixel a pixel ({mine.Width}x{mine.Height})");
        else
            Fail("PNG com pixels diferentes da referência");

        static string[] SpriteLines(string path) =>
            File.ReadAllLines(path).Where(l => l.Length > 0 && l[0] is not '#' and not ':').ToArray();

        var lines = SpriteLines(request.TpSheetPath);
        var expected = SpriteLines(Path.Combine(folder, "BenchAtlas.tpsheet"));
        if (lines.SequenceEqual(expected)) Pass($".tpsheet com as mesmas {lines.Length} linhas de sprite");
        else Fail($".tpsheet difere da referência ({lines.Length} linhas, referência {expected.Length})");

        Console.WriteLine();
    }

    private static bool IsPowerOfTwo(int v) => v > 0 && (v & (v - 1)) == 0;
}

/// <summary>
/// Reprodução do empacotamento por linhas usado na versão em Python (antes do DaigoPacker 2.0),
/// só para dimensionar o ganho do MaxRects.
/// </summary>
internal static class ShelfReference
{
    public static (int W, int H)? Pack(SpriteSource[] sprites, int padding, int maxSize)
    {
        int[] dims = [256, 512, 1024, 2048, 4096, 8192];
        (int W, int H)? best = null;

        var ordered = sprites.OrderByDescending(s => s.Height).ToArray();

        foreach (int w in dims)
        {
            if (w > maxSize) continue;
            foreach (int h in dims)
            {
                if (h > maxSize) continue;
                if (best is not null && (long)w * h >= (long)best.Value.W * best.Value.H) continue;
                if (!FitsShelf(ordered, w, h, padding)) continue;
                best = (w, h);
            }
        }

        return best;
    }

    private static bool FitsShelf(SpriteSource[] sprites, int width, int height, int padding)
    {
        int cursorX = 0, cursorY = 0, rowHeight = 0;

        foreach (var s in sprites)
        {
            int w = s.Width + padding;
            int h = s.Height + padding;

            if (cursorX + w > width)
            {
                cursorX = 0;
                cursorY += rowHeight;
                rowHeight = 0;
            }

            if (cursorY + h > height) return false;

            cursorX += w;
            rowHeight = Math.Max(rowHeight, h);
        }

        return true;
    }
}

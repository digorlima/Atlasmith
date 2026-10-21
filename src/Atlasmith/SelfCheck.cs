using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Xml;
using Atlasmith.Core;
using Atlasmith.Localization;

namespace Atlasmith;

/// <summary>
/// "Atlasmith.exe --self-check [arquivo]": confere, no executável publicado, o que o
/// enxugamento do pacote poderia ter quebrado sem aparecer no build. Sai com 0 se
/// tudo passou; o relatório vai para o arquivo (um app de janela não tem console).
/// </summary>
internal static class SelfCheck
{
    public static int Run(string? reportPath)
    {
        var report = new StringBuilder($"{AtlasmithInfo.Name} {AtlasmithInfo.Version} self-check\n");
        bool ok = true;

        void Check(string name, Action test)
        {
            try
            {
                test();
                report.Append("ok    ").Append(name).Append('\n');
            }
            catch (Exception ex)
            {
                ok = false;
                var inner = ex is TargetInvocationException { InnerException: { } cause } ? cause : ex;
                report.Append("FALHA ").Append(name).Append(": ").Append(inner.GetType().Name)
                      .Append(": ").Append(inner.Message).Append('\n');
            }
        }

        // A janela vem antes do teste de renderização: quem cria o primeiro visual liga o
        // compositor, e é na sequência real da abertura (tema, idioma, janela) que ele falha.
        Check("montar uma janela com tema e textos (sem mostrar)", CheckWindow);
        Check("renderização do WPF (wpfgfx)", CheckRendering);
        Check("DLLs nativas do WPF no pacote, não emprestadas do PATH", CheckNativeFiles);
        Check("XML (System.Private.Xml)", CheckXml);
        Check("copiar texto de um campo (Ctrl+C)", CheckCopy);
        Check("textos em português e inglês", CheckTexts);
        Check("gerar atlas com PNG, .atlasmith.json e .tpsheet", CheckPipeline);

        report.Append(ok ? "resultado: ok\n" : "resultado: FALHOU\n");
        if (reportPath is { Length: > 0 })
        {
            try
            {
                File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
            }
            catch (Exception)
            {
                return 2;
            }
        }
        return ok ? 0 : 1;
    }

    /// <summary>
    /// A janela do instalador passa pelo mesmo caminho da principal (dicionários de tema,
    /// estilos, textos dinâmicos) sem depender da configuração do usuário.
    /// </summary>
    private static void CheckWindow()
    {
        Texts.Apply(AppLanguage.System);
        Services.ThemeManager.Initialize(Services.AppTheme.System);

        string root = Path.Combine(Path.GetTempPath(), "atlasmith-selfcheck-" + Guid.NewGuid().ToString("N"));
        var window = new InstallerWindow(Services.InstallLayout.ForTest(root), null, null, null);
        if (window.Content is not FrameworkElement) throw new InvalidOperationException("janela sem conteúdo");
    }

    /// <summary>
    /// As DLLs nativas do WPF têm que estar ao lado do renderizador. Uma cópia achada
    /// no PATH faz o app abrir aqui e fechar no PC de outra pessoa — foi o caso da 2.1.
    /// </summary>
    private static void CheckNativeFiles()
    {
        var renderer = System.Diagnostics.Process.GetCurrentProcess().Modules
            .Cast<System.Diagnostics.ProcessModule>()
            .FirstOrDefault(m => string.Equals(m.ModuleName, "wpfgfx_cor3.dll", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("wpfgfx_cor3.dll não foi carregado");

        string folder = Path.GetDirectoryName(renderer.FileName)!;
        var missing = new[] { "D3DCompiler_47_cor3.dll", "vcruntime140_cor3.dll", "PresentationNative_cor3.dll" }
            .Where(name => !File.Exists(Path.Combine(folder, name)))
            .ToList();
        if (missing.Count > 0) throw new FileNotFoundException($"faltam em {folder}: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// O primeiro elemento posto numa árvore visual liga o compositor nativo (a mesma
    /// coisa que abrir uma janela, mas sem nada na tela): é aí que falta de DLL nativa
    /// derruba o app antes da janela aparecer.
    /// </summary>
    private static void CheckRendering()
    {
        var border = new Border
        {
            Width = 8,
            Height = 8,
            Background = System.Windows.Media.Brushes.Magenta
        };
        var root = new Grid();
        root.Children.Add(border);
        root.Measure(new Size(8, 8));
        root.Arrange(new Rect(0, 0, 8, 8));

        var bitmap = new RenderTargetBitmap(8, 8, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(root);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(4, 4, 1, 1), pixel, 4, 0);
        if (pixel[0] != 0xFF || pixel[1] != 0x00 || pixel[2] != 0xFF)
            throw new InvalidOperationException($"pixel renderizado {pixel[2]},{pixel[1]},{pixel[0]}");
    }

    private static void CheckXml()
    {
        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, new XmlWriterSettings { OmitXmlDeclaration = true }))
            writer.WriteElementString("a", "b");
        if (sb.ToString() != "<a>b</a>") throw new InvalidOperationException(sb.ToString());
    }

    /// <summary>
    /// O caminho exato do Ctrl+C num TextBox, sem tocar na área de transferência: foi
    /// ele que derrubou a 2.1 quando o System.Private.Xml saiu do pacote.
    /// </summary>
    private static void CheckCopy()
    {
        var assembly = typeof(TextBox).Assembly;
        var copyPaste = assembly.GetType("System.Windows.Documents.TextEditorCopyPaste", throwOnError: true)!;
        var create = copyPaste.GetMethod("_CreateDataObject", BindingFlags.NonPublic | BindingFlags.Static)
                     ?? throw new MissingMethodException(copyPaste.FullName, "_CreateDataObject");
        RuntimeHelpers.PrepareMethod(create.MethodHandle);

        var box = new TextBox { Text = "Atlasmith" };
        box.SelectAll();
        object editor = typeof(TextBoxBase)
                            .GetProperty("TextEditor", BindingFlags.NonPublic | BindingFlags.Instance)?
                            .GetValue(box)
                        ?? throw new MissingMemberException(nameof(TextBoxBase), "TextEditor");

        var data = (IDataObject)create.Invoke(null, [editor, false])!;
        if (data.GetData(DataFormats.UnicodeText) as string != "Atlasmith")
            throw new InvalidOperationException("o texto copiado não confere");
    }

    private static void CheckTexts()
    {
        foreach (var language in new[] { TextLanguage.Portuguese, TextLanguage.English })
        {
            var texts = Texts.For(language);
            foreach (var property in typeof(Texts).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.PropertyType == typeof(string) && string.IsNullOrWhiteSpace((string?)property.GetValue(texts)))
                    throw new InvalidOperationException($"{language}: {property.Name} vazio");
            }
        }
    }

    private static void CheckPipeline()
    {
        string folder = Path.Combine(Path.GetTempPath(), "atlasmith-selfcheck-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);

            var sprites = new[] { Solid("hero", 12, 20, 0x30), Solid("coin", 8, 8, 0xC0) };
            var request = new AtlasRequest(folder, "check", new PackSettings()) { Format = SheetFormat.Both };
            var outcome = AtlasBuilder.Build(new LoadReport(sprites, []), request);
            if (outcome.Status != AtlasBuildStatus.Saved) throw new InvalidOperationException(outcome.Status.ToString());

            var sheet = AtlasSheet.Read(request.SheetPath);
            var tp = TpSheet.TryRead(request.TpSheetPath) ?? throw new InvalidDataException(".tpsheet ilegível");
            if (sheet.Sprites.Count != 2 || tp.Sprites.Count != 2) throw new InvalidDataException("planilha incompleta");

            using var stream = File.OpenRead(request.PngPath);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            if (frame.PixelWidth != sheet.Width || frame.PixelHeight != sheet.Height)
                throw new InvalidDataException("PNG e planilha com tamanhos diferentes");
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
        }
    }

    private static SpriteSource Solid(string name, int width, int height, byte shade)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = shade;
            pixels[i + 1] = (byte)(255 - shade);
            pixels[i + 2] = 0x80;
            pixels[i + 3] = 0xFF;
        }
        return new SpriteSource(name, width, height, pixels);
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Atlasmith.Core
{
    /// <summary>Que planilha acompanha o PNG.</summary>
    public enum SheetFormat
    {
        /// <summary>.atlasmith.json — lido pelo importer do pacote do Unity e fácil de ler em qualquer engine.</summary>
        Atlasmith,

        /// <summary>.tpsheet — para projetos que ainda usam o TexturePackerImporter.</summary>
        TpSheet,

        Both
    }

    public enum AtlasBuildStage
    {
        Packing,
        Composing,
        Saving
    }

    public enum AtlasBuildStatus
    {
        Saved,

        /// <summary>Nenhum sprite para empacotar; veja LoadReport.Failures para saber se foi erro de leitura.</summary>
        NoSprites,

        /// <summary>Não coube no tamanho máximo.</summary>
        DoesNotFit
    }

    /// <summary>Onde e como gravar um atlas.</summary>
    public sealed class AtlasRequest
    {
        public AtlasRequest(string outputFolder, string name, PackSettings settings)
        {
            OutputFolder = outputFolder;
            Name = name;
            Settings = settings;
        }

        public string OutputFolder { get; }
        public string Name { get; }
        public PackSettings Settings { get; }

        public SheetFormat Format { get; set; } = SheetFormat.Atlasmith;

        /// <summary>A receita que vai junto na planilha, para gerar de novo depois.</summary>
        public AtlasSource? Source { get; set; }

        public string Generator { get; set; } = AtlasmithInfo.Generator;

        public string PngPath => Path.Combine(OutputFolder, Name + ".png");
        public string SheetPath => AtlasSheet.PathFor(OutputFolder, Name);
        public string TpSheetPath => TpSheet.PathFor(OutputFolder, Name);

        public bool WritesAtlasmith => Format != SheetFormat.TpSheet;
        public bool WritesTpSheet => Format != SheetFormat.Atlasmith;

        /// <summary>
        /// Planilhas do outro formato com o mesmo nome. Ficariam desatualizadas ao lado
        /// do PNG novo — e, no Unity, dois importers brigariam pela mesma textura.
        /// </summary>
        public string[] StaleSheets()
        {
            var stale = new List<string>();
            if (!WritesAtlasmith && File.Exists(SheetPath)) stale.Add(SheetPath);
            if (!WritesTpSheet && File.Exists(TpSheetPath)) stale.Add(TpSheetPath);
            return stale.ToArray();
        }

        /// <summary>
        /// Sprites da planilha atual do destino que esta geração deixaria de fora. No
        /// Unity, cada um é uma referência que quebra (prefab, cena, animação), então
        /// quem gera deve mostrar a lista e pedir confirmação antes de gravar.
        /// </summary>
        public List<string> RemovedSprites(LoadReport report)
        {
            var removed = new List<string>();
            var previous = AtlasNaming.TryReadSheet(OutputFolder, Name);
            if (previous is null) return removed;

            var kept = new HashSet<string>(report.Sprites.Select(s => s.Name), StringComparer.Ordinal);
            foreach (var sprite in previous.Sprites)
            {
                if (!kept.Contains(sprite.Name)) removed.Add(sprite.Name);
            }
            return removed;
        }
    }

    public sealed class AtlasBuildOutcome
    {
        public AtlasBuildOutcome(AtlasBuildStatus status, LoadReport report)
        {
            Status = status;
            Report = report;
        }

        public AtlasBuildStatus Status { get; }
        public LoadReport Report { get; }

        public PackResult? Pack { get; set; }

        /// <summary>O atlas em BGRA32, para a pré-visualização.</summary>
        public byte[]? Pixels { get; set; }

        public AtlasSheet? Sheet { get; set; }

        /// <summary>Arquivos gravados, na ordem: PNG e planilhas.</summary>
        public List<string> Written { get; } = new List<string>();
    }

    /// <summary>
    /// O caminho inteiro de sprites lidos até os arquivos no disco: empacota, monta a
    /// imagem e grava PNG e planilha. O app e o Unity passam pelo mesmo código, então
    /// o mesmo .kra com as mesmas opções dá o mesmo atlas nos dois.
    /// </summary>
    public static class AtlasBuilder
    {
        public static AtlasBuildOutcome Build(
            LoadReport report,
            AtlasRequest request,
            Action<string>? log = null,
            Action<AtlasBuildStage>? stage = null,
            CancellationToken ct = default)
        {
            var text = CoreText.Current;

            foreach (string note in report.Notes) log?.Invoke("  " + note);
            foreach (string failure in report.Failures) log?.Invoke("  " + text.NoteFailure(failure));

            var sprites = report.Sprites;
            if (sprites.Length == 0) return new AtlasBuildOutcome(AtlasBuildStatus.NoSprites, report);

            stage?.Invoke(AtlasBuildStage.Packing);
            var settings = request.Settings.Clamped();
            var sizes = sprites.Select(s => (s.Width, s.Height)).ToArray();
            var pack = AtlasPacker.Pack(sizes, settings, log, ct);
            if (pack is null) return new AtlasBuildOutcome(AtlasBuildStatus.DoesNotFit, report);

            log?.Invoke(text.PackSummary(pack.Width, pack.Height, pack.Strategy, pack.CandidatesTried));

            stage?.Invoke(AtlasBuildStage.Composing);
            byte[] pixels = AtlasComposer.Compose(sprites, pack, settings.Extrude, ct);
            var sheet = BuildSheet(sprites, pack, request, settings);

            ct.ThrowIfCancellationRequested();
            stage?.Invoke(AtlasBuildStage.Saving);

            var outcome = new AtlasBuildOutcome(AtlasBuildStatus.Saved, report)
            {
                Pack = pack,
                Pixels = pixels,
                Sheet = sheet
            };

            Write(request.PngPath, outcome, log, path => AtomicFile.WriteAllBytes(path, PngWriter.Encode(pixels, pack.Width, pack.Height)));
            if (request.WritesAtlasmith) Write(request.SheetPath, outcome, log, sheet.Save);
            if (request.WritesTpSheet) Write(request.TpSheetPath, outcome, log, path => TpSheet.Save(path, sheet));

            return outcome;
        }

        private static AtlasSheet BuildSheet(SpriteSource[] sprites, PackResult pack, AtlasRequest request, PackSettings settings)
        {
            var sheet = new AtlasSheet
            {
                Generator = request.Generator,
                Texture = request.Name + ".png",
                Width = pack.Width,
                Height = pack.Height,
                Source = request.Source,
                Settings = settings
            };

            // Pivô e bordas não saem do Krita: quem os ajustou na planilha anterior não os perde.
            var previous = AtlasNaming.TryReadSheet(request.OutputFolder, request.Name);

            foreach (var p in pack.Placements)
            {
                var sprite = new AtlasSheetSprite(sprites[p.Index].Name, new RectI(p.X, p.Y, p.W, p.H));
                var before = previous?.Find(sprite.Name);
                if (before != null) sprite.CopyMetadataFrom(before);
                sheet.Sprites.Add(sprite);
            }

            return sheet;
        }

        private static void Write(string path, AtlasBuildOutcome outcome, Action<string>? log, Action<string> write)
        {
            if (File.Exists(path)) log?.Invoke(CoreText.Current.Replacing(Path.GetFileName(path)));
            write(path);
            outcome.Written.Add(path);
            log?.Invoke(CoreText.Current.SavedTo(path));
        }
    }
}

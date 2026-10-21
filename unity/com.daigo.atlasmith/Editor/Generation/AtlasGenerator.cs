#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atlasmith.Core;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Atlasmith.Editor
{
    /// <summary>Um sprite que está no atlas de hoje e que a geração nova deixaria de fora.</summary>
    public sealed class RemovedSprite
    {
        public RemovedSprite(string name, bool isHiddenLayer, long? internalId, List<string> referencedBy)
        {
            Name = name;
            IsHiddenLayer = isHiddenLayer;
            InternalId = internalId;
            ReferencedBy = referencedBy;
        }

        public string Name { get; }

        /// <summary>Ficou de fora só porque a camada está desativada no Krita.</summary>
        public bool IsHiddenLayer { get; }

        public long? InternalId { get; }

        /// <summary>Arquivos do projeto que apontam para este sprite (vazio se ninguém aponta ou se a busca não rodou).</summary>
        public List<string> ReferencedBy { get; }
    }

    /// <summary>
    /// Por que uma geração parou antes de gravar: sairiam sprites do atlas. Cada um pode
    /// ser uma referência que quebra, então quem gera decide (gerar mesmo assim, incluir
    /// as camadas ocultas ou cancelar).
    /// </summary>
    public sealed class SpriteRemovalCheck
    {
        public SpriteRemovalCheck(List<RemovedSprite> sprites, bool hiddenLayersIgnored, bool referencesScanned)
        {
            Sprites = sprites;
            HiddenLayersIgnored = hiddenLayersIgnored;
            ReferencesScanned = referencesScanned;
        }

        /// <summary>Os referenciados primeiro, depois por nome.</summary>
        public List<RemovedSprite> Sprites { get; }

        /// <summary>A geração estava ignorando camadas ocultas (então incluí-las pode trazer sprites de volta).</summary>
        public bool HiddenLayersIgnored { get; }

        /// <summary>A busca por referências rodou (precisa do .meta da textura com os IDs).</summary>
        public bool ReferencesScanned { get; }

        public int HiddenCount => Sprites.Count(s => s.IsHiddenLayer);
        public int ReferencedCount => Sprites.Count(s => s.ReferencedBy.Count > 0);
        public List<string> Names => Sprites.Select(s => s.Name).ToList();

        /// <summary>Incluir as camadas ocultas traria pelo menos um sprite de volta.</summary>
        public bool CanIncludeHidden => HiddenLayersIgnored && HiddenCount > 0;
    }

    /// <summary>O resultado de uma geração: o que saiu, quanto tempo levou e o log.</summary>
    public sealed class AtlasGenerationResult
    {
        public AtlasGenerationResult(AtlasRecipe recipe) => Recipe = recipe;

        public AtlasRecipe Recipe { get; }

        public AtlasBuildStatus? Status { get; internal set; }
        public bool Cancelled { get; internal set; }
        public string? Error { get; internal set; }
        public AtlasBuildOutcome? Outcome { get; internal set; }

        /// <summary>
        /// Preenchido quando a geração parou sem gravar nada porque sairiam sprites do
        /// atlas. Para seguir mesmo assim, use <see cref="AtlasGenerator.Resume"/>.
        /// </summary>
        public SpriteRemovalCheck? PendingRemoval { get; internal set; }

        public bool Succeeded => Status == AtlasBuildStatus.Saved && Error == null && !Cancelled;

        public int Width { get; internal set; }
        public int Height { get; internal set; }
        public int SpriteCount { get; internal set; }
        public double Occupancy { get; internal set; }
        public double Seconds { get; internal set; }

        public List<string> AddedSprites { get; } = new List<string>();
        public List<string> RemovedSprites { get; } = new List<string>();

        /// <summary>Sprites que já existiam e continuaram com o mesmo spriteID e fileID.</summary>
        public int KeptSprites { get; internal set; }

        /// <summary>Sprites que existiam e perderam o ID — nunca deveria acontecer.</summary>
        public List<string> ChangedIds { get; } = new List<string>();

        public List<string> Warnings { get; } = new List<string>();
        public List<string> Log { get; } = new List<string>();

        // As camadas já lidas, para Resume não precisar ler o arquivo do Krita de novo.
        internal LoadReport? Report;
    }

    /// <summary>Uma geração em andamento: a leitura e o empacotamento correm fora da thread principal.</summary>
    public sealed class AtlasGenerationJob
    {
        internal AtlasGenerationJob(AtlasRecipe recipe) => Result = new AtlasGenerationResult(recipe);

        internal const int StageReading = 0;
        internal const int StageCheckingRemoval = 4;

        internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
        internal readonly ConcurrentQueue<string> PendingLog = new ConcurrentQueue<string>();
        internal readonly Stopwatch Clock = Stopwatch.StartNew();
        internal Task<GenerationOutput>? Work;
        internal SpriteImportState? Before;
        internal bool LocksHeld;
        internal bool Finished;
        internal double EarlierSeconds;

        // 0 = lendo camadas, 1..3 = estágios do AtlasBuilder (empacotar, montar, salvar), 4 = conferindo o que sai.
        private int _stage;
        private int _done, _total;

        public AtlasGenerationResult Result { get; }

        public bool IsRunning => Work != null && !Work.IsCompleted;
        public bool IsCompleted => Work == null || Work.IsCompleted;

        /// <summary>0..1, aproximado (a leitura das camadas é a parte longa).</summary>
        public float Progress
        {
            get
            {
                int stage = Volatile.Read(ref _stage);
                if (stage == StageCheckingRemoval) return 0.82f;
                if (stage > 0) return 0.8f + 0.06f * stage;
                int total = Volatile.Read(ref _total);
                return total <= 0 ? 0f : 0.8f * Math.Min(1f, Volatile.Read(ref _done) / (float)total);
            }
        }

        /// <summary>O que está acontecendo agora, no idioma da interface.</summary>
        public string Stage
        {
            get
            {
                var text = Texts.Current;
                switch (Volatile.Read(ref _stage))
                {
                    case 1: return text.StagePacking;
                    case 2: return text.StageComposing;
                    case 3: return text.StageSaving;
                    case StageCheckingRemoval: return text.StageCheckingRemoval;
                    default: return text.StageReading(Volatile.Read(ref _done), Volatile.Read(ref _total));
                }
            }
        }

        internal void SetStage(AtlasBuildStage stage) => Volatile.Write(ref _stage, (int)stage + 1);

        internal void SetStage(int stage) => Volatile.Write(ref _stage, stage);

        internal void SetProgress(int done, int total)
        {
            Volatile.Write(ref _total, total);
            Volatile.Write(ref _done, done);
        }

        public void Cancel() => Cancellation.Cancel();

        /// <summary>Passa as linhas novas do log para o resultado e as devolve (thread principal).</summary>
        public List<string> DrainLog()
        {
            var lines = new List<string>();
            while (PendingLog.TryDequeue(out string? line))
            {
                Result.Log.Add(line);
                lines.Add(line);
            }
            return lines;
        }
    }

    /// <summary>O que a parte em segundo plano devolve: as camadas lidas e, se chegou a gravar, o atlas.</summary>
    internal sealed class GenerationOutput
    {
        public GenerationOutput(LoadReport report, AtlasBuildOutcome? build, SpriteRemovalCheck? removal)
        {
            Report = report;
            Build = build;
            Removal = removal;
        }

        public LoadReport Report { get; }
        public AtlasBuildOutcome? Build { get; }
        public SpriteRemovalCheck? Removal { get; }
    }

    /// <summary>
    /// Gera o atlas de um arquivo do Krita pelo mesmo núcleo do app (KraReader +
    /// AtlasBuilder) e importa o resultado. É o caminho do menu "Create Atlas", da
    /// janela e de quem chamar pela API (modo batch, testes).
    ///
    /// Proteção contra sprites sumindo: antes de gravar por cima de um atlas, a geração
    /// compara com a planilha atual. Se algum sprite sairia (camada apagada, renomeada ou
    /// desativada), nada é gravado e o resultado volta com <see cref="AtlasGenerationResult.PendingRemoval"/>
    /// — a não ser que <c>allowRemovingSprites</c> seja true.
    /// </summary>
    public static class AtlasGenerator
    {
        /// <summary>
        /// Gera e importa de forma síncrona (modo batch, testes). Se o destino ainda for um
        /// .tpsheet, converte antes quando <paramref name="convertTexturePacker"/> for true;
        /// senão, falha. Se sairiam sprites do atlas e <paramref name="allowRemovingSprites"/>
        /// for false, não grava nada e avisa no Console.
        /// </summary>
        public static AtlasGenerationResult Generate(AtlasRecipe recipe, bool convertTexturePacker = false, bool allowRemovingSprites = false)
        {
            var result = Wait(Start(recipe, convertTexturePacker, allowRemovingSprites));
            if (result.PendingRemoval != null) Debug.LogWarning(Texts.Current.RemovalSkipped(result.Recipe.PngPath, result.PendingRemoval));
            return result;
        }

        /// <summary>Versão síncrona de <see cref="Resume"/>.</summary>
        public static AtlasGenerationResult GenerateAnyway(AtlasGenerationResult paused) => Wait(Resume(paused));

        private static AtlasGenerationResult Wait(AtlasGenerationJob job)
        {
            try
            {
                job.Work?.Wait();
            }
            catch (AggregateException)
            {
                // O erro é tratado em Finish.
            }
            return Finish(job);
        }

        /// <summary>Começa a geração (thread principal). Chame <see cref="Finish"/> quando o job terminar.</summary>
        public static AtlasGenerationJob Start(AtlasRecipe recipe, bool convertTexturePacker = false, bool allowRemovingSprites = false)
        {
            AtlasmithLanguage.Apply();
            var text = Texts.Current;
            recipe = recipe.Clone();
            var job = new AtlasGenerationJob(recipe);
            var result = job.Result;

            string kritaFull = ProjectPaths.ToFull(recipe.KritaPath);
            if (!KraReader.IsKritaFile(kritaFull) || !File.Exists(kritaFull))
            {
                result.Error = text.NotAKritaFile(recipe.KritaPath);
                return job;
            }

            if (AtlasNaming.InvalidCharIn(recipe.AtlasName) != null || recipe.AtlasName.Trim().Length == 0)
            {
                result.Error = text.InvalidAtlasName(recipe.AtlasName);
                return job;
            }

            if (recipe.HasTexturePackerSheet)
            {
                if (!convertTexturePacker)
                {
                    result.Error = text.TargetIsTexturePacker(recipe.TpSheetPath);
                    return job;
                }

                var conversion = TpSheetConverter.Convert(recipe.TpSheetPath);
                foreach (string message in conversion.Messages) result.Log.Add(message);
                if (conversion.Status != TpConversionStatus.Converted)
                {
                    result.Error = text.ConversionBeforeGenerateFailed(recipe.TpSheetPath);
                    return job;
                }
            }

            job.Before = CaptureBefore(recipe);
            var request = CreateRequest(recipe, kritaFull);
            var options = new KraReadOptions { IgnoreHiddenLayers = recipe.IgnoreHiddenLayers };
            var guard = allowRemovingSprites ? null : new RemovalGuard(recipe, kritaFull, job.Before);
            var ct = job.Cancellation.Token;

            result.Log.Add(text.LogGenerating(recipe.KritaPath, recipe.PngPath));
            result.Log.Add(CoreText.Current.ReadingLayers(recipe.KritaPath));

            Lock(job);
            job.Work = Task.Run(() =>
            {
                var report = KraReader.LoadSprites(kritaFull, options, job.SetProgress, ct);
                job.PendingLog.Enqueue(CoreText.Current.LayersRead(report.Sprites.Length));
                ct.ThrowIfCancellationRequested();

                // Sem sprites, o AtlasBuilder não grava nada (e o erro diz por quê).
                if (guard != null && report.Sprites.Length > 0)
                {
                    var removed = request.RemovedSprites(report);
                    if (removed.Count > 0)
                    {
                        job.SetStage(AtlasGenerationJob.StageCheckingRemoval);
                        return new GenerationOutput(report, null, guard.Check(removed, ct));
                    }
                }

                return new GenerationOutput(report, AtlasBuilder.Build(report, request, job.PendingLog.Enqueue, job.SetStage, ct), null);
            }, ct);

            return job;
        }

        /// <summary>
        /// Continua uma geração que parou por <see cref="AtlasGenerationResult.PendingRemoval"/>:
        /// grava o atlas sem os sprites que saíram, com as camadas já lidas.
        /// </summary>
        public static AtlasGenerationJob Resume(AtlasGenerationResult paused)
        {
            AtlasmithLanguage.Apply();
            var text = Texts.Current;
            var recipe = paused.Recipe.Clone();
            var job = new AtlasGenerationJob(recipe) { EarlierSeconds = paused.Seconds };
            var result = job.Result;
            result.Log.AddRange(paused.Log);

            var report = paused.Report;
            if (paused.PendingRemoval == null || report == null)
            {
                result.Error = text.GenerationFailedUnknown;
                return job;
            }

            result.Log.Add(text.RemovalAccepted(paused.PendingRemoval.Sprites.Count));
            job.Before = CaptureBefore(recipe);
            var request = CreateRequest(recipe, ProjectPaths.ToFull(recipe.KritaPath));
            var ct = job.Cancellation.Token;

            Lock(job);
            job.SetStage(AtlasBuildStage.Packing);
            job.Work = Task.Run(() => new GenerationOutput(report, AtlasBuilder.Build(report, request, job.PendingLog.Enqueue, job.SetStage, ct), null), ct);
            return job;
        }

        /// <summary>Termina a geração na thread principal: importa e preenche o resultado.</summary>
        public static AtlasGenerationResult Finish(AtlasGenerationJob job)
        {
            var result = job.Result;
            if (job.Finished) return result;
            job.Finished = true;

            var text = Texts.Current;
            var recipe = result.Recipe;
            try
            {
                if (job.Work == null) return result;

                job.DrainLog();

                GenerationOutput output;
                try
                {
                    output = job.Work.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    result.Cancelled = true;
                    result.Log.Add(text.GenerationCancelled);
                    return result;
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                    result.Log.Add(text.LogError(ex.Message));
                    return result;
                }
                finally
                {
                    job.DrainLog();
                }

                result.Report = output.Report;
                if (output.Removal != null)
                {
                    result.PendingRemoval = output.Removal;
                    result.Log.Add(text.RemovalStopped(output.Removal.Sprites.Count));
                    foreach (var sprite in output.Removal.Sprites)
                    {
                        result.Log.Add("  " + text.RemovalLine(sprite));
                        foreach (string file in sprite.ReferencedBy.Take(10)) result.Log.Add("      " + file);
                    }
                    return result;
                }

                var outcome = output.Build!;
                result.Outcome = outcome;
                result.Status = outcome.Status;
                if (outcome.Status == AtlasBuildStatus.NoSprites)
                {
                    result.Error = outcome.Report.Failures.Length > 0 ? CoreText.Current.NoSpriteCouldBeRead : CoreText.Current.NoLayerBecameSpriteDetail;
                    return result;
                }
                if (outcome.Status == AtlasBuildStatus.DoesNotFit)
                {
                    result.Error = CoreText.Current.DoesNotFit(recipe.Settings.Clamped().MaxSize);
                    return result;
                }

                Import(recipe);
                Summarize(job, outcome);
                return result;
            }
            finally
            {
                if (job.LocksHeld)
                {
                    job.LocksHeld = false;
                    AssetDatabase.AllowAutoRefresh();
                    EditorApplication.UnlockReloadAssemblies();
                }
                job.Clock.Stop();
                result.Seconds = job.EarlierSeconds + job.Clock.Elapsed.TotalSeconds;
            }
        }

        private static SpriteImportState? CaptureBefore(AtlasRecipe recipe) =>
            File.Exists(ProjectPaths.ToFull(recipe.PngPath)) && AssetImporter.GetAtPath(recipe.PngPath) is TextureImporter
                ? SpriteImportState.Capture(recipe.PngPath)
                : null;

        private static AtlasRequest CreateRequest(AtlasRecipe recipe, string kritaFull)
        {
            string outputFull = ProjectPaths.ToFull(recipe.OutputFolder);
            return new AtlasRequest(outputFull, recipe.AtlasName, recipe.Settings.Clamped())
            {
                Format = SheetFormat.Atlasmith,
                Source = AtlasSource.For(AtlasSourceKind.Krita, kritaFull, outputFull, recipe.IgnoreHiddenLayers)
            };
        }

        // Nada de refresh automático nem recompilação enquanto os arquivos são gravados.
        private static void Lock(AtlasGenerationJob job)
        {
            AssetDatabase.DisallowAutoRefresh();
            EditorApplication.LockReloadAssemblies();
            job.LocksHeld = true;
        }

        private static void Import(AtlasRecipe recipe)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                AssetDatabase.ImportAsset(recipe.SheetPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.ImportAsset(recipe.PngPath, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        private static void Summarize(AtlasGenerationJob job, AtlasBuildOutcome outcome)
        {
            var result = job.Result;
            var text = Texts.Current;
            var recipe = result.Recipe;

            if (outcome.Pack != null)
            {
                result.Width = outcome.Pack.Width;
                result.Height = outcome.Pack.Height;
                result.Occupancy = outcome.Pack.Occupancy;
            }
            var sheetNames = new HashSet<string>(outcome.Sheet?.Sprites.Select(s => s.Name) ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            result.SpriteCount = sheetNames.Count;

            var after = SpriteImportState.Capture(recipe.PngPath);
            var before = job.Before;
            if (before != null)
            {
                foreach (var old in before.Sprites)
                {
                    if (!sheetNames.Contains(old.Name))
                    {
                        result.RemovedSprites.Add(old.Name);
                        continue;
                    }
                    var now = after.Find(old.Name);
                    if (now != null && now.SpriteId == old.SpriteId && now.InternalId == old.InternalId) result.KeptSprites++;
                    else result.ChangedIds.Add(old.Name);
                }
            }
            foreach (string name in sheetNames)
                if (before?.Find(name) == null) result.AddedSprites.Add(name);

            if (result.RemovedSprites.Count > 0) result.Warnings.Add(text.WarnRemovedSprites(result.RemovedSprites));
            if (result.ChangedIds.Count > 0) result.Warnings.Add(text.WarnChangedIds(result.ChangedIds));
            if (after.Sprites.Count != result.SpriteCount) result.Warnings.Add(text.WarnImportCount(after.Sprites.Count, result.SpriteCount));

            if (AssetImporter.GetAtPath(recipe.PngPath) is TextureImporter importer)
            {
                int side = Math.Max(result.Width, result.Height);
                if (importer.maxTextureSize < side) result.Warnings.Add(text.WarnMaxTextureSize(importer.maxTextureSize, side));
            }

            result.Log.Add(text.LogSummary(result.SpriteCount, result.KeptSprites, result.AddedSprites.Count, result.RemovedSprites.Count));
            foreach (string warning in result.Warnings) result.Log.Add(text.LogWarning(warning));
        }

        /// <summary>
        /// Monta o <see cref="SpriteRemovalCheck"/>. O que depende do AssetDatabase (GUID e
        /// IDs da textura) é colhido na thread principal; a busca roda em segundo plano.
        /// </summary>
        private sealed class RemovalGuard
        {
            private readonly string _kritaFull;
            private readonly bool _ignoreHidden;
            private readonly string _projectRoot;
            private readonly string _textureGuid;
            private readonly Dictionary<string, long> _idByName = new Dictionary<string, long>(StringComparer.Ordinal);

            public RemovalGuard(AtlasRecipe recipe, string kritaFull, SpriteImportState? before)
            {
                _kritaFull = kritaFull;
                _ignoreHidden = recipe.IgnoreHiddenLayers;
                _projectRoot = ProjectPaths.ProjectRoot;
                _textureGuid = AssetDatabase.AssetPathToGUID(recipe.PngPath) ?? "";
                if (before == null) return;

                // Os IDs dos sprites de hoje; a tabela de nomes cobre também os que só ficaram nela.
                foreach (var pair in before.NameFileIds) _idByName[pair.Key] = pair.Value;
                foreach (var sprite in before.Sprites) _idByName[sprite.Name] = sprite.InternalId;
            }

            public SpriteRemovalCheck Check(List<string> removed, CancellationToken ct)
            {
                var hidden = new HashSet<string>(StringComparer.Ordinal);
                if (_ignoreHidden)
                {
                    try
                    {
                        hidden = KraReader.Inspect(_kritaFull, new KraReadOptions { IgnoreHiddenLayers = true }).HiddenSpriteNames();
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        // Sem a marcação de ocultas; a lista continua valendo.
                    }
                }

                var ids = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (string name in removed)
                    if (_idByName.TryGetValue(name, out long id) && id != 0) ids[name] = id;

                bool scanned = _textureGuid.Length > 0 && ids.Count > 0;
                var references = scanned
                    ? SpriteReferenceScanner.Scan(_projectRoot, _textureGuid, ids.Values.ToList(), ct)
                    : new Dictionary<long, List<string>>();

                var sprites = removed
                    .Select(name =>
                    {
                        long? id = ids.TryGetValue(name, out long found) ? found : (long?)null;
                        var refs = id.HasValue && references.TryGetValue(id.Value, out var files) ? files : new List<string>();
                        return new RemovedSprite(name, hidden.Contains(name), id, refs);
                    })
                    .OrderByDescending(s => s.ReferencedBy.Count > 0)
                    .ThenBy(s => s.Name, StringComparer.Ordinal)
                    .ToList();

                return new SpriteRemovalCheck(sprites, _ignoreHidden, scanned);
            }
        }
    }
}

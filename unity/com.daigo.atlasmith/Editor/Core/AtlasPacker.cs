#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Atlasmith.Core
{
    /// <summary>Posição final de um sprite dentro do atlas (canto superior esquerdo, sem extrude).</summary>
    public readonly struct Placement
    {
        public Placement(int index, int x, int y, int w, int h)
        {
            Index = index;
            X = x;
            Y = y;
            W = w;
            H = h;
        }

        /// <summary>Índice do sprite na lista passada ao empacotador.</summary>
        public int Index { get; }
        public int X { get; }
        public int Y { get; }
        public int W { get; }
        public int H { get; }
    }

    public sealed class PackResult
    {
        public PackResult(int width, int height, Placement[] placements, double occupancy, string strategy, int candidatesTried)
        {
            Width = width;
            Height = height;
            Placements = placements;
            Occupancy = occupancy;
            Strategy = strategy;
            CandidatesTried = candidatesTried;
        }

        public int Width { get; }
        public int Height { get; }
        public Placement[] Placements { get; }

        /// <summary>Fração da área do atlas coberta por pixels de sprite.</summary>
        public double Occupancy { get; }

        /// <summary>Nome da combinação heurística + ordenação que venceu.</summary>
        public string Strategy { get; }

        /// <summary>Quantos pares (largura, altura) foram testados até achar este.</summary>
        public int CandidatesTried { get; }
    }

    /// <summary>Ordem em que os sprites são apresentados ao MaxRects.</summary>
    internal enum SortMode
    {
        AreaDesc,
        MaxSideDesc,
        HeightDesc,
        WidthDesc,
        PerimeterDesc
    }

    /// <summary>
    /// Busca o menor atlas que comporta todos os sprites.
    ///
    /// Diferenças em relação à versão antiga (shelf / row-based):
    ///   1. Usa MaxRects, que aproveita o espaço acima de cada "linha".
    ///   2. Testa 5 heurísticas x 5 ordenações por tamanho candidato, em paralelo.
    ///   3. Ordena os candidatos por área E por proximidade do quadrado, então entre
    ///      1024x8192 e 2048x4096 (mesma área) o formato equilibrado vence sempre.
    /// </summary>
    public static class AtlasPacker
    {
        private const int MinDimension = 32;

        private static readonly FreeRectChoice[] Rules =
        {
            FreeRectChoice.BestShortSideFit,
            FreeRectChoice.BestAreaFit,
            FreeRectChoice.BestLongSideFit,
            FreeRectChoice.BottomLeftRule,
            FreeRectChoice.ContactPointRule
        };

        private static readonly SortMode[] Sorts =
        {
            SortMode.AreaDesc,
            SortMode.MaxSideDesc,
            SortMode.HeightDesc,
            SortMode.WidthDesc,
            SortMode.PerimeterDesc
        };

        /// <summary>Acima deste número de sprites, a passada "offline" (O(n^2)) é pulada.</summary>
        private const int OfflineLimit = 350;

        public static PackResult? Pack(
            IReadOnlyList<(int W, int H)> sprites,
            PackSettings settings,
            Action<string>? log = null,
            CancellationToken ct = default)
        {
            if (sprites.Count == 0) return null;

            int pad = Math.Max(0, settings.Padding);
            int border = Math.Max(0, settings.BorderPadding);
            int extrude = Math.Max(0, settings.Extrude);
            int grow = 2 * extrude + pad;

            // Cada sprite é inflado pelo extrude (dos dois lados) e pelo padding (de um lado só).
            // O padding do último sprite de cada linha cai fora do bin útil, por isso o bin
            // efetivo ganha "+ pad" de volta — é o mesmo truque que o TexturePacker usa.
            var inflated = new (int W, int H)[sprites.Count];
            long neededArea = 0;
            int maxW = 0, maxH = 0;

            for (int i = 0; i < sprites.Count; i++)
            {
                int w = sprites[i].W + grow;
                int h = sprites[i].H + grow;
                inflated[i] = (w, h);
                neededArea += (long)w * h;
                maxW = Math.Max(maxW, w);
                maxH = Math.Max(maxH, h);
            }

            var candidates = BuildCandidates(settings, maxW, maxH, border, pad, neededArea);
            if (candidates.Count == 0)
            {
                log?.Invoke(CoreText.Current.NoAtlasFits(settings.MaxSize, maxW - grow, maxH - grow));
                return null;
            }

            long spriteArea = 0;
            foreach (var s in sprites) spriteArea += (long)s.W * s.H;

            int tried = 0;
            foreach (var (binW, binH) in candidates)
            {
                ct.ThrowIfCancellationRequested();
                tried++;

                int effW = binW - 2 * border + pad;
                int effH = binH - 2 * border + pad;
                if (effW < maxW || effH < maxH) continue;

                var attempt = TryFit(inflated, effW, effH, ct);
                if (attempt is null)
                {
                    log?.Invoke(CoreText.Current.CandidateRejected(binW, binH));
                    continue;
                }

                var (rects, strategy) = attempt.Value;

                // Volta do retângulo inflado para o retângulo real do sprite.
                var placements = new Placement[sprites.Count];
                for (int i = 0; i < sprites.Count; i++)
                {
                    var r = rects[i];
                    placements[i] = new Placement(
                        i,
                        border + r.X + extrude,
                        border + r.Y + extrude,
                        sprites[i].W,
                        sprites[i].H);
                }

                int finalW = binW, finalH = binH;

                if (!settings.PowerOfTwo && settings.ShrinkToFit)
                {
                    int usedRight = 0, usedBottom = 0;
                    foreach (var p in placements)
                    {
                        usedRight = Math.Max(usedRight, p.X + p.W + extrude);
                        usedBottom = Math.Max(usedBottom, p.Y + p.H + extrude);
                    }
                    finalW = Math.Min(binW, RoundUpTo(usedRight + border, 4));
                    finalH = Math.Min(binH, RoundUpTo(usedBottom + border, 4));
                    if (settings.ForceSquare) finalW = finalH = Math.Max(finalW, finalH);
                }

                log?.Invoke(CoreText.Current.CandidateAccepted(binW, binH, strategy));

                return new PackResult(
                    finalW,
                    finalH,
                    placements,
                    (double)spriteArea / ((long)finalW * finalH),
                    strategy,
                    tried);
            }

            log?.Invoke(CoreText.Current.NothingFits(settings.MaxSize, sprites.Count));
            return null;
        }

        /// <summary>
        /// Roda todas as combinações heurística x ordenação para um tamanho de bin.
        /// Retorna a que ocupa o menor retângulo real (desempate determinístico pelo índice).
        /// </summary>
        private static (RectI[] Rects, string Strategy)? TryFit(
            (int W, int H)[] inflated, int effW, int effH, CancellationToken ct)
        {
            var jobs = new List<(int Order, FreeRectChoice Rule, SortMode Sort, bool Offline)>();
            int order = 0;

            foreach (var sort in Sorts)
                foreach (var rule in Rules)
                    jobs.Add((order++, rule, sort, false));

            if (inflated.Length <= OfflineLimit)
                foreach (var rule in Rules)
                    jobs.Add((order++, rule, SortMode.AreaDesc, true));

            var results = new ConcurrentBag<(int Order, RectI[] Rects, long BoundArea, string Name)>();

            var options = new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount)
            };

            Parallel.ForEach(jobs, options, job =>
            {
                var rects = job.Offline
                    ? RunOffline(inflated, effW, effH, job.Rule)
                    : RunOnline(inflated, effW, effH, job.Rule, job.Sort);

                if (rects is null) return;

                int right = 0, bottom = 0;
                foreach (var r in rects)
                {
                    if (r.Right > right) right = r.Right;
                    if (r.Bottom > bottom) bottom = r.Bottom;
                }

                string name = job.Offline
                    ? $"{job.Rule} / global"
                    : $"{job.Rule} / {job.Sort}";

                results.Add((job.Order, rects, (long)right * bottom, name));
            });

            if (results.IsEmpty) return null;

            var best = results
                .OrderBy(r => r.BoundArea)
                .ThenBy(r => r.Order)
                .First();

            return (best.Rects, best.Name);
        }

        private static RectI[]? RunOnline(
            (int W, int H)[] inflated, int effW, int effH, FreeRectChoice rule, SortMode sort)
        {
            var order = BuildOrder(inflated, sort);
            var bin = new MaxRectsBin(effW, effH);
            var result = new RectI[inflated.Length];

            foreach (int idx in order)
            {
                if (!bin.TryInsert(inflated[idx].W, inflated[idx].H, rule, out var placed))
                    return null;
                result[idx] = placed;
            }
            return result;
        }

        private static RectI[]? RunOffline(
            (int W, int H)[] inflated, int effW, int effH, FreeRectChoice rule)
        {
            var bin = new MaxRectsBin(effW, effH);
            var map = new int[inflated.Length];
            var placed = new RectI[inflated.Length];

            if (!bin.TryInsertAll(inflated, rule, map, placed))
                return null;

            var result = new RectI[inflated.Length];
            for (int i = 0; i < inflated.Length; i++)
                result[map[i]] = placed[i];
            return result;
        }

        private static int[] BuildOrder((int W, int H)[] items, SortMode sort)
        {
            var idx = new int[items.Length];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;

            Comparison<int> cmp = sort switch
            {
                SortMode.AreaDesc => (a, b) =>
                    ((long)items[b].W * items[b].H).CompareTo((long)items[a].W * items[a].H),
                SortMode.MaxSideDesc => (a, b) =>
                    Math.Max(items[b].W, items[b].H).CompareTo(Math.Max(items[a].W, items[a].H)),
                SortMode.HeightDesc => (a, b) => items[b].H.CompareTo(items[a].H),
                SortMode.WidthDesc => (a, b) => items[b].W.CompareTo(items[a].W),
                _ => (a, b) => (items[b].W + items[b].H).CompareTo(items[a].W + items[a].H)
            };

            // Desempate pelo índice original mantém o resultado reprodutível entre execuções.
            Array.Sort(idx, (a, b) =>
            {
                int c = cmp(a, b);
                return c != 0 ? c : a.CompareTo(b);
            });
            return idx;
        }

        /// <summary>
        /// Monta a lista de tamanhos a testar, do mais promissor para o menos.
        /// A ordenação é (área, desequilíbrio do formato) — é ela que impede o
        /// resultado em tira que a versão antiga produzia.
        /// </summary>
        private static List<(int W, int H)> BuildCandidates(
            PackSettings settings, int maxW, int maxH, int border, int pad, long neededArea)
        {
            var dims = new List<int>();
            int max = Math.Max(MinDimension, settings.MaxSize);

            if (settings.PowerOfTwo)
            {
                for (int d = MinDimension; d <= max; d *= 2) dims.Add(d);
            }
            else
            {
                // Sem potência de 2 ainda usamos os mesmos degraus como limite superior de
                // busca; o encolhimento final (ShrinkToFit) é que dá o tamanho exato.
                for (int d = MinDimension; d <= max; d *= 2) dims.Add(d);
                if (dims.Count == 0 || dims[dims.Count - 1] != max) dims.Add(max);
            }

            var list = new List<(int W, int H)>();
            foreach (int w in dims)
            {
                foreach (int h in dims)
                {
                    if (settings.ForceSquare && w != h) continue;
                    if (w - 2 * border + pad < maxW) continue;
                    if (h - 2 * border + pad < maxH) continue;
                    if ((long)(w - 2 * border + pad) * (h - 2 * border + pad) < neededArea) continue;
                    list.Add((w, h));
                }
            }

            list.Sort((a, b) =>
            {
                long areaA = (long)a.W * a.H;
                long areaB = (long)b.W * b.H;
                int c = areaA.CompareTo(areaB);
                if (c != 0) return c;

                if (settings.Bias == SizeBias.PreferSquare)
                {
                    c = AspectSkew(a).CompareTo(AspectSkew(b));
                    if (c != 0) return c;
                }

                // Entre dois formatos igualmente desequilibrados, prefere o mais largo:
                // texturas largas costumam se comportar melhor em atlas de UI.
                return b.W.CompareTo(a.W);
            });

            return list;
        }

        // Lado maior / lado menor: ordena igual a |log2(W) - log2(H)|, mas é exato para
        // potências de 2, então formatos espelhados (512x2048 e 2048x512) empatam de verdade.
        private static double AspectSkew((int W, int H) s) =>
            (double)Math.Max(s.W, s.H) / Math.Min(s.W, s.H);

        private static int RoundUpTo(int value, int multiple)
        {
            if (value <= 0) return multiple;
            int r = value % multiple;
            return r == 0 ? value : value + (multiple - r);
        }
    }
}

#nullable enable

using System;
using System.Collections.Generic;

namespace Atlasmith.Core
{
    /// <summary>Regras de escolha do retângulo livre no MaxRects (Jylanki).</summary>
    public enum FreeRectChoice
    {
        BestShortSideFit,
        BestLongSideFit,
        BestAreaFit,
        BottomLeftRule,
        ContactPointRule
    }

    /// <summary>
    /// Implementação do algoritmo MaxRects. Mantém a lista de retângulos livres máximos e,
    /// a cada inserção, escolhe a posição segundo a heurística pedida.
    /// Muito superior ao empacotamento por linhas (shelf), que desperdiça todo o espaço
    /// acima de cada linha e acaba forçando atlas em tira do tipo 1024x8192.
    /// </summary>
    public sealed class MaxRectsBin
    {
        private readonly int _binW;
        private readonly int _binH;
        private readonly List<RectI> _free = new(128);
        private readonly List<RectI> _used = new(128);

        public MaxRectsBin(int width, int height)
        {
            _binW = width;
            _binH = height;
            _free.Add(new RectI(0, 0, width, height));
        }

        public IReadOnlyList<RectI> Used => _used;

        /// <summary>Área ocupada dividida pela área total do bin.</summary>
        public double Occupancy
        {
            get
            {
                long used = 0;
                foreach (var r in _used) used += r.Area;
                return (double)used / ((long)_binW * _binH);
            }
        }

        /// <summary>Insere um retângulo na melhor posição disponível segundo a heurística.</summary>
        public bool TryInsert(int w, int h, FreeRectChoice rule, out RectI placed)
        {
            placed = FindPosition(w, h, rule, out _, out _);
            if (placed.W == 0 || placed.H == 0) return false;
            Place(in placed);
            return true;
        }

        /// <summary>
        /// Inserção "offline": a cada passo escolhe globalmente qual retângulo ainda pendente
        /// tem o melhor encaixe. É O(n^2), mas costuma render melhor que uma ordem fixa,
        /// então só é usada em conjuntos pequenos.
        /// </summary>
        public bool TryInsertAll(
            IReadOnlyList<(int W, int H)> rects,
            FreeRectChoice rule,
            int[] outIndexMap,
            RectI[] outPlacements)
        {
            var pending = new List<int>(rects.Count);
            for (int i = 0; i < rects.Count; i++) pending.Add(i);

            int placedCount = 0;
            while (pending.Count > 0)
            {
                int bestScore1 = int.MaxValue, bestScore2 = int.MaxValue;
                int bestPendingIdx = -1;
                RectI bestRect = default;

                for (int i = 0; i < pending.Count; i++)
                {
                    var (w, h) = rects[pending[i]];
                    var node = FindPosition(w, h, rule, out int s1, out int s2);
                    if (node.W == 0 || node.H == 0) continue;

                    if (s1 < bestScore1 || (s1 == bestScore1 && s2 < bestScore2))
                    {
                        bestScore1 = s1;
                        bestScore2 = s2;
                        bestRect = node;
                        bestPendingIdx = i;
                    }
                }

                if (bestPendingIdx < 0) return false;

                Place(in bestRect);
                outIndexMap[placedCount] = pending[bestPendingIdx];
                outPlacements[placedCount] = bestRect;
                placedCount++;
                pending.RemoveAt(bestPendingIdx);
            }

            return true;
        }

        private RectI FindPosition(int w, int h, FreeRectChoice rule, out int score1, out int score2)
        {
            RectI best = default;
            score1 = int.MaxValue;
            score2 = int.MaxValue;

            for (int i = 0; i < _free.Count; i++)
            {
                var fr = _free[i];
                if (fr.W < w || fr.H < h) continue;

                int s1, s2;
                switch (rule)
                {
                    case FreeRectChoice.BestShortSideFit:
                    {
                        int leftoverH = fr.W - w;
                        int leftoverV = fr.H - h;
                        s1 = Math.Min(leftoverH, leftoverV);
                        s2 = Math.Max(leftoverH, leftoverV);
                        break;
                    }
                    case FreeRectChoice.BestLongSideFit:
                    {
                        int leftoverH = fr.W - w;
                        int leftoverV = fr.H - h;
                        s2 = Math.Min(leftoverH, leftoverV);
                        s1 = Math.Max(leftoverH, leftoverV);
                        break;
                    }
                    case FreeRectChoice.BestAreaFit:
                    {
                        long leftoverArea = fr.Area - (long)w * h;
                        s1 = (int)Math.Min(int.MaxValue, leftoverArea);
                        s2 = Math.Min(fr.W - w, fr.H - h);
                        break;
                    }
                    case FreeRectChoice.BottomLeftRule:
                    {
                        s1 = fr.Y + h;
                        s2 = fr.X;
                        break;
                    }
                    case FreeRectChoice.ContactPointRule:
                    {
                        // Quanto maior o contato com bordas e vizinhos, melhor.
                        // Invertido porque o restante do código minimiza o score.
                        s1 = -ContactPointScore(fr.X, fr.Y, w, h);
                        s2 = 0;
                        break;
                    }
                    default:
                        continue;
                }

                if (s1 < score1 || (s1 == score1 && s2 < score2))
                {
                    best = new RectI(fr.X, fr.Y, w, h);
                    score1 = s1;
                    score2 = s2;
                }
            }

            if (best.W == 0 || best.H == 0)
            {
                score1 = int.MaxValue;
                score2 = int.MaxValue;
            }
            return best;
        }

        private int ContactPointScore(int x, int y, int w, int h)
        {
            int score = 0;
            if (x == 0 || x + w == _binW) score += h;
            if (y == 0 || y + h == _binH) score += w;

            foreach (var u in _used)
            {
                if (u.X == x + w || u.Right == x)
                    score += CommonLength(u.Y, u.Bottom, y, y + h);
                if (u.Y == y + h || u.Bottom == y)
                    score += CommonLength(u.X, u.Right, x, x + w);
            }
            return score;
        }

        private static int CommonLength(int a1, int a2, int b1, int b2)
        {
            if (a1 >= b2 || a2 <= b1) return 0;
            return Math.Min(a2, b2) - Math.Max(a1, b1);
        }

        private void Place(in RectI node)
        {
            for (int i = 0; i < _free.Count; i++)
            {
                if (SplitFreeNode(_free[i], in node))
                {
                    _free.RemoveAt(i);
                    i--;
                }
            }
            PruneFreeList();
            _used.Add(node);
        }

        /// <summary>
        /// Divide um retângulo livre em torno do nó recém-ocupado.
        /// Retorna false quando não houve interseção (nada a fazer).
        /// </summary>
        private bool SplitFreeNode(RectI free, in RectI used)
        {
            if (!free.Intersects(in used)) return false;

            if (used.X < free.Right && used.Right > free.X)
            {
                // Sobra acima
                if (used.Y > free.Y && used.Y < free.Bottom)
                    _free.Add(new RectI(free.X, free.Y, free.W, used.Y - free.Y));

                // Sobra abaixo
                if (used.Bottom < free.Bottom)
                    _free.Add(new RectI(free.X, used.Bottom, free.W, free.Bottom - used.Bottom));
            }

            if (used.Y < free.Bottom && used.Bottom > free.Y)
            {
                // Sobra à esquerda
                if (used.X > free.X && used.X < free.Right)
                    _free.Add(new RectI(free.X, free.Y, used.X - free.X, free.H));

                // Sobra à direita
                if (used.Right < free.Right)
                    _free.Add(new RectI(used.Right, free.Y, free.Right - used.Right, free.H));
            }

            return true;
        }

        /// <summary>Descarta retângulos livres contidos em outros, mantendo apenas os maximais.</summary>
        private void PruneFreeList()
        {
            for (int i = 0; i < _free.Count; i++)
            {
                for (int j = i + 1; j < _free.Count; j++)
                {
                    if (_free[j].Contains(_free[i]))
                    {
                        _free.RemoveAt(i);
                        i--;
                        break;
                    }
                    if (_free[i].Contains(_free[j]))
                    {
                        _free.RemoveAt(j);
                        j--;
                    }
                }
            }
        }
    }
}

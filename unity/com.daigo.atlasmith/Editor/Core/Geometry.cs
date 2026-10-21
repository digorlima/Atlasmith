#nullable enable

namespace Atlasmith.Core
{
    /// <summary>Retângulo inteiro simples usado por todo o pipeline de empacotamento.</summary>
    public struct RectI
    {
        public int X;
        public int Y;
        public int W;
        public int H;

        public RectI(int x, int y, int w, int h)
        {
            X = x;
            Y = y;
            W = w;
            H = h;
        }

        public readonly int Right => X + W;
        public readonly int Bottom => Y + H;
        public readonly long Area => (long)W * H;

        public readonly bool Contains(in RectI o) =>
            o.X >= X && o.Y >= Y && o.Right <= Right && o.Bottom <= Bottom;

        public readonly bool Intersects(in RectI o) =>
            X < o.Right && Right > o.X && Y < o.Bottom && Bottom > o.Y;

        public override readonly string ToString() => $"({X},{Y} {W}x{H})";
    }
}

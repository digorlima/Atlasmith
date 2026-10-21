#nullable enable

namespace Atlasmith.Core
{
    /// <summary>Como o algoritmo escolhe entre dois atlas de mesma área.</summary>
    public enum SizeBias
    {
        /// <summary>Prefere o formato mais próximo do quadrado (2048x4096 em vez de 1024x8192).</summary>
        PreferSquare,

        /// <summary>Aceita qualquer formato — apenas a menor área importa.</summary>
        AnyShape
    }

    public sealed class PackSettings
    {
        public const int DefaultPadding = 5;
        public const int DefaultMaxSize = 4096;
        public const int MaxPadding = 64;
        public const int MaxExtrude = 16;
        public const int MinMaxSize = 64;
        public const int MaxMaxSize = 16384;

        /// <summary>Espaço mínimo, em pixels, entre um sprite e outro dentro do atlas.</summary>
        public int Padding { get; set; } = DefaultPadding;

        /// <summary>Margem vazia, em pixels, entre os sprites e a borda do atlas.</summary>
        public int BorderPadding { get; set; }

        /// <summary>
        /// Quantos pixels de borda de cada sprite são replicados para fora dele.
        /// Evita o "bleeding" de pixels vizinhos quando a textura é filtrada ou tem mipmap.
        /// Não altera o retângulo declarado na planilha.
        /// </summary>
        public int Extrude { get; set; }

        /// <summary>Largura/altura máxima permitida para o atlas.</summary>
        public int MaxSize { get; set; } = DefaultMaxSize;

        /// <summary>Restringe as dimensões a potências de 2 (exigência da maioria das engines).</summary>
        public bool PowerOfTwo { get; set; } = true;

        /// <summary>Obriga o atlas a ser quadrado.</summary>
        public bool ForceSquare { get; set; }

        /// <summary>Critério de desempate entre atlas de mesma área.</summary>
        public SizeBias Bias { get; set; } = SizeBias.PreferSquare;

        /// <summary>
        /// Quando <see cref="PowerOfTwo"/> está desligado, encolhe o atlas final
        /// até o retângulo realmente ocupado (arredondado para múltiplo de 4).
        /// </summary>
        public bool ShrinkToFit { get; set; } = true;

        public PackSettings Clone() => (PackSettings)MemberwiseClone();

        /// <summary>Traz os valores para as faixas que a interface aceita.</summary>
        public PackSettings Clamped()
        {
            var c = Clone();
            c.Padding = Clamp(c.Padding, 0, MaxPadding);
            c.BorderPadding = Clamp(c.BorderPadding, 0, MaxPadding);
            c.Extrude = Clamp(c.Extrude, 0, MaxExtrude);
            c.MaxSize = Clamp(c.MaxSize, MinMaxSize, MaxMaxSize);
            return c;
        }

        private static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
    }
}

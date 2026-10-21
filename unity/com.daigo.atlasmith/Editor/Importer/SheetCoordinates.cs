#nullable enable

using System;
using Atlasmith.Core;
using UnityEngine;

namespace Atlasmith.Editor
{
    /// <summary>
    /// Conversões entre a planilha (origem no canto superior esquerdo, y para baixo) e o
    /// Unity (origem no canto inferior esquerdo, y para cima). O pivô normalizado segue a
    /// mesma regra: na planilha y = 0 é o topo do sprite; no Unity, a base.
    /// </summary>
    internal static class SheetCoordinates
    {
        public static Rect ToUnityRect(RectI rect, int sheetHeight) =>
            new Rect(rect.X, sheetHeight - rect.Y - rect.H, rect.W, rect.H);

        /// <summary>Null quando o retângulo do Unity não tem coordenadas inteiras (a planilha só guarda inteiros).</summary>
        public static RectI? FromUnityRect(Rect rect, int textureHeight)
        {
            if (!IsWhole(rect.x) || !IsWhole(rect.y) || !IsWhole(rect.width) || !IsWhole(rect.height)) return null;

            int x = Mathf.RoundToInt(rect.x), y = Mathf.RoundToInt(rect.y);
            int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
            return new RectI(x, textureHeight - y - h, w, h);
        }

        public static Vector2 ToUnityPivot(double pivotX, double pivotY) => new Vector2((float)pivotX, (float)(1.0 - pivotY));

        public static (double X, double Y) FromUnityPivot(Vector2 pivot) => (pivot.x, 1.0 - pivot.y);

        /// <summary>O Vector4 de borda do Unity é (esquerda, baixo, direita, cima).</summary>
        public static Vector4 ToUnityBorder(AtlasSheetSprite sprite) =>
            new Vector4(sprite.BorderLeft, sprite.BorderBottom, sprite.BorderRight, sprite.BorderTop);

        /// <summary>Null quando alguma borda não é inteira.</summary>
        public static (int Left, int Top, int Right, int Bottom)? FromUnityBorder(Vector4 border)
        {
            if (!IsWhole(border.x) || !IsWhole(border.y) || !IsWhole(border.z) || !IsWhole(border.w)) return null;
            return (Mathf.RoundToInt(border.x), Mathf.RoundToInt(border.w), Mathf.RoundToInt(border.z), Mathf.RoundToInt(border.y));
        }

        /// <summary>Alinhamento para um sprite novo: centro quando o pivô é o centro, senão personalizado.</summary>
        public static SpriteAlignment AlignmentFor(AtlasSheetSprite sprite) =>
            sprite.HasCustomPivot ? SpriteAlignment.Custom : SpriteAlignment.Center;

        private static bool IsWhole(float value) => Math.Abs(value - Mathf.Round(value)) < 1e-4f;
    }
}

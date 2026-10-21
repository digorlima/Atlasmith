using Atlasmith.Core;
using NUnit.Framework;
using UnityEngine;

namespace Atlasmith.Editor.Tests
{
    public sealed class SheetCoordinatesTests
    {
        [Test]
        public void RectIsFlippedToBottomLeftOrigin()
        {
            var rect = SheetCoordinates.ToUnityRect(new RectI(10, 20, 30, 40), 100);
            Assert.AreEqual(new Rect(10, 40, 30, 40), rect);
        }

        [Test]
        public void RectRoundTrips()
        {
            var original = new RectI(3, 0, 17, 9);
            var back = SheetCoordinates.FromUnityRect(SheetCoordinates.ToUnityRect(original, 64), 64);
            Assert.IsTrue(back.HasValue);
            Assert.AreEqual(original.X, back.Value.X);
            Assert.AreEqual(original.Y, back.Value.Y);
            Assert.AreEqual(original.W, back.Value.W);
            Assert.AreEqual(original.H, back.Value.H);
        }

        [Test]
        public void FractionalRectIsRejected()
        {
            Assert.IsNull(SheetCoordinates.FromUnityRect(new Rect(1.5f, 0, 10, 10), 64));
        }

        [Test]
        public void PivotIsFlipped()
        {
            var pivot = SheetCoordinates.ToUnityPivot(0.25, 0.1);
            Assert.AreEqual(0.25f, pivot.x, 1e-6f);
            Assert.AreEqual(0.9f, pivot.y, 1e-6f);

            var back = SheetCoordinates.FromUnityPivot(pivot);
            Assert.AreEqual(0.25, back.X, 1e-6);
            Assert.AreEqual(0.1, back.Y, 1e-6);
        }

        [Test]
        public void BorderIsReorderedToLeftBottomRightTop()
        {
            var sprite = new AtlasSheetSprite("a-b", new RectI(0, 0, 10, 10)) { BorderLeft = 1, BorderTop = 2, BorderRight = 3, BorderBottom = 4 };
            Assert.AreEqual(new Vector4(1, 4, 3, 2), SheetCoordinates.ToUnityBorder(sprite));

            var back = SheetCoordinates.FromUnityBorder(new Vector4(1, 4, 3, 2));
            Assert.IsTrue(back.HasValue);
            Assert.AreEqual((1, 2, 3, 4), back.Value);
        }

        [Test]
        public void AlignmentIsCenterUnlessPivotIsCustom()
        {
            var center = new AtlasSheetSprite("a-b", new RectI(0, 0, 4, 4));
            var custom = new AtlasSheetSprite("a-c", new RectI(0, 0, 4, 4)) { PivotY = 1 };
            Assert.AreEqual(SpriteAlignment.Center, SheetCoordinates.AlignmentFor(center));
            Assert.AreEqual(SpriteAlignment.Custom, SheetCoordinates.AlignmentFor(custom));
        }
    }
}

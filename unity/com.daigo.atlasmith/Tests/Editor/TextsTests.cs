using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Atlasmith.Core;
using NUnit.Framework;

namespace Atlasmith.Editor.Tests
{
    public sealed class TextsTests
    {
        private static IEnumerable<TextLanguage> Languages => new[] { TextLanguage.Portuguese, TextLanguage.English };

        [Test]
        public void EveryTextPropertyIsFilledInBothLanguages([ValueSource(nameof(Languages))] TextLanguage language)
        {
            var text = Texts.For(language);
            Assert.AreEqual(language, text.Language);

            var properties = typeof(Texts).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(string));
            foreach (var property in properties)
            {
                string value = (string)property.GetValue(text);
                Assert.IsFalse(string.IsNullOrWhiteSpace(value), $"{language}: {property.Name} está vazio");
            }
        }

        [Test]
        public void LanguagesDiffer()
        {
            Assert.AreNotEqual(Texts.For(TextLanguage.Portuguese).Generate, Texts.For(TextLanguage.English).Generate);
            Assert.AreEqual("Gerar atlas", Texts.For(TextLanguage.Portuguese).Generate);
            Assert.AreEqual("Generate atlas", Texts.For(TextLanguage.English).Generate);
            Assert.AreEqual("Ignorar camadas desativadas", Texts.For(TextLanguage.Portuguese).IgnoreHidden);
            Assert.AreEqual("Ignore hidden layers", Texts.For(TextLanguage.English).IgnoreHidden);
        }

        [Test]
        public void RemovalMessageListsSpritesHiddenLayersAndReferences([ValueSource(nameof(Languages))] TextLanguage language)
        {
            var text = Texts.For(language);
            var check = new SpriteRemovalCheck(new List<RemovedSprite>
            {
                new RemovedSprite("ingame-entity-shield", true, 348250710, new List<string> { "Assets/UI/component-shield-gauge.prefab" }),
                new RemovedSprite("ingame-art-day", true, 1, new List<string>()),
                new RemovedSprite("ingame-old", false, 2, new List<string>())
            }, hiddenLayersIgnored: true, referencesScanned: true);

            string message = text.RemovalMessage("ingame-ui-atlas", check);
            StringAssert.Contains("ingame-entity-shield", message);
            StringAssert.Contains("component-shield-gauge.prefab", message);
            StringAssert.Contains("ingame-old", message);
            StringAssert.Contains(text.RemovalIncludeHidden, message);
            Assert.AreEqual(2, check.HiddenCount);
            Assert.AreEqual(1, check.ReferencedCount);
            Assert.IsTrue(check.CanIncludeHidden);

            string skipped = text.RemovalSkipped("Assets/UI/ingame-ui-atlas.png", check);
            StringAssert.Contains("allowRemovingSprites", skipped);
        }

        [Test]
        public void LongListsAreShortened()
        {
            var text = Texts.For(TextLanguage.English);
            var names = Enumerable.Range(0, 40).Select(i => "sprite-" + i).ToList();
            string warning = text.WarnRemovedSprites(names);
            StringAssert.Contains("and 25 more", warning);
            StringAssert.DoesNotContain("sprite-39", warning);
        }
    }
}

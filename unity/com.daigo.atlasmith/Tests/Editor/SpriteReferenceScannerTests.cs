using System;
using System.IO;
using NUnit.Framework;

namespace Atlasmith.Editor.Tests
{
    public sealed class SpriteReferenceScannerTests
    {
        private const string Guid = "0123456789abcdef0123456789abcdef";
        private const string OtherGuid = "fedcba9876543210fedcba9876543210";
        private string _root = "";

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "AtlasmithScan-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "Assets", "UI"));
            Directory.CreateDirectory(Path.Combine(_root, "Packages", "com.example.embedded"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private void Write(string relative, string content) => File.WriteAllText(Path.Combine(_root, relative), content);

        [Test]
        public void FindsYamlAndUxmlReferences()
        {
            Write("Assets/UI/a.prefab", $"  m_Sprite: {{fileID: 12345, guid: {Guid}, type: 3}}\n  m_Other: {{fileID: 999, guid: {OtherGuid}, type: 3}}\n");
            Write("Assets/UI/b.uxml", $"<ui:VisualElement style=\"background-image: url(&apos;project://database/Assets/x.png?fileID=-777&amp;guid={Guid}&amp;type=3#x&apos;);\" />");
            Write("Assets/UI/c.asset", $"  icon: {{fileID: 999, guid: {OtherGuid}, type: 3}}\n");
            Write("Assets/UI/d.txt", $"fileID: 12345, guid: {Guid}");
            Write("Packages/com.example.embedded/e.prefab", $"  m_Sprite: {{fileID: 12345, guid: {Guid}, type: 3}}\n");

            var found = SpriteReferenceScanner.Scan(_root, Guid, new long[] { 12345, -777, 999 });

            CollectionAssert.AreEquivalent(new[] { 12345L, -777L }, found.Keys);
            CollectionAssert.AreEqual(new[] { "Assets/UI/a.prefab", "Packages/com.example.embedded/e.prefab" }, found[12345]);
            CollectionAssert.AreEqual(new[] { "Assets/UI/b.uxml" }, found[-777]);
        }

        [Test]
        public void NothingToLookForReturnsEmpty()
        {
            Write("Assets/UI/a.prefab", $"  m_Sprite: {{fileID: 12345, guid: {Guid}, type: 3}}\n");
            Assert.IsEmpty(SpriteReferenceScanner.Scan(_root, Guid, Array.Empty<long>()));
            Assert.IsEmpty(SpriteReferenceScanner.Scan(_root, "", new long[] { 12345 }));
        }
    }
}

#nullable enable

using System.Collections.Generic;
using System.Linq;
using Atlasmith.Core;
using UnityEditor;

namespace Atlasmith.Editor
{
    /// <summary>
    /// Os itens de menu. Os rótulos ficam em inglês, como os do próprio Unity: o texto de
    /// um [MenuItem] é constante de compilação e o Unity não tem API pública para registrar
    /// um item com outro texto em tempo de execução (Menu.AddMenuItem é interno, no 2022.3
    /// e no Unity 6). As janelas, essas sim, seguem o idioma escolhido.
    /// </summary>
    public static class CreateAtlasMenu
    {
        public const string CreateAtlasItem = "Assets/Create Atlas";

        // Num grupo próprio, longe dos itens do Unity que vêm logo no começo do menu.
        public const int CreateAtlasPriority = 1000;

        [MenuItem(CreateAtlasItem, false, CreateAtlasPriority)]
        private static void CreateAtlas()
        {
            var selected = SelectedKritaFiles();
            if (selected.Count > 0) CreateAtlasWindow.Open(selected, generateNow: true);
        }

        [MenuItem(CreateAtlasItem, true)]
        private static bool CreateAtlasValidate() => SelectedKritaFiles().Count > 0;

        /// <summary>Os .kra/.krz selecionados no Project (o autosave do Krita, ".nome-autosave.kra", fica de fora).</summary>
        public static List<string> SelectedKritaFiles() =>
            Selection.assetGUIDs
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) && KraReader.IsKritaFile(p) && !KraReader.IsAutosave(p))
                .Distinct()
                .ToList();
    }
}

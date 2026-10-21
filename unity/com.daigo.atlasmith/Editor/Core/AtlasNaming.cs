#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Atlasmith.Core
{
    public static class AtlasNaming
    {
        /// <summary>
        /// Nome para um atlas novo: a planilha (.atlasmith.json ou .tpsheet) que já
        /// estiver sozinha no destino ou, se não houver, o nome do arquivo do Krita
        /// ("menu-ui-project.kra" vira "menu-ui-atlas").
        /// </summary>
        public static string Suggest(string sourcePath, string outputFolder)
        {
            var existing = ExistingAtlasNames(outputFolder);
            if (existing.Count == 1) return existing[0];

            string stem = Path.GetFileNameWithoutExtension(sourcePath.TrimEnd('/', '\\'));
            if (stem.EndsWith("-project", StringComparison.OrdinalIgnoreCase))
                stem = stem.Substring(0, stem.Length - "-project".Length);
            return stem + "-atlas";
        }

        /// <summary>Nomes de atlas com planilha na pasta, sem repetir quem tem os dois formatos.</summary>
        public static List<string> ExistingAtlasNames(string folder)
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!Directory.Exists(folder)) return new List<string>();

                foreach (string file in Directory.EnumerateFiles(folder))
                {
                    if (AtlasSheet.IsSheetFile(file)) names.Add(AtlasSheet.AtlasNameOf(file));
                    else if (TpSheet.IsSheetFile(file)) names.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Sem acesso ao destino, o nome sai do arquivo de origem.
            }
            return names.ToList();
        }

        /// <summary>A planilha de um atlas já gravado, no formato que existir (o .atlasmith.json tem preferência).</summary>
        public static AtlasSheet? TryReadSheet(string folder, string atlasName)
        {
            return AtlasSheet.TryRead(AtlasSheet.PathFor(folder, atlasName))
                ?? TpSheet.TryRead(TpSheet.PathFor(folder, atlasName));
        }

        /// <summary>Qual formato um atlas já gravado usa, ou null se ele não tem planilha.</summary>
        public static SheetFormat? ExistingFormat(string folder, string atlasName)
        {
            bool json = File.Exists(AtlasSheet.PathFor(folder, atlasName));
            bool tp = File.Exists(TpSheet.PathFor(folder, atlasName));
            if (json && tp) return SheetFormat.Both;
            if (json) return SheetFormat.Atlasmith;
            if (tp) return SheetFormat.TpSheet;
            return null;
        }

        /// <summary>Caracteres que não podem ir no nome do atlas (o nome vira nome de arquivo).</summary>
        public static char? InvalidCharIn(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                if (name.IndexOf(c) >= 0) return c;
            return null;
        }
    }
}

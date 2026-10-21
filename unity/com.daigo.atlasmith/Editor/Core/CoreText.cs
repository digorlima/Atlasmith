#nullable enable

using System.Globalization;

namespace Atlasmith.Core
{
    public enum TextLanguage
    {
        Portuguese,
        English
    }

    /// <summary>
    /// Mensagens do núcleo (leitor de .kra, empacotador, planilhas), em português e
    /// inglês. Quem hospeda o núcleo — o app ou o Unity — escolhe o idioma com Use();
    /// o padrão segue o idioma do sistema.
    /// </summary>
    public abstract class CoreText
    {
        private static CoreText _current = For(SystemLanguage());

        public static CoreText Current => _current;

        public static void Use(TextLanguage language) => _current = For(language);

        public static CoreText For(TextLanguage language) =>
            language == TextLanguage.Portuguese ? (CoreText)new Portuguese() : new English();

        /// <summary>Português para qualquer variante (pt-BR, pt-PT); inglês para o resto.</summary>
        public static TextLanguage SystemLanguage() =>
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt"
                ? TextLanguage.Portuguese
                : TextLanguage.English;

        public abstract TextLanguage Language { get; }
        public abstract CultureInfo Culture { get; }

        // --- empacotador ---
        public abstract string NoAtlasFits(int maxSize, int spriteW, int spriteH);
        public abstract string CandidateRejected(int w, int h);
        public abstract string CandidateAccepted(int w, int h, string strategy);
        public abstract string NothingFits(int maxSize, int count);

        // --- seleção das camadas do .kra ---
        public abstract string SkipOutOfPattern { get; }
        public abstract string SkipHidden { get; }
        public abstract string SkipInsideHiddenGroup(string group);
        public abstract string SkipUnsupportedType(string type);
        public abstract string SkipDuplicateName(string firstPath);

        public abstract string NoteSkipped(string path, string reason);
        public abstract string NoteEmptyLayer(string path);
        public abstract string NoteWarning(string warning);
        public abstract string NoteFailure(string failure);
        public abstract string NoteColorProfile(string profile);

        public abstract string LayerType(string nodeType);

        // --- leitura do .kra ---
        public abstract string NotKritaArchive { get; }
        public abstract string MissingMainDoc { get; }
        public abstract string MissingImageElement { get; }
        public abstract string LayerDataMissing(string file);
        public abstract string LayerDataCorrupt(string file);
        public abstract string UnsupportedColorSpace(string colorSpace);
        public abstract string UnsupportedLayerVersion(int version);
        public abstract string PixelSizeMismatch(string file, int pixelSize, string colorSpace, int expected);
        public abstract string TypeNotSupported(string type);

        public abstract string WarnAnimated(string path);
        public abstract string WarnLayerStyle(string path);
        public abstract string WarnLayerStyleInGroup(string path);
        public abstract string WarnUnsupportedInGroup(string path, string type);
        public abstract string WarnEmptyMask(string mask, string path);
        public abstract string WarnUnsupportedMask(string type, string mask, string path);
        public abstract string WarnBlendMode(string mode, string path);

        // --- XML e JSON ---
        public abstract string XmlUnclosed(string element);
        public abstract string XmlUnexpectedClose(string element, int position);
        public abstract string XmlMalformed(int position);
        public abstract string JsonMalformed(int position);
        public abstract string NotAnAtlasFile { get; }
        public abstract string NewerAtlasFormat(int version);

        // --- geração ---
        public abstract string ReadingLayers(string path);
        public abstract string LayersRead(int count);
        public abstract string NoSpriteCouldBeRead { get; }
        public abstract string NoLayerBecameSprite { get; }
        public abstract string NoLayerBecameSpriteDetail { get; }
        public abstract string DoesNotFit(int maxSize);
        public abstract string PackSummary(int w, int h, string strategy, int tried);
        public abstract string Replacing(string file);
        public abstract string SavedTo(string path);

        private sealed class Portuguese : CoreText
        {
            public override TextLanguage Language => TextLanguage.Portuguese;
            public override CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("pt-BR");

            public override string NoAtlasFits(int maxSize, int spriteW, int spriteH) =>
                $"Nenhum atlas de até {maxSize}px comporta os sprites (maior sprite: {spriteW}x{spriteH}).";
            public override string CandidateRejected(int w, int h) => $"  {w}x{h} — não coube.";
            public override string CandidateAccepted(int w, int h, string strategy) => $"  {w}x{h} — OK ({strategy}).";
            public override string NothingFits(int maxSize, int count) =>
                $"Nenhuma combinação até {maxSize}px comportou os {count} sprites.";

            public override string SkipOutOfPattern => "nome fora do padrão nome-nome";
            public override string SkipHidden => "camada desativada";
            public override string SkipInsideHiddenGroup(string group) => $"dentro do grupo desativado {group}";
            public override string SkipUnsupportedType(string type) => $"{type} não é suportada — converta para camada de pintura";
            public override string SkipDuplicateName(string firstPath) => $"nome repetido, já usado por {firstPath}";

            public override string NoteSkipped(string path, string reason) => $"ignorada — {path} ({reason})";
            public override string NoteEmptyLayer(string path) => $"ignorada — {path} (camada vazia)";
            public override string NoteWarning(string warning) => $"aviso — {warning}";
            public override string NoteFailure(string failure) => $"ignorada — {failure}";
            public override string NoteColorProfile(string profile) =>
                $"aviso — o documento usa o perfil {profile}; as cores são lidas sem conversão de perfil";

            public override string LayerType(string nodeType)
            {
                switch (nodeType)
                {
                    case "paintlayer": return "camada de pintura";
                    case "grouplayer": return "grupo";
                    case "shapelayer": return "camada vetorial";
                    case "adjustmentlayer": return "camada de filtro";
                    case "generatorlayer": return "camada de preenchimento";
                    case "clonelayer": return "camada clone";
                    case "filelayer": return "camada de arquivo";
                    case "transparencymask": return "máscara de transparência";
                    case "filtermask": return "máscara de filtro";
                    case "transformmask": return "máscara de transformação";
                    case "colorizemask": return "máscara de colorização";
                    case "selectionmask": return "máscara de seleção";
                    default: return $"camada do tipo \"{nodeType}\"";
                }
            }

            public override string NotKritaArchive => "o arquivo não é um .kra/.krz válido (se o Krita estava salvando, tente de novo).";
            public override string MissingMainDoc => "o arquivo não parece ser do Krita (falta o maindoc.xml).";
            public override string MissingImageElement => "maindoc.xml sem o elemento IMAGE.";
            public override string LayerDataMissing(string file) => $"os dados da camada ({file}) não estão no arquivo";
            public override string LayerDataCorrupt(string file) => $"os dados da camada ({file}) estão corrompidos";
            public override string UnsupportedColorSpace(string colorSpace) =>
                $"espaço de cor {colorSpace} não suportado — converta a imagem para RGB/Alfa 8 bits (Imagem → Converter espaço de cor)";
            public override string UnsupportedLayerVersion(int version) =>
                $"formato de camada versão {version} — abra e salve o arquivo num Krita recente";
            public override string PixelSizeMismatch(string file, int pixelSize, string colorSpace, int expected) =>
                $"{file} diz {pixelSize} bytes por pixel, mas {colorSpace} usa {expected}";
            public override string TypeNotSupported(string type) => $"{type} não é suportada";

            public override string WarnAnimated(string path) => $"{path} tem animação; só o quadro guardado na camada é lido";
            public override string WarnLayerStyle(string path) => $"o estilo de camada (efeitos) de {path} não entra no sprite";
            public override string WarnLayerStyleInGroup(string path) => $"o estilo de camada (efeitos) de {path} foi ignorado";
            public override string WarnUnsupportedInGroup(string path, string type) => $"{path}: {type} ignorada dentro do grupo (não suportada)";
            public override string WarnEmptyMask(string mask, string path) => $"a máscara \"{mask}\" de {path} está vazia e foi ignorada";
            public override string WarnUnsupportedMask(string type, string mask, string path) => $"{type} \"{mask}\" de {path} foi ignorada (não suportada)";
            public override string WarnBlendMode(string mode, string path) => $"o modo de mesclagem \"{mode}\" de {path} foi tratado como Normal";

            public override string XmlUnclosed(string element) => $"XML incompleto: <{element}> não foi fechado.";
            public override string XmlUnexpectedClose(string element, int position) => $"XML malformado: </{element}> inesperado na posição {position}.";
            public override string XmlMalformed(int position) => $"XML malformado perto da posição {position}.";
            public override string JsonMalformed(int position) => $"JSON malformado perto da posição {position}.";
            public override string NotAnAtlasFile => "o arquivo não é uma planilha do Atlasmith.";
            public override string NewerAtlasFormat(int version) => $"a planilha usa o formato {version}, mais novo que este Atlasmith; atualize-o.";

            public override string ReadingLayers(string path) => $"Lendo camadas de {path}";
            public override string LayersRead(int count) => count == 1 ? "1 camada lida." : $"{count} camadas lidas.";
            public override string NoSpriteCouldBeRead => "Nenhum sprite pôde ser lido. Os motivos estão no log.";
            public override string NoLayerBecameSprite => "Nenhuma camada virou sprite.";
            public override string NoLayerBecameSpriteDetail =>
                "Nenhuma camada desse arquivo virou sprite.\n\n" +
                "Entram as camadas com nome no padrão nome-nome (por exemplo, menu-botao-play) " +
                "e com algo desenhado. Renomeie no Krita e salve o arquivo.";
            public override string DoesNotFit(int maxSize) =>
                $"Não foi possível encaixar tudo em um atlas de até {maxSize}px.\n\nAumente o tamanho máximo ou reduza a margem.";
            public override string PackSummary(int w, int h, string strategy, int tried) =>
                $"Atlas {w}x{h} — {strategy} — {tried} {(tried == 1 ? "formato testado" : "formatos testados")}.";
            public override string Replacing(string file) => $"Substituindo {file}.";
            public override string SavedTo(string path) => $"Salvo em {path}";
        }

        private sealed class English : CoreText
        {
            public override TextLanguage Language => TextLanguage.English;
            public override CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("en-US");

            public override string NoAtlasFits(int maxSize, int spriteW, int spriteH) =>
                $"No atlas up to {maxSize}px can hold the sprites (largest sprite: {spriteW}x{spriteH}).";
            public override string CandidateRejected(int w, int h) => $"  {w}x{h} — didn't fit.";
            public override string CandidateAccepted(int w, int h, string strategy) => $"  {w}x{h} — OK ({strategy}).";
            public override string NothingFits(int maxSize, int count) =>
                $"No layout up to {maxSize}px fits all {count} sprites.";

            public override string SkipOutOfPattern => "name doesn't follow the name-name pattern";
            public override string SkipHidden => "hidden layer";
            public override string SkipInsideHiddenGroup(string group) => $"inside the hidden group {group}";
            public override string SkipUnsupportedType(string type) => $"{type} isn't supported — convert it to a paint layer";
            public override string SkipDuplicateName(string firstPath) => $"duplicate name, already used by {firstPath}";

            public override string NoteSkipped(string path, string reason) => $"skipped — {path} ({reason})";
            public override string NoteEmptyLayer(string path) => $"skipped — {path} (empty layer)";
            public override string NoteWarning(string warning) => $"warning — {warning}";
            public override string NoteFailure(string failure) => $"skipped — {failure}";
            public override string NoteColorProfile(string profile) =>
                $"warning — the document uses the {profile} profile; colors are read without profile conversion";

            public override string LayerType(string nodeType)
            {
                switch (nodeType)
                {
                    case "paintlayer": return "paint layer";
                    case "grouplayer": return "group";
                    case "shapelayer": return "vector layer";
                    case "adjustmentlayer": return "filter layer";
                    case "generatorlayer": return "fill layer";
                    case "clonelayer": return "clone layer";
                    case "filelayer": return "file layer";
                    case "transparencymask": return "transparency mask";
                    case "filtermask": return "filter mask";
                    case "transformmask": return "transform mask";
                    case "colorizemask": return "colorize mask";
                    case "selectionmask": return "selection mask";
                    default: return $"layer of type \"{nodeType}\"";
                }
            }

            public override string NotKritaArchive => "the file isn't a valid .kra/.krz (if Krita was saving it, try again).";
            public override string MissingMainDoc => "the file doesn't look like a Krita file (maindoc.xml is missing).";
            public override string MissingImageElement => "maindoc.xml has no IMAGE element.";
            public override string LayerDataMissing(string file) => $"the layer data ({file}) is missing from the file";
            public override string LayerDataCorrupt(string file) => $"the layer data ({file}) is corrupted";
            public override string UnsupportedColorSpace(string colorSpace) =>
                $"color space {colorSpace} isn't supported — convert the image to RGB/Alpha 8-bit (Image → Convert Image Color Space)";
            public override string UnsupportedLayerVersion(int version) =>
                $"layer format version {version} — open and save the file in a recent Krita";
            public override string PixelSizeMismatch(string file, int pixelSize, string colorSpace, int expected) =>
                $"{file} says {pixelSize} bytes per pixel, but {colorSpace} uses {expected}";
            public override string TypeNotSupported(string type) => $"{type} isn't supported";

            public override string WarnAnimated(string path) => $"{path} is animated; only the frame stored in the layer is read";
            public override string WarnLayerStyle(string path) => $"the layer style (effects) of {path} isn't included in the sprite";
            public override string WarnLayerStyleInGroup(string path) => $"the layer style (effects) of {path} was ignored";
            public override string WarnUnsupportedInGroup(string path, string type) => $"{path}: {type} skipped inside the group (not supported)";
            public override string WarnEmptyMask(string mask, string path) => $"the mask \"{mask}\" of {path} is empty and was ignored";
            public override string WarnUnsupportedMask(string type, string mask, string path) => $"{type} \"{mask}\" of {path} was ignored (not supported)";
            public override string WarnBlendMode(string mode, string path) => $"the blending mode \"{mode}\" of {path} was treated as Normal";

            public override string XmlUnclosed(string element) => $"Incomplete XML: <{element}> was never closed.";
            public override string XmlUnexpectedClose(string element, int position) => $"Malformed XML: unexpected </{element}> at position {position}.";
            public override string XmlMalformed(int position) => $"Malformed XML near position {position}.";
            public override string JsonMalformed(int position) => $"Malformed JSON near position {position}.";
            public override string NotAnAtlasFile => "the file isn't an Atlasmith sheet.";
            public override string NewerAtlasFormat(int version) => $"the sheet uses format {version}, newer than this Atlasmith; please update it.";

            public override string ReadingLayers(string path) => $"Reading layers from {path}";
            public override string LayersRead(int count) => count == 1 ? "1 layer read." : $"{count} layers read.";
            public override string NoSpriteCouldBeRead => "No sprite could be read. The reasons are in the log.";
            public override string NoLayerBecameSprite => "No layer became a sprite.";
            public override string NoLayerBecameSpriteDetail =>
                "No layer in this file became a sprite.\n\n" +
                "Layers are included when their name follows the name-name pattern (for example, menu-button-play) " +
                "and they have something drawn. Rename them in Krita and save the file.";
            public override string DoesNotFit(int maxSize) =>
                $"Couldn't fit everything in an atlas of up to {maxSize}px.\n\nIncrease the maximum size or reduce the padding.";
            public override string PackSummary(int w, int h, string strategy, int tried) =>
                $"Atlas {w}x{h} — {strategy} — {tried} {(tried == 1 ? "size tried" : "sizes tried")}.";
            public override string Replacing(string file) => $"Replacing {file}.";
            public override string SavedTo(string path) => $"Saved to {path}";
        }
    }
}

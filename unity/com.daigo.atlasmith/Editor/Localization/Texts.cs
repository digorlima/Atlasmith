#nullable enable

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Atlasmith.Core;

namespace Atlasmith.Editor
{
    /// <summary>
    /// Todos os textos da interface do pacote, em português e inglês. Os rótulos dos
    /// menus ficam de fora: são constantes em inglês (veja CreateAtlasMenu).
    /// </summary>
    public abstract class Texts
    {
        private static Texts _current = new English();

        public static Texts Current => _current;

        public static void Use(TextLanguage language)
        {
            if (_current.Language == language) return;
            _current = For(language);
        }

        public static Texts For(TextLanguage language) =>
            language == TextLanguage.Portuguese ? (Texts)new Portuguese() : new English();

        public abstract TextLanguage Language { get; }
        public abstract CultureInfo Culture { get; }

        /// <summary>Até 15 nomes, separados por vírgula; o resto vira "e mais N".</summary>
        protected string Names(IReadOnlyCollection<string> names)
        {
            const int Max = 15;
            string shown = string.Join(", ", names.Take(Max));
            return names.Count > Max ? shown + " " + AndMore(names.Count - Max) : shown;
        }

        protected abstract string AndMore(int count);

        // --- comuns ---
        public abstract string Cancel { get; }
        public abstract string Copy { get; }
        public abstract string Clear { get; }
        public abstract string Refresh { get; }
        public abstract string Replace { get; }
        public abstract string Show { get; }
        public abstract string LanguageLabel { get; }
        public abstract string LanguageHint { get; }
        public abstract string LanguageAuto { get; }

        // --- janela Create Atlas ---
        public abstract string CreateAtlasTitle { get; }
        public abstract string NoKritaSelected { get; }
        public abstract string SourceLabel { get; }
        public abstract string OutputLabel { get; }
        public abstract string Change { get; }
        public abstract string ChangeOutputTitle { get; }
        public abstract string ChangeOutputMessage { get; }
        public abstract string KritaFileMissing(string path);
        public abstract string TargetUsesTexturePacker { get; }
        public abstract string SettingsFromSheet(string path);
        public abstract string SettingsFromExistingAtlas(string path);
        public abstract string SettingsDefault { get; }

        public abstract string SettingsHeader { get; }
        public abstract string Padding { get; }
        public abstract string PaddingHint { get; }
        public abstract string BorderPadding { get; }
        public abstract string BorderPaddingHint { get; }
        public abstract string Extrude { get; }
        public abstract string ExtrudeHint { get; }
        public abstract string MaxSize { get; }
        public abstract string MaxSizeHint { get; }
        public abstract string PowerOfTwo { get; }
        public abstract string PowerOfTwoHint { get; }
        public abstract string Square { get; }
        public abstract string SquareHint { get; }
        public abstract string IgnoreHidden { get; }
        public abstract string IgnoreHiddenHint { get; }
        public abstract string ResetDefaults { get; }
        public abstract string Generate { get; }
        public abstract string GenerateHint { get; }
        public abstract string SettingsChangedHint { get; }

        public abstract string MetricSize { get; }
        public abstract string MetricOccupancy { get; }
        public abstract string MetricSprites { get; }
        public abstract string MetricTime { get; }
        public abstract string PreviewHeader { get; }
        public abstract string Outlines { get; }
        public abstract string OutlinesHint { get; }
        public abstract string LogHeader { get; }

        public abstract string ConvertFirstTitle { get; }
        public abstract string ConvertFirstMessage(string tpSheetFile);
        public abstract string ConvertFirstOk { get; }
        public abstract string ConvertFirstDeclined { get; }
        public abstract string ConfirmOtherSource(string atlas, string generatedFrom, string now);

        // --- geração ---
        public abstract string StageReading(int done, int total);
        public abstract string StagePacking { get; }
        public abstract string StageComposing { get; }
        public abstract string StageSaving { get; }
        public abstract string NotAKritaFile(string path);
        public abstract string InvalidAtlasName(string name);
        public abstract string TargetIsTexturePacker(string tpSheetPath);
        public abstract string ConversionBeforeGenerateFailed(string tpSheetPath);
        public abstract string LogGenerating(string kritaPath, string pngPath);
        public abstract string LogError(string message);
        public abstract string LogWarning(string message);
        public abstract string LogSummary(int sprites, int kept, int added, int removed);
        public abstract string GeneratedOk(string pngPath);
        public abstract string GenerationCancelled { get; }
        public abstract string GenerationFailedUnknown { get; }
        public abstract string WarnRemovedSprites(IReadOnlyCollection<string> names);
        public abstract string WarnChangedIds(IReadOnlyCollection<string> names);
        public abstract string WarnImportCount(int imported, int expected);
        public abstract string WarnMaxTextureSize(int maxTextureSize, int atlasSide);

        // --- sprites que sairiam do atlas ---
        public abstract string StageCheckingRemoval { get; }
        public abstract string RemovalTitle { get; }
        public abstract string RemovalGenerateAnyway { get; }
        public abstract string RemovalIncludeHidden { get; }
        public abstract string RemovalStopped(int count);
        public abstract string RemovalAccepted(int count);
        public abstract string RemovalIncludingHidden { get; }
        public abstract string RemovalCancelled { get; }
        protected abstract string HiddenLayerTag { get; }
        protected abstract string UsedIn(string files);
        protected abstract string RemovalIntro(string atlas, int count);
        protected abstract string RemovalReferenced(int referenced);
        protected abstract string RemovalNoneReferenced { get; }
        protected abstract string RemovalHiddenHint(int hidden);
        protected abstract string RemovalSameIdHint { get; }
        protected abstract string RemovalSkippedIntro(string pngPath, int count);
        protected abstract string RemovalSkippedHow { get; }

        /// <summary>"nome (camada oculta) — usado em a.prefab, b.unity e mais 2".</summary>
        public string RemovalLine(RemovedSprite sprite)
        {
            string line = sprite.Name;
            if (sprite.IsHiddenLayer) line += " " + HiddenLayerTag;
            if (sprite.ReferencedBy.Count > 0)
            {
                const int Max = 3;
                string files = string.Join(", ", sprite.ReferencedBy.Take(Max).Select(System.IO.Path.GetFileName));
                if (sprite.ReferencedBy.Count > Max) files += " " + AndMore(sprite.ReferencedBy.Count - Max);
                line += " — " + UsedIn(files);
            }
            return line;
        }

        /// <summary>O texto do diálogo que pede confirmação antes de tirar sprites do atlas.</summary>
        public string RemovalMessage(string atlas, SpriteRemovalCheck check)
        {
            const int Max = 12;
            var parts = new List<string> { RemovalIntro(atlas, check.Sprites.Count) };
            parts.Add(string.Join("\n", check.Sprites.Take(Max).Select(s => "• " + RemovalLine(s))) +
                      (check.Sprites.Count > Max ? "\n• … " + AndMore(check.Sprites.Count - Max) : ""));

            var notes = new List<string>();
            if (check.ReferencedCount > 0) notes.Add(RemovalReferenced(check.ReferencedCount));
            else if (check.ReferencesScanned) notes.Add(RemovalNoneReferenced);
            if (check.CanIncludeHidden) notes.Add(RemovalHiddenHint(check.HiddenCount));
            notes.Add(RemovalSameIdHint);
            parts.Add(string.Join("\n\n", notes));
            return string.Join("\n\n", parts);
        }

        /// <summary>O aviso do modo batch, quando a geração para em vez de tirar sprites.</summary>
        public string RemovalSkipped(string pngPath, SpriteRemovalCheck check) =>
            "Atlasmith: " + RemovalSkippedIntro(pngPath, check.Sprites.Count) + "\n" +
            string.Join("\n", check.Sprites.Select(s => "  " + RemovalLine(s))) + "\n" + RemovalSkippedHow;

        // --- importer ---
        public abstract string ImporterTpSheetConflict(string texturePath, string tpSheetPath);
        public abstract string ImporterInvalidSheet(string sheetPath, string error);
        public abstract string ImporterTextureMismatch(string sheetPath, string sheetTexture, string textureFile);
        public abstract string ImporterSizeMismatch(string texturePath, int width, int height, int sheetWidth, int sheetHeight);
        public abstract string ImporterSpritesRemoved(string texturePath, IReadOnlyCollection<string> names);
        public abstract string ImporterDuplicateNames(string sheetPath, IReadOnlyCollection<string> names);
        public abstract string ImporterOutOfBounds(string sheetPath, IReadOnlyCollection<string> names);

        // --- conversão do TexturePacker ---
        public abstract string ConvertTitle { get; }
        public abstract string ConvertIntro { get; }
        public abstract string ConvertNothingFound { get; }
        public abstract string SelectAll { get; }
        public abstract string SelectNone { get; }
        public abstract string ConvertButton { get; }
        public abstract string ConvertSelectedButton(int count);
        public abstract string ConvertConfirm(int count);
        public abstract string SpriteCount(int count);
        public abstract string ResultsHeader { get; }
        public abstract string ConvertSummary(int converted, int total);
        public abstract string MoreDifferences(int count);
        public abstract string OpenBackupFolder { get; }
        public abstract string ConvertResultLine(TpConversionResult result);

        public abstract string ConvertProblemNoTexture(string texturePath);
        public abstract string ConvertProblemNotSprites { get; }
        public abstract string ConvertProblemAlreadyHasSheet(string sheetFile);
        public abstract string ConvertProblemNoSize(string texturePath);
        public abstract string ConvertProblemDuplicate(string name);
        public abstract string ConvertProblemFractional(string name);
        public abstract string ConvertBackupFailed(string error);
        public abstract string ConvertBackedUp(string folder);
        public abstract string ConvertDeleteFailed(string path);
        public abstract string ConvertTpSheetStillThere { get; }
        public abstract string ConvertStillListed { get; }
        public abstract string ConvertSettingsCleaned(string file);
        public abstract string ConvertDone(string sheetFile, int sprites);
        public abstract string ConvertMismatch(int differences);
        public abstract string ConvertRolledBack { get; }
        public abstract string ConvertRollbackIncomplete(string backupFolder);

        private sealed class Portuguese : Texts
        {
            public override TextLanguage Language => TextLanguage.Portuguese;
            public override CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("pt-BR");
            protected override string AndMore(int count) => $"e mais {count}";

            public override string Cancel => "Cancelar";
            public override string Copy => "Copiar";
            public override string Clear => "Limpar";
            public override string Refresh => "Atualizar";
            public override string Replace => "Substituir";
            public override string Show => "Mostrar";
            public override string LanguageLabel => "Idioma";
            public override string LanguageHint => "Idioma das janelas e mensagens do Atlasmith (os menus do Unity ficam em inglês)";
            public override string LanguageAuto => "Automático";

            public override string CreateAtlasTitle => "Criar atlas";
            public override string NoKritaSelected => "Clique com o botão direito em um arquivo .kra ou .krz na janela Project e escolha Create Atlas.";
            public override string SourceLabel => "Origem";
            public override string OutputLabel => "Atlas";
            public override string Change => "Alterar…";
            public override string ChangeOutputTitle => "Salvar o atlas como";
            public override string ChangeOutputMessage => "Escolha onde gravar o PNG do atlas (a planilha .atlasmith.json vai junto).";
            public override string KritaFileMissing(string path) => $"O arquivo {path} não existe mais.";
            public override string TargetUsesTexturePacker =>
                "Este atlas ainda usa o TexturePacker (.tpsheet). Ao gerar, ele é convertido antes para o Atlasmith — as referências dos sprites são mantidas.";
            public override string SettingsFromSheet(string path) => $"Configurações guardadas em {path}.";
            public override string SettingsFromExistingAtlas(string path) => $"Atlas existente: {path}. Sem configurações guardadas — usando o padrão.";
            public override string SettingsDefault => "Primeira vez deste arquivo: usando as configurações padrão.";

            public override string SettingsHeader => "Empacotamento";
            public override string Padding => "Margem entre sprites";
            public override string PaddingHint => "pixels vazios separando um sprite do outro";
            public override string BorderPadding => "Margem da borda";
            public override string BorderPaddingHint => "espaço vazio nas bordas do atlas";
            public override string Extrude => "Extrude";
            public override string ExtrudeHint => "repete a borda do sprite para fora, evitando bleeding";
            public override string MaxSize => "Tamanho máximo";
            public override string MaxSizeHint => "largura e altura máximas do atlas";
            public override string PowerOfTwo => "Dimensões em potência de 2";
            public override string PowerOfTwoHint => "512, 1024, 2048… exigido pela maioria das engines";
            public override string Square => "Forçar atlas quadrado";
            public override string SquareHint => "desligado, é escolhido o formato mais equilibrado que couber";
            public override string IgnoreHidden => "Ignorar camadas desativadas";
            public override string IgnoreHiddenHint => "camadas com o olho fechado no Krita (ou dentro de um grupo fechado) ficam de fora";
            public override string ResetDefaults => "Restaurar padrão";
            public override string Generate => "Gerar atlas";
            public override string GenerateHint => "Lê o arquivo do Krita, empacota, grava o PNG e a planilha e importa";
            public override string SettingsChangedHint => "Configurações alteradas — clique em Gerar atlas para aplicar.";

            public override string MetricSize => "DIMENSÕES";
            public override string MetricOccupancy => "OCUPAÇÃO";
            public override string MetricSprites => "SPRITES";
            public override string MetricTime => "TEMPO";
            public override string PreviewHeader => "Pré-visualização";
            public override string Outlines => "Contornos";
            public override string OutlinesHint => "Mostrar o contorno de cada sprite";
            public override string LogHeader => "Log";

            public override string ConvertFirstTitle => "Atlas do TexturePacker";
            public override string ConvertFirstMessage(string tpSheetFile) =>
                $"Este atlas ainda é descrito por {tpSheetFile} (TexturePacker Importer).\n\n" +
                "Antes de gerar, ele precisa ser convertido para o Atlasmith. A conversão mantém o ID de cada sprite, " +
                "então as referências em cenas e prefabs continuam funcionando, e guarda um backup em AtlasmithBackups.\n\n" +
                "Converter e gerar agora?";
            public override string ConvertFirstOk => "Converter e gerar";
            public override string ConvertFirstDeclined => "Geração cancelada: o atlas continua com o TexturePacker.";
            public override string ConfirmOtherSource(string atlas, string generatedFrom, string now) =>
                $"O atlas \"{atlas}\" foi gerado a partir de:\n{generatedFrom}\n\nAgora a origem é:\n{now}\n\nGerar e substituir os arquivos dele?";

            public override string StageReading(int done, int total) => total > 0 ? $"Lendo camadas… {done}/{total}" : "Lendo o arquivo do Krita…";
            public override string StagePacking => "Calculando o menor atlas…";
            public override string StageComposing => "Montando a imagem…";
            public override string StageSaving => "Salvando…";
            public override string NotAKritaFile(string path) => $"{path} não é um arquivo do Krita (.kra ou .krz) que exista.";
            public override string InvalidAtlasName(string name) => $"\"{name}\" não serve como nome de atlas.";
            public override string TargetIsTexturePacker(string tpSheetPath) => $"O destino ainda é um atlas do TexturePacker ({tpSheetPath}). Converta-o antes.";
            public override string ConversionBeforeGenerateFailed(string tpSheetPath) => $"A conversão de {tpSheetPath} não deu certo; nada foi gerado. Detalhes no log.";
            public override string LogGenerating(string kritaPath, string pngPath) => $"Gerando {pngPath} a partir de {kritaPath}";
            public override string LogError(string message) => $"ERRO: {message}";
            public override string LogWarning(string message) => $"aviso — {message}";
            public override string LogSummary(int sprites, int kept, int added, int removed) =>
                $"Importado: {sprites} sprites ({kept} mantidos com o mesmo ID, {added} novos, {removed} removidos).";
            public override string GeneratedOk(string pngPath) => $"Pronto — {pngPath} gerado e importado.";
            public override string GenerationCancelled => "Geração cancelada.";
            public override string GenerationFailedUnknown => "Não foi possível gerar o atlas.";
            public override string WarnRemovedSprites(IReadOnlyCollection<string> names) =>
                $"{names.Count} sprite(s) saíram do atlas e as referências a eles ficam vazias: {Names(names)}. " +
                "Se voltarem com o mesmo nome, recuperam o mesmo fileID e as referências voltam a funcionar.";
            public override string WarnChangedIds(IReadOnlyCollection<string> names) => $"Sprites que mudaram de ID (as referências a eles se perderam): {Names(names)}.";
            public override string WarnImportCount(int imported, int expected) => $"A textura importou {imported} sprites; a planilha tem {expected}.";
            public override string WarnMaxTextureSize(int maxTextureSize, int atlasSide) =>
                $"O atlas tem {atlasSide}px, mas o Max Size da textura é {maxTextureSize}: o Unity vai reduzi-la. Aumente o Max Size no Inspector da textura.";

            public override string StageCheckingRemoval => "Procurando quem usa os sprites que sairiam…";
            public override string RemovalTitle => "Sprites sairiam do atlas";
            public override string RemovalGenerateAnyway => "Remover e gerar";
            public override string RemovalIncludeHidden => "Incluir camadas ocultas";
            public override string RemovalStopped(int count) => $"Nada foi gravado: {count} sprite(s) do atlas atual ficariam de fora:";
            public override string RemovalAccepted(int count) => $"Gerando mesmo assim: {count} sprite(s) saem do atlas.";
            public override string RemovalIncludingHidden => "Incluindo as camadas ocultas neste atlas (a escolha fica guardada na planilha).";
            public override string RemovalCancelled => "Nada foi gravado: o atlas continua como estava.";
            protected override string HiddenLayerTag => "(camada oculta)";
            protected override string UsedIn(string files) => $"usado em {files}";
            protected override string RemovalIntro(string atlas, int count) =>
                count == 1 ? $"Gerar \"{atlas}\" agora tiraria 1 sprite do atlas:" : $"Gerar \"{atlas}\" agora tiraria {count} sprites do atlas:";
            protected override string RemovalReferenced(int referenced) =>
                (referenced == 1 ? "1 deles é usado" : $"{referenced} deles são usados") +
                " no projeto (prefabs, cenas, animações…). Removidos, essas referências ficam vazias (Missing).";
            protected override string RemovalNoneReferenced => "Nenhum deles é usado nos prefabs, cenas e assets do projeto.";
            protected override string RemovalHiddenHint(int hidden) =>
                (hidden == 1 ? "1 está numa camada desativada" : $"{hidden} estão em camadas desativadas") +
                " no Krita. \"Incluir camadas ocultas\" gera com elas e guarda essa escolha para este atlas.";
            protected override string RemovalSameIdHint => "Se um sprite voltar depois com o mesmo nome, ele recupera o mesmo fileID e as referências voltam a funcionar.";
            protected override string RemovalSkippedIntro(string pngPath, int count) =>
                $"{pngPath} não foi gerado: {count} sprite(s) do atlas atual ficariam de fora.";
            protected override string RemovalSkippedHow =>
                "Para gerar mesmo assim, chame AtlasGenerator.Generate(recipe, allowRemovingSprites: true) ou desligue IgnoreHiddenLayers na receita.";

            public override string ImporterTpSheetConflict(string texturePath, string tpSheetPath) =>
                $"Atlasmith: {texturePath} tem um .atlasmith.json e também {tpSheetPath}. O TexturePacker Importer continua cuidando da textura; " +
                "converta pelo menu Tools/Atlasmith/Convert TexturePacker Sheets… para passar ao Atlasmith.";
            public override string ImporterInvalidSheet(string sheetPath, string error) => $"Atlasmith: não foi possível ler {sheetPath}: {error}";
            public override string ImporterTextureMismatch(string sheetPath, string sheetTexture, string textureFile) =>
                $"Atlasmith: {sheetPath} descreve \"{sheetTexture}\", não \"{textureFile}\"; a textura não foi alterada.";
            public override string ImporterSizeMismatch(string texturePath, int width, int height, int sheetWidth, int sheetHeight) =>
                $"Atlasmith: {texturePath} tem {width}x{height}, mas a planilha diz {sheetWidth}x{sheetHeight}. Gere o atlas de novo.";
            public override string ImporterSpritesRemoved(string texturePath, IReadOnlyCollection<string> names) =>
                $"Atlasmith: {names.Count} sprite(s) removido(s) de {texturePath} porque não estão mais na planilha: {Names(names)}. " +
                "As referências a eles ficam vazias; se voltarem com o mesmo nome, recuperam o mesmo fileID e elas voltam a funcionar.";
            public override string ImporterDuplicateNames(string sheetPath, IReadOnlyCollection<string> names) =>
                $"Atlasmith: nomes repetidos em {sheetPath} (só o primeiro de cada entrou): {Names(names)}";
            public override string ImporterOutOfBounds(string sheetPath, IReadOnlyCollection<string> names) =>
                $"Atlasmith: sprites fora dos limites da textura em {sheetPath}: {Names(names)}";

            public override string ConvertTitle => "TexturePacker → Atlasmith";
            public override string ConvertIntro =>
                "Converte atlas do TexturePacker (.tpsheet) para .atlasmith.json. A planilha nova sai do que o Unity tem hoje " +
                "(retângulos, pivôs e bordas), cada sprite mantém o spriteID e o fileID, e o resultado é conferido sprite por sprite — " +
                "se algo não bater, tudo volta ao que era. Antes de mexer, os arquivos são copiados para AtlasmithBackups, na raiz do projeto.";
            public override string ConvertNothingFound => "Nenhum .tpsheet no projeto.";
            public override string SelectAll => "Marcar todos";
            public override string SelectNone => "Desmarcar";
            public override string ConvertButton => "Converter";
            public override string ConvertSelectedButton(int count) => count == 1 ? "Converter 1 atlas" : $"Converter {count} atlas";
            public override string ConvertConfirm(int count) =>
                (count == 1 ? "Converter 1 atlas do TexturePacker para o Atlasmith?" : $"Converter {count} atlas do TexturePacker para o Atlasmith?") +
                "\n\nO .tpsheet de cada um é apagado (há backup em AtlasmithBackups) e a textura passa a ser importada pelo Atlasmith.";
            public override string SpriteCount(int count) => count == 1 ? "1 sprite" : $"{count} sprites";
            public override string ResultsHeader => "Resultado";
            public override string ConvertSummary(int converted, int total) => $"{converted} de {total} convertido(s).";
            public override string MoreDifferences(int count) => $"… e mais {count} diferença(s).";
            public override string OpenBackupFolder => "Abrir a pasta de backup";
            public override string ConvertResultLine(TpConversionResult result)
            {
                switch (result.Status)
                {
                    case TpConversionStatus.Converted: return $"✔ {result.TpSheetPath} → {System.IO.Path.GetFileName(result.SheetPath)} ({SpriteCount(result.SpriteCount)})";
                    case TpConversionStatus.RolledBack: return $"↺ {result.TpSheetPath}: desfeito, nada mudou";
                    case TpConversionStatus.Skipped: return $"– {result.TpSheetPath}: não convertido";
                    default: return $"✖ {result.TpSheetPath}: falhou";
                }
            }

            public override string ConvertProblemNoTexture(string texturePath) => $"textura {texturePath} não encontrada";
            public override string ConvertProblemNotSprites => "a textura não tem sprites (o TexturePacker Importer ainda não a importou?)";
            public override string ConvertProblemAlreadyHasSheet(string sheetFile) => $"já existe {sheetFile}";
            public override string ConvertProblemNoSize(string texturePath) => $"não foi possível ler o tamanho de {texturePath}";
            public override string ConvertProblemDuplicate(string name) => $"há dois sprites chamados \"{name}\"; renomeie um no Sprite Editor";
            public override string ConvertProblemFractional(string name) => $"o sprite \"{name}\" tem retângulo ou borda fracionários, que a planilha não guarda";
            public override string ConvertBackupFailed(string error) => $"Não foi possível fazer o backup, nada foi alterado: {error}";
            public override string ConvertBackedUp(string folder) => $"Backup em {folder}";
            public override string ConvertDeleteFailed(string path) => $"não foi possível apagar {path}";
            public override string ConvertTpSheetStillThere => "o .tpsheet continua lá";
            public override string ConvertStillListed => "o TexturePacker Importer ainda lista a planilha nas configurações dele";
            public override string ConvertSettingsCleaned(string file) => $"Entrada removida de {file}.";
            public override string ConvertDone(string sheetFile, int sprites) =>
                $"Convertido para {sheetFile}: {sprites} sprites conferidos (nome, retângulo, pivô, alinhamento, bordas, spriteID e fileID iguais).";
            public override string ConvertMismatch(int differences) => $"A verificação achou {differences} diferença(s); desfazendo a conversão.";
            public override string ConvertRolledBack => "Conversão desfeita: os arquivos voltaram do backup e os sprites estão como antes.";
            public override string ConvertRollbackIncomplete(string backupFolder) =>
                $"A volta automática não restaurou tudo. Os arquivos originais estão em {backupFolder} — copie-os de volta para o projeto.";
        }

        private sealed class English : Texts
        {
            public override TextLanguage Language => TextLanguage.English;
            public override CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("en-US");
            protected override string AndMore(int count) => $"and {count} more";

            public override string Cancel => "Cancel";
            public override string Copy => "Copy";
            public override string Clear => "Clear";
            public override string Refresh => "Refresh";
            public override string Replace => "Replace";
            public override string Show => "Show";
            public override string LanguageLabel => "Language";
            public override string LanguageHint => "Language of Atlasmith's windows and messages (Unity menus stay in English)";
            public override string LanguageAuto => "Automatic";

            public override string CreateAtlasTitle => "Create Atlas";
            public override string NoKritaSelected => "Right-click a .kra or .krz file in the Project window and choose Create Atlas.";
            public override string SourceLabel => "Source";
            public override string OutputLabel => "Atlas";
            public override string Change => "Change…";
            public override string ChangeOutputTitle => "Save atlas as";
            public override string ChangeOutputMessage => "Choose where to write the atlas PNG (the .atlasmith.json sheet goes next to it).";
            public override string KritaFileMissing(string path) => $"The file {path} no longer exists.";
            public override string TargetUsesTexturePacker =>
                "This atlas still uses TexturePacker (.tpsheet). Generating converts it to Atlasmith first — sprite references are kept.";
            public override string SettingsFromSheet(string path) => $"Settings stored in {path}.";
            public override string SettingsFromExistingAtlas(string path) => $"Existing atlas: {path}. No stored settings — using the defaults.";
            public override string SettingsDefault => "First time for this file: using the default settings.";

            public override string SettingsHeader => "Packing";
            public override string Padding => "Sprite padding";
            public override string PaddingHint => "empty pixels between one sprite and the next";
            public override string BorderPadding => "Border padding";
            public override string BorderPaddingHint => "empty space along the atlas edges";
            public override string Extrude => "Extrude";
            public override string ExtrudeHint => "repeats the sprite edge outward to avoid bleeding";
            public override string MaxSize => "Maximum size";
            public override string MaxSizeHint => "maximum atlas width and height";
            public override string PowerOfTwo => "Power-of-two size";
            public override string PowerOfTwoHint => "512, 1024, 2048… required by most engines";
            public override string Square => "Force square atlas";
            public override string SquareHint => "when off, the most balanced shape that fits is chosen";
            public override string IgnoreHidden => "Ignore hidden layers";
            public override string IgnoreHiddenHint => "layers with the eye closed in Krita (or inside a hidden group) are left out";
            public override string ResetDefaults => "Reset to defaults";
            public override string Generate => "Generate atlas";
            public override string GenerateHint => "Reads the Krita file, packs, writes the PNG and the sheet, and imports them";
            public override string SettingsChangedHint => "Settings changed — click Generate atlas to apply them.";

            public override string MetricSize => "SIZE";
            public override string MetricOccupancy => "OCCUPANCY";
            public override string MetricSprites => "SPRITES";
            public override string MetricTime => "TIME";
            public override string PreviewHeader => "Preview";
            public override string Outlines => "Outlines";
            public override string OutlinesHint => "Show each sprite's outline";
            public override string LogHeader => "Log";

            public override string ConvertFirstTitle => "TexturePacker atlas";
            public override string ConvertFirstMessage(string tpSheetFile) =>
                $"This atlas is still described by {tpSheetFile} (TexturePacker Importer).\n\n" +
                "It has to be converted to Atlasmith before generating. The conversion keeps every sprite's ID, so references " +
                "in scenes and prefabs keep working, and it saves a backup in AtlasmithBackups.\n\n" +
                "Convert and generate now?";
            public override string ConvertFirstOk => "Convert and generate";
            public override string ConvertFirstDeclined => "Generation cancelled: the atlas stays with TexturePacker.";
            public override string ConfirmOtherSource(string atlas, string generatedFrom, string now) =>
                $"The atlas \"{atlas}\" was generated from:\n{generatedFrom}\n\nThe source is now:\n{now}\n\nGenerate and replace its files?";

            public override string StageReading(int done, int total) => total > 0 ? $"Reading layers… {done}/{total}" : "Reading the Krita file…";
            public override string StagePacking => "Finding the smallest atlas…";
            public override string StageComposing => "Composing the image…";
            public override string StageSaving => "Saving…";
            public override string NotAKritaFile(string path) => $"{path} isn't an existing Krita file (.kra or .krz).";
            public override string InvalidAtlasName(string name) => $"\"{name}\" can't be used as an atlas name.";
            public override string TargetIsTexturePacker(string tpSheetPath) => $"The target is still a TexturePacker atlas ({tpSheetPath}). Convert it first.";
            public override string ConversionBeforeGenerateFailed(string tpSheetPath) => $"Converting {tpSheetPath} didn't succeed; nothing was generated. Details are in the log.";
            public override string LogGenerating(string kritaPath, string pngPath) => $"Generating {pngPath} from {kritaPath}";
            public override string LogError(string message) => $"ERROR: {message}";
            public override string LogWarning(string message) => $"warning — {message}";
            public override string LogSummary(int sprites, int kept, int added, int removed) =>
                $"Imported: {sprites} sprites ({kept} kept with the same ID, {added} new, {removed} removed).";
            public override string GeneratedOk(string pngPath) => $"Done — {pngPath} generated and imported.";
            public override string GenerationCancelled => "Generation cancelled.";
            public override string GenerationFailedUnknown => "Couldn't generate the atlas.";
            public override string WarnRemovedSprites(IReadOnlyCollection<string> names) =>
                $"{names.Count} sprite(s) left the atlas and references to them become empty: {Names(names)}. " +
                "If they come back with the same name, they get the same fileID back and the references work again.";
            public override string WarnChangedIds(IReadOnlyCollection<string> names) => $"Sprites whose ID changed (references to them were lost): {Names(names)}.";
            public override string WarnImportCount(int imported, int expected) => $"The texture imported {imported} sprites; the sheet has {expected}.";
            public override string WarnMaxTextureSize(int maxTextureSize, int atlasSide) =>
                $"The atlas is {atlasSide}px but the texture's Max Size is {maxTextureSize}, so Unity will downscale it. Raise Max Size in the texture's Inspector.";

            public override string StageCheckingRemoval => "Looking for uses of the sprites that would be removed…";
            public override string RemovalTitle => "Sprites would leave the atlas";
            public override string RemovalGenerateAnyway => "Remove and generate";
            public override string RemovalIncludeHidden => "Include hidden layers";
            public override string RemovalStopped(int count) => $"Nothing was written: {count} sprite(s) of the current atlas would be left out:";
            public override string RemovalAccepted(int count) => $"Generating anyway: {count} sprite(s) leave the atlas.";
            public override string RemovalIncludingHidden => "Including hidden layers for this atlas (the choice is stored in the sheet).";
            public override string RemovalCancelled => "Nothing was written: the atlas stays as it was.";
            protected override string HiddenLayerTag => "(hidden layer)";
            protected override string UsedIn(string files) => $"used in {files}";
            protected override string RemovalIntro(string atlas, int count) =>
                count == 1 ? $"Generating \"{atlas}\" now would remove 1 sprite from the atlas:" : $"Generating \"{atlas}\" now would remove {count} sprites from the atlas:";
            protected override string RemovalReferenced(int referenced) =>
                (referenced == 1 ? "1 of them is used" : $"{referenced} of them are used") +
                " in the project (prefabs, scenes, animations…). If removed, those references become empty (Missing).";
            protected override string RemovalNoneReferenced => "None of them is used in the project's prefabs, scenes or assets.";
            protected override string RemovalHiddenHint(int hidden) =>
                (hidden == 1 ? "1 is on a hidden layer" : $"{hidden} are on hidden layers") +
                " in Krita. \"Include hidden layers\" generates with them and remembers that choice for this atlas.";
            protected override string RemovalSameIdHint => "If a sprite comes back later with the same name, it gets the same fileID back and the references work again.";
            protected override string RemovalSkippedIntro(string pngPath, int count) =>
                $"{pngPath} was not generated: {count} sprite(s) of the current atlas would be left out.";
            protected override string RemovalSkippedHow =>
                "To generate anyway, call AtlasGenerator.Generate(recipe, allowRemovingSprites: true) or turn IgnoreHiddenLayers off in the recipe.";

            public override string ImporterTpSheetConflict(string texturePath, string tpSheetPath) =>
                $"Atlasmith: {texturePath} has an .atlasmith.json and also {tpSheetPath}. TexturePacker Importer stays in charge of the texture; " +
                "convert it with Tools/Atlasmith/Convert TexturePacker Sheets… to hand it over to Atlasmith.";
            public override string ImporterInvalidSheet(string sheetPath, string error) => $"Atlasmith: couldn't read {sheetPath}: {error}";
            public override string ImporterTextureMismatch(string sheetPath, string sheetTexture, string textureFile) =>
                $"Atlasmith: {sheetPath} describes \"{sheetTexture}\", not \"{textureFile}\"; the texture was left unchanged.";
            public override string ImporterSizeMismatch(string texturePath, int width, int height, int sheetWidth, int sheetHeight) =>
                $"Atlasmith: {texturePath} is {width}x{height} but the sheet says {sheetWidth}x{sheetHeight}. Regenerate the atlas.";
            public override string ImporterSpritesRemoved(string texturePath, IReadOnlyCollection<string> names) =>
                $"Atlasmith: removed {names.Count} sprite(s) from {texturePath} because they're no longer in the sheet: {Names(names)}. " +
                "References to them become empty; if they come back with the same name, they get the same fileID back and the references work again.";
            public override string ImporterDuplicateNames(string sheetPath, IReadOnlyCollection<string> names) =>
                $"Atlasmith: duplicate names in {sheetPath} (only the first of each was used): {Names(names)}";
            public override string ImporterOutOfBounds(string sheetPath, IReadOnlyCollection<string> names) =>
                $"Atlasmith: sprites outside the texture bounds in {sheetPath}: {Names(names)}";

            public override string ConvertTitle => "TexturePacker → Atlasmith";
            public override string ConvertIntro =>
                "Converts TexturePacker atlases (.tpsheet) to .atlasmith.json. The new sheet is built from what Unity has today " +
                "(rects, pivots and borders), every sprite keeps its spriteID and fileID, and the result is checked sprite by sprite — " +
                "if anything differs, everything goes back to how it was. Files are copied to AtlasmithBackups, at the project root, first.";
            public override string ConvertNothingFound => "No .tpsheet files in the project.";
            public override string SelectAll => "Select all";
            public override string SelectNone => "Select none";
            public override string ConvertButton => "Convert";
            public override string ConvertSelectedButton(int count) => count == 1 ? "Convert 1 atlas" : $"Convert {count} atlases";
            public override string ConvertConfirm(int count) =>
                (count == 1 ? "Convert 1 TexturePacker atlas to Atlasmith?" : $"Convert {count} TexturePacker atlases to Atlasmith?") +
                "\n\nEach .tpsheet is deleted (a backup goes to AtlasmithBackups) and the texture is imported by Atlasmith from then on.";
            public override string SpriteCount(int count) => count == 1 ? "1 sprite" : $"{count} sprites";
            public override string ResultsHeader => "Result";
            public override string ConvertSummary(int converted, int total) => $"{converted} of {total} converted.";
            public override string MoreDifferences(int count) => $"… and {count} more difference(s).";
            public override string OpenBackupFolder => "Open backup folder";
            public override string ConvertResultLine(TpConversionResult result)
            {
                switch (result.Status)
                {
                    case TpConversionStatus.Converted: return $"✔ {result.TpSheetPath} → {System.IO.Path.GetFileName(result.SheetPath)} ({SpriteCount(result.SpriteCount)})";
                    case TpConversionStatus.RolledBack: return $"↺ {result.TpSheetPath}: rolled back, nothing changed";
                    case TpConversionStatus.Skipped: return $"– {result.TpSheetPath}: not converted";
                    default: return $"✖ {result.TpSheetPath}: failed";
                }
            }

            public override string ConvertProblemNoTexture(string texturePath) => $"texture {texturePath} not found";
            public override string ConvertProblemNotSprites => "the texture has no sprites (hasn't TexturePacker Importer imported it yet?)";
            public override string ConvertProblemAlreadyHasSheet(string sheetFile) => $"{sheetFile} already exists";
            public override string ConvertProblemNoSize(string texturePath) => $"couldn't read the size of {texturePath}";
            public override string ConvertProblemDuplicate(string name) => $"there are two sprites named \"{name}\"; rename one in the Sprite Editor";
            public override string ConvertProblemFractional(string name) => $"sprite \"{name}\" has a fractional rect or border, which the sheet can't store";
            public override string ConvertBackupFailed(string error) => $"Couldn't make the backup, nothing was changed: {error}";
            public override string ConvertBackedUp(string folder) => $"Backup in {folder}";
            public override string ConvertDeleteFailed(string path) => $"couldn't delete {path}";
            public override string ConvertTpSheetStillThere => "the .tpsheet is still there";
            public override string ConvertStillListed => "TexturePacker Importer still lists the sheet in its settings";
            public override string ConvertSettingsCleaned(string file) => $"Entry removed from {file}.";
            public override string ConvertDone(string sheetFile, int sprites) =>
                $"Converted to {sheetFile}: {sprites} sprites checked (name, rect, pivot, alignment, borders, spriteID and fileID identical).";
            public override string ConvertMismatch(int differences) => $"The check found {differences} difference(s); rolling the conversion back.";
            public override string ConvertRolledBack => "Conversion rolled back: the files came back from the backup and the sprites are as before.";
            public override string ConvertRollbackIncomplete(string backupFolder) =>
                $"The automatic rollback didn't restore everything. The original files are in {backupFolder} — copy them back into the project.";
        }
    }
}

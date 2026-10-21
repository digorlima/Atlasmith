using System.Globalization;
using System.Reflection;
using System.Windows;
using Atlasmith.Core;

namespace Atlasmith.Localization;

public enum AppLanguage
{
    /// <summary>Segue o idioma do Windows: português para pt-*, inglês para o resto.</summary>
    System,
    Portuguese,
    English
}

/// <summary>
/// Todos os textos da interface, em português e inglês. Cada idioma é uma classe
/// concreta, então esquecer uma tradução é erro de compilação, não um texto vazio.
///
/// As propriedades viram recursos "T.Nome" do Application, usados no XAML com
/// {DynamicResource T.Nome}: trocar o idioma atualiza a janela na hora. Os métodos
/// montam as frases com números e caminhos, usadas pelo código.
/// </summary>
public abstract class Texts
{
    public static Texts Current { get; private set; } = For(CoreText.SystemLanguage());

    /// <summary>Disparado depois da troca de idioma, para quem monta textos no código.</summary>
    public static event Action? Changed;

    public static Texts For(TextLanguage language) =>
        language == TextLanguage.Portuguese ? new Portuguese() : new English();

    public static TextLanguage Resolve(AppLanguage language) => language switch
    {
        AppLanguage.Portuguese => TextLanguage.Portuguese,
        AppLanguage.English => TextLanguage.English,
        _ => CoreText.SystemLanguage()
    };

    public static void Apply(AppLanguage language)
    {
        var resolved = Resolve(language);
        Current = For(resolved);
        CoreText.Use(resolved);

        if (Application.Current is { } app)
        {
            foreach (var property in typeof(Texts).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.PropertyType == typeof(string))
                    app.Resources["T." + property.Name] = property.GetValue(Current);
            }
        }

        Changed?.Invoke();
    }

    public abstract TextLanguage Language { get; }
    public abstract CultureInfo Culture { get; }

    // ------------------------------------------------------------------
    // Barra de título
    // ------------------------------------------------------------------

    public abstract string ThemeSystem { get; }
    public abstract string ThemeLight { get; }
    public abstract string ThemeDark { get; }
    public abstract string ThemeTip { get; }
    public abstract string LanguageAuto { get; }
    public abstract string LanguageTip { get; }
    public abstract string Minimize { get; }
    public abstract string Maximize { get; }
    public abstract string Restore { get; }
    public abstract string CloseWindow { get; }

    // ------------------------------------------------------------------
    // Atlas salvos
    // ------------------------------------------------------------------

    public abstract string SectionSaved { get; }
    public abstract string NewAtlas { get; }
    public abstract string NewAtlasTip { get; }
    public abstract string RemoveAtlasTip { get; }
    public abstract string Revert { get; }
    public abstract string RevertTip { get; }
    public abstract string NoSavedAtlases { get; }
    public abstract string ChooseSavedAtlas { get; }
    public abstract string SavedAtlasesEmpty { get; }
    public abstract string AtlasNotInList { get; }
    public abstract string AtlasChanged(string changes);
    public abstract string SpriteCount(int count);
    public abstract string Generated(string when);
    public abstract string StatusAtlasLoaded(string name);
    public abstract string StatusReverted(string name);
    public abstract string ConfirmRemove(string name, string files);
    public abstract string SavedListFailed(string message);
    public abstract string StatusRemoved(string name);
    public abstract string StatusNewAtlas { get; }
    public abstract string AddedToSaved(string name);
    public abstract string SavedListUpdateFailed(string message);

    public abstract string BadgeKrita { get; }
    public abstract string BadgeFolder { get; }
    public abstract string DetailsSource(string path);

    public abstract string ChangeSource { get; }
    public abstract string ChangePadding { get; }
    public abstract string ChangeBorder { get; }
    public abstract string ChangeExtrude { get; }
    public abstract string ChangeMaxSize { get; }
    public abstract string ChangePowerOfTwo { get; }
    public abstract string ChangeSquare { get; }
    public abstract string ChangeHidden { get; }
    public abstract string ChangeFormat { get; }

    // ------------------------------------------------------------------
    // Entrada
    // ------------------------------------------------------------------

    public abstract string SectionInput { get; }
    public abstract string ReadKrita { get; }
    public abstract string ReadKritaHint { get; }
    public abstract string IgnoreHidden { get; }
    public abstract string IgnoreHiddenHint { get; }
    public abstract string InputLabelKrita { get; }
    public abstract string InputLabelFolder { get; }
    public abstract string Browse { get; }
    public abstract string BrowseFolderTitle { get; }
    public abstract string BrowseKritaTitle { get; }
    public abstract string KritaFilter { get; }
    public abstract string StatusUsingNearbyKrita(string file);

    public abstract string FolderPrompt { get; }
    public abstract string FolderMissing { get; }
    public abstract string FolderNoImages { get; }
    public abstract string FolderImageCount(int count);
    public abstract string FolderReadFailed(string message);

    public abstract string KritaPrompt { get; }
    public abstract string NotKritaFile { get; }
    public abstract string FileMissing { get; }
    public abstract string KritaNoSpriteLayers { get; }
    public abstract string KritaSpriteCount(int count);
    public abstract string KritaSaved(string when);
    public abstract string KritaIgnored(string layers);
    public abstract string KritaHidden(string layers);
    public abstract string KritaProblem(string path, string reason);
    public abstract string KritaReadFailed(string message);
    public abstract string LayersBecomeSprites { get; }
    public abstract string LayersHidden { get; }
    public abstract string LayersIgnored { get; }
    public abstract string NoLayers { get; }
    public abstract string AndMore(string list, int more);

    public abstract string StatusKritaSet(string path);
    public abstract string StatusFolderSet(string path);

    // ------------------------------------------------------------------
    // Saída
    // ------------------------------------------------------------------

    public abstract string SectionOutput { get; }
    public abstract string OutputFolder { get; }
    public abstract string BrowseOutputTitle { get; }
    public abstract string AtlasName { get; }
    public abstract string SheetFormat { get; }
    public abstract string SheetFormatHint { get; }
    public abstract string FormatAtlasmith { get; }
    public abstract string FormatTpSheet { get; }
    public abstract string FormatBoth { get; }
    public abstract string OutputSummary(string files);
    public abstract string JoinAnd(IReadOnlyList<string> items);

    // ------------------------------------------------------------------
    // Empacotamento
    // ------------------------------------------------------------------

    public abstract string SectionPacking { get; }
    public abstract string Padding { get; }
    public abstract string PaddingHint { get; }
    public abstract string Border { get; }
    public abstract string BorderHint { get; }
    public abstract string Extrude { get; }
    public abstract string ExtrudeHint { get; }
    public abstract string MaxSize { get; }
    public abstract string PowerOfTwo { get; }
    public abstract string PowerOfTwoHint { get; }
    public abstract string Square { get; }
    public abstract string SquareHint { get; }
    public abstract string StorageNote { get; }

    // ------------------------------------------------------------------
    // Geração
    // ------------------------------------------------------------------

    public abstract string Generate { get; }
    public abstract string Generating { get; }
    public abstract string ChooseExistingKrita { get; }
    public abstract string SourceMustBeKrita { get; }
    public abstract string ChooseExistingFolder { get; }
    public abstract string ChooseOutputFolder { get; }
    public abstract string NameInvalidChar(char c);
    public abstract string ConfirmSourceChange(string name, string before, string now);
    public abstract string ConfirmStaleSheet(string stale, string written);
    public abstract string FormatFollowsDisk(string file);
    public abstract string ConfirmTexturePackerSheet(string file);
    public abstract string DeletedStale(string file);
    public abstract string SpritesLeaving(string names);
    public abstract string ConfirmRemovedSprites(int count, string names, string? hidden);
    public abstract string StatusCancelled { get; }

    public abstract string StatusLoadingImages { get; }
    public abstract string StatusReadingLayers { get; }
    public abstract string ReadingImagesFrom(string path);
    public abstract string ImagesLoaded(int count);
    public abstract string NoImagesFound { get; }
    public abstract string NoImagesFoundDetail { get; }
    public abstract string StatusPacking { get; }
    public abstract string StatusComposing { get; }
    public abstract string StatusSaving { get; }
    public abstract string StatusGenerated(string files);
    public abstract string StatusRightOccupancy(int w, int h, double occupancy);
    public abstract string ErrorLog(string message);
    public abstract string StatusFailed { get; }
    public abstract string GenerateFailed(string message);
    public abstract string Ready { get; }

    // ------------------------------------------------------------------
    // Pré-visualização
    // ------------------------------------------------------------------

    public abstract string MetricSize { get; }
    public abstract string MetricOccupancy { get; }
    public abstract string MetricSprites { get; }
    public abstract string MetricTime { get; }
    public abstract string OutlinesTip { get; }
    public abstract string Fit { get; }
    public abstract string OpenFolder { get; }
    public abstract string EmptyTitle { get; }
    public abstract string EmptyHint { get; }
    public abstract string StatusRightOnDisk(string file, string when);

    // ------------------------------------------------------------------
    // Datas
    // ------------------------------------------------------------------

    public abstract string JustNow { get; }
    public abstract string MinutesAgo(int minutes);
    public abstract string HoursAgo(int hours);
    public abstract string Today { get; }
    public abstract string Yesterday { get; }
    public abstract string DaysAgo(int days);
    public abstract string OnDate(DateTime date);

    // ------------------------------------------------------------------
    // Erro inesperado
    // ------------------------------------------------------------------

    public abstract string UnexpectedError(string chain, string logPath);

    // ------------------------------------------------------------------
    // Instalador
    // ------------------------------------------------------------------

    public abstract string InstallTitle { get; }
    public abstract string UpdateTitle { get; }
    public abstract string InstallIntro { get; }
    public abstract string InstallLocation { get; }
    public abstract string DesktopShortcut { get; }
    public abstract string StartMenuShortcut { get; }
    public abstract string LaunchAfterInstall { get; }
    public abstract string InstallButton { get; }
    public abstract string UpdateButton { get; }
    public abstract string Cancel { get; }
    public abstract string Installing { get; }
    public abstract string InstallDone { get; }
    public abstract string InstallFailed(string message);
    public abstract string CloseRunningApp { get; }
    public abstract string UninstallTitle { get; }
    public abstract string UninstallConfirm(string dataFolder);
    public abstract string UninstallDone { get; }
    public abstract string UninstallFailed(string message);

    // ==================================================================

    private sealed class Portuguese : Texts
    {
        public override TextLanguage Language => TextLanguage.Portuguese;
        public override CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("pt-BR");

        public override string ThemeSystem => "Sistema";
        public override string ThemeLight => "Claro";
        public override string ThemeDark => "Escuro";
        public override string ThemeTip => "Tema";
        public override string LanguageAuto => "Auto";
        public override string LanguageTip => "Idioma da interface (Auto segue o Windows)";
        public override string Minimize => "Minimizar";
        public override string Maximize => "Maximizar";
        public override string Restore => "Restaurar";
        public override string CloseWindow => "Fechar";

        public override string SectionSaved => "ATLAS SALVOS";
        public override string NewAtlas => "+ Novo atlas";
        public override string NewAtlasTip => "Limpa origem, destino e nome para configurar outro atlas. As margens ficam.";
        public override string RemoveAtlasTip => "Tirar da lista. Os arquivos do atlas não são apagados.";
        public override string Revert => "Desfazer";
        public override string RevertTip => "Volta aos valores da última vez que este atlas foi gerado";
        public override string NoSavedAtlases => "Nenhum atlas salvo ainda";
        public override string ChooseSavedAtlas => "Escolha um atlas salvo";
        public override string SavedAtlasesEmpty => "Todo atlas que você gerar fica guardado aqui, com origem, destino e margens.";
        public override string AtlasNotInList => "Este destino ainda não está na lista: entra quando você gerar.";
        public override string AtlasChanged(string changes) => $"Mudou desde a última geração: {changes}. Gerar atualiza o atlas salvo.";
        public override string SpriteCount(int count) => count == 1 ? "1 sprite" : $"{count} sprites";
        public override string Generated(string when) => "gerado " + when;
        public override string StatusAtlasLoaded(string name) => $"Atlas \"{name}\" carregado.";
        public override string StatusReverted(string name) => $"Voltaram os valores da última geração de \"{name}\".";
        public override string ConfirmRemove(string name, string files) =>
            $"Tirar \"{name}\" da lista de atlas salvos?\n\nOs arquivos {files} continuam onde estão.";
        public override string SavedListFailed(string message) => $"Não foi possível atualizar a lista de atlas salvos.\n\n{message}";
        public override string StatusRemoved(string name) => $"\"{name}\" saiu da lista de atlas salvos.";
        public override string StatusNewAtlas => "Novo atlas: escolha a origem (ou arraste-a para a janela), o destino e o nome.";
        public override string AddedToSaved(string name) => $"\"{name}\" entrou na lista de atlas salvos.";
        public override string SavedListUpdateFailed(string message) => $"Não foi possível atualizar a lista de atlas salvos: {message}";

        public override string BadgeKrita => "KRITA";
        public override string BadgeFolder => "PASTA";
        public override string DetailsSource(string path) => "Origem: " + path;

        public override string ChangeSource => "origem";
        public override string ChangePadding => "margem entre sprites";
        public override string ChangeBorder => "margem da borda";
        public override string ChangeExtrude => "extrude";
        public override string ChangeMaxSize => "tamanho máximo";
        public override string ChangePowerOfTwo => "potência de 2";
        public override string ChangeSquare => "atlas quadrado";
        public override string ChangeHidden => "camadas desativadas";
        public override string ChangeFormat => "formato da planilha";

        public override string SectionInput => "ENTRADA";
        public override string ReadKrita => "Ler direto do Krita";
        public override string ReadKritaHint => ".kra ou .krz, sem Batch Exporter: entram as camadas com nome no padrão nome-nome";
        public override string IgnoreHidden => "Ignorar camadas desativadas";
        public override string IgnoreHiddenHint => "camadas com o olho fechado no Krita (ou dentro de um grupo fechado) ficam de fora";
        public override string InputLabelKrita => "Arquivo do Krita";
        public override string InputLabelFolder => "Pasta com as imagens";
        public override string Browse => "Procurar";
        public override string BrowseFolderTitle => "Selecione a pasta com as imagens";
        public override string BrowseKritaTitle => "Selecione o arquivo do Krita";
        public override string KritaFilter => "Arquivos do Krita (*.kra, *.krz)|*.kra;*.krz";
        public override string StatusUsingNearbyKrita(string file) => $"Usando {file}, que estava ao lado do atlas. Confira se é o arquivo certo.";

        public override string FolderPrompt => "Selecione uma pasta ou arraste-a para cá. Subpastas entram junto.";
        public override string FolderMissing => "Essa pasta não existe.";
        public override string FolderNoImages => "Nenhuma imagem suportada encontrada nessa pasta.";
        public override string FolderImageCount(int count) =>
            count == 1 ? "1 imagem encontrada, incluindo subpastas." : $"{count} imagens encontradas, incluindo subpastas.";
        public override string FolderReadFailed(string message) => $"Não foi possível ler a pasta: {message}";

        public override string KritaPrompt => "Selecione o .kra ou .krz, ou arraste-o para cá. Vale o que está salvo em disco: salve no Krita antes de gerar.";
        public override string NotKritaFile => "Isso não é um arquivo do Krita (.kra ou .krz).";
        public override string FileMissing => "Esse arquivo não existe.";
        public override string KritaNoSpriteLayers => "Nenhuma camada com nome no padrão nome-nome (ex.: menu-botao-play).";
        public override string KritaSpriteCount(int count) => count == 1 ? "1 camada vira sprite" : $"{count} camadas viram sprites";
        public override string KritaSaved(string when) => $" · arquivo salvo {when}.";
        public override string KritaIgnored(string layers) => $"\nIgnoradas: {layers}.";
        public override string KritaHidden(string layers) => $"\nDesativadas, ficam de fora: {layers}.";
        public override string KritaProblem(string path, string reason) => $"\nAtenção, {path}: {reason}.";
        public override string KritaReadFailed(string message) => $"Não foi possível ler o arquivo do Krita: {message}";
        public override string LayersBecomeSprites => "Viram sprite:";
        public override string LayersHidden => "Desativadas:";
        public override string LayersIgnored => "Ignoradas:";
        public override string NoLayers => "O arquivo não tem camadas.";
        public override string AndMore(string list, int more) => $"{list} e mais {more}";

        public override string StatusKritaSet(string path) => $"Arquivo do Krita definido: {path}";
        public override string StatusFolderSet(string path) => $"Pasta de origem definida: {path}";

        public override string SectionOutput => "SAÍDA";
        public override string OutputFolder => "Pasta de destino";
        public override string BrowseOutputTitle => "Selecione onde salvar o atlas";
        public override string AtlasName => "Nome do atlas";
        public override string SheetFormat => "Planilha";
        public override string SheetFormatHint =>
            "o importer do Atlasmith para Unity lê o .atlasmith.json; o TexturePacker Importer só aceita .tpsheet feito pelo TexturePacker";
        public override string FormatAtlasmith => "Atlasmith (.atlasmith.json)";
        public override string FormatTpSheet => "TexturePacker (.tpsheet)";
        public override string FormatBoth => "As duas";
        public override string OutputSummary(string files) => $"Serão gerados {files}.";
        public override string JoinAnd(IReadOnlyList<string> items) =>
            items.Count <= 1 ? string.Join("", items) : string.Join(", ", items.Take(items.Count - 1)) + " e " + items[^1];

        public override string SectionPacking => "EMPACOTAMENTO";
        public override string Padding => "Margem entre sprites";
        public override string PaddingHint => "pixels vazios separando um sprite do outro";
        public override string Border => "Margem da borda";
        public override string BorderHint => "espaço vazio nas bordas do atlas";
        public override string Extrude => "Extrude";
        public override string ExtrudeHint => "repete a borda do sprite para fora, evitando bleeding";
        public override string MaxSize => "Tamanho máximo";
        public override string PowerOfTwo => "Dimensões em potência de 2";
        public override string PowerOfTwoHint => "512, 1024, 2048… exigido pela maioria das engines";
        public override string Square => "Forçar atlas quadrado";
        public override string SquareHint => "desligado, o app escolhe o formato mais equilibrado que couber";
        public override string StorageNote => "Os atlas salvos, as configurações e os caminhos ficam em %AppData%\\Atlasmith.";

        public override string Generate => "Gerar atlas";
        public override string Generating => "Gerando…";
        public override string ChooseExistingKrita => "Escolha um arquivo do Krita (.kra ou .krz) que exista.";
        public override string SourceMustBeKrita => "A origem precisa ser um arquivo do Krita (.kra ou .krz).";
        public override string ChooseExistingFolder => "Escolha uma pasta de origem que exista.";
        public override string ChooseOutputFolder => "Escolha a pasta onde o atlas será salvo.";
        public override string NameInvalidChar(char c) => $"O nome do atlas não pode conter o caractere '{c}'.";
        public override string ConfirmSourceChange(string name, string before, string now) =>
            $"O atlas \"{name}\" foi gerado a partir de:\n{before}\n\nAgora a origem é:\n{now}\n\nGerar e substituir os arquivos dele?";
        public override string ConfirmStaleSheet(string stale, string written) =>
            $"Ao lado do atlas existe {stale}, que ficaria desatualizado: o novo atlas vai junto com {written}.\n\n" +
            "Sim — apagar o arquivo antigo.\nNão — manter os dois (o antigo continua descrevendo o atlas anterior).\n\n" +
            "Num projeto Unity que usa o TexturePacker Importer, converta pelo pacote do Atlasmith no Unity antes: " +
            "ele preserva as referências dos sprites.";
        public override string FormatFollowsDisk(string file) => $"O atlas já usa {file}: a planilha continua nesse formato.";
        public override string ConfirmTexturePackerSheet(string file) =>
            $"{file} foi feito pelo TexturePacker (ou por uma beta do Atlasmith). No Unity, quem lê esse arquivo é o " +
            "TexturePacker Importer, e ele recusa planilhas gravadas por outros programas: o PNG mudaria e os sprites " +
            "ficariam presos ao atlas anterior.\n\n" +
            "Antes, converta o atlas no Unity com o pacote do Atlasmith (Tools → Atlasmith → Convert TexturePacker Sheets…). " +
            "Depois disso, o Atlasmith passa a gravar .atlasmith.json para ele.\n\n" +
            "Gravar o .tpsheet mesmo assim?";
        public override string DeletedStale(string file) => $"Apagado {file} (desatualizado).";
        public override string SpritesLeaving(string names) => $"Saem do atlas atual: {names}.";
        public override string ConfirmRemovedSprites(int count, string names, string? hidden) =>
            (count == 1 ? $"1 sprite que está no atlas atual vai sair: {names}." : $"{count} sprites que estão no atlas atual vão sair: {names}.") +
            (hidden is null ? "" : $"\n\nEstão em camadas desativadas do Krita: {hidden}. Para mantê-los, desligue “Ignorar camadas desativadas” ou ative essas camadas.") +
            "\n\nNo Unity, o que usar esses sprites (prefabs, cenas, animações) perde a referência. Gerar mesmo assim?";
        public override string StatusCancelled => "Geração cancelada: nada foi gravado.";

        public override string StatusLoadingImages => "Carregando imagens";
        public override string StatusReadingLayers => "Lendo camadas";
        public override string ReadingImagesFrom(string path) => $"Lendo imagens de {path}";
        public override string ImagesLoaded(int count) => count == 1 ? "1 imagem carregada." : $"{count} imagens carregadas.";
        public override string NoImagesFound => "Nenhuma imagem suportada foi encontrada.";
        public override string NoImagesFoundDetail => "Nenhuma imagem suportada foi encontrada nessa pasta.";
        public override string StatusPacking => "Calculando o menor atlas…";
        public override string StatusComposing => "Montando a imagem…";
        public override string StatusSaving => "Salvando…";
        public override string StatusGenerated(string files) => $"Pronto — {files} gerados.";
        public override string StatusRightOccupancy(int w, int h, double occupancy) =>
            string.Format(Culture, "{0}×{1} · {2:0.0}% de aproveitamento", w, h, occupancy * 100);
        public override string ErrorLog(string message) => $"ERRO: {message}";
        public override string StatusFailed => "Falhou.";
        public override string GenerateFailed(string message) => $"Não foi possível gerar o atlas.\n\n{message}";
        public override string Ready => "Pronto.";

        public override string MetricSize => "DIMENSÕES";
        public override string MetricOccupancy => "OCUPAÇÃO";
        public override string MetricSprites => "SPRITES";
        public override string MetricTime => "TEMPO";
        public override string OutlinesTip => "Mostrar o contorno de cada sprite";
        public override string Fit => "Ajustar";
        public override string OpenFolder => "Abrir pasta";
        public override string EmptyTitle => "Nenhum atlas gerado ainda";
        public override string EmptyHint => "Escolha a origem (pasta ou arquivo do Krita) e clique em Gerar atlas.";
        public override string StatusRightOnDisk(string file, string when) => $"{file} em disco · gravado {when}";

        public override string JustNow => "agora há pouco";
        public override string MinutesAgo(int minutes) => $"há {minutes} min";
        public override string HoursAgo(int hours) => $"há {hours} h";
        public override string Today => "hoje";
        public override string Yesterday => "ontem";
        public override string DaysAgo(int days) => $"há {days} dias";
        public override string OnDate(DateTime date) => "em " + date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        public override string UnexpectedError(string chain, string logPath) =>
            $"Ocorreu um erro inesperado:\n\n{chain}\nDetalhes completos em:\n{logPath}";

        public override string InstallTitle => "Instalar o Atlasmith";
        public override string UpdateTitle => "Atualizar o Atlasmith";
        public override string InstallIntro => "Gera atlas de sprites direto dos arquivos do Krita, para o Unity ou qualquer engine.";
        public override string InstallLocation => "Instala para o seu usuário, sem pedir permissão de administrador, em:";
        public override string DesktopShortcut => "Atalho na área de trabalho";
        public override string StartMenuShortcut => "Atalho no menu Iniciar";
        public override string LaunchAfterInstall => "Abrir o Atlasmith ao terminar";
        public override string InstallButton => "Instalar";
        public override string UpdateButton => "Atualizar";
        public override string Cancel => "Cancelar";
        public override string Installing => "Instalando…";
        public override string InstallDone => "Pronto: o Atlasmith foi instalado. Ele aparece em Configurações → Aplicativos, onde também pode ser desinstalado.";
        public override string InstallFailed(string message) => $"Não foi possível instalar o Atlasmith.\n\n{message}";
        public override string CloseRunningApp => "O Atlasmith instalado está aberto. Feche-o e tente de novo.";
        public override string UninstallTitle => "Desinstalar o Atlasmith";
        public override string UninstallConfirm(string dataFolder) =>
            $"Remover o Atlasmith deste computador?\n\nOs atlas gerados não são tocados, e as configurações continuam em {dataFolder}.";
        public override string UninstallDone => "O Atlasmith foi removido.";
        public override string UninstallFailed(string message) => $"Não foi possível remover o Atlasmith por completo.\n\n{message}";
    }

    private sealed class English : Texts
    {
        public override TextLanguage Language => TextLanguage.English;
        public override CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("en-US");

        public override string ThemeSystem => "System";
        public override string ThemeLight => "Light";
        public override string ThemeDark => "Dark";
        public override string ThemeTip => "Theme";
        public override string LanguageAuto => "Auto";
        public override string LanguageTip => "Interface language (Auto follows Windows)";
        public override string Minimize => "Minimize";
        public override string Maximize => "Maximize";
        public override string Restore => "Restore";
        public override string CloseWindow => "Close";

        public override string SectionSaved => "SAVED ATLASES";
        public override string NewAtlas => "+ New atlas";
        public override string NewAtlasTip => "Clears source, destination and name to set up another atlas. The padding settings stay.";
        public override string RemoveAtlasTip => "Remove from the list. The atlas files are not deleted.";
        public override string Revert => "Undo";
        public override string RevertTip => "Go back to the values from the last time this atlas was generated";
        public override string NoSavedAtlases => "No saved atlases yet";
        public override string ChooseSavedAtlas => "Choose a saved atlas";
        public override string SavedAtlasesEmpty => "Every atlas you generate is kept here, with its source, destination and settings.";
        public override string AtlasNotInList => "This destination isn't in the list yet: it's added when you generate.";
        public override string AtlasChanged(string changes) => $"Changed since the last generation: {changes}. Generating updates the saved atlas.";
        public override string SpriteCount(int count) => count == 1 ? "1 sprite" : $"{count} sprites";
        public override string Generated(string when) => "generated " + when;
        public override string StatusAtlasLoaded(string name) => $"Atlas \"{name}\" loaded.";
        public override string StatusReverted(string name) => $"Restored the values from the last generation of \"{name}\".";
        public override string ConfirmRemove(string name, string files) =>
            $"Remove \"{name}\" from the saved atlases?\n\nThe files {files} stay where they are.";
        public override string SavedListFailed(string message) => $"Couldn't update the list of saved atlases.\n\n{message}";
        public override string StatusRemoved(string name) => $"\"{name}\" was removed from the saved atlases.";
        public override string StatusNewAtlas => "New atlas: choose the source (or drag it onto the window), the destination and the name.";
        public override string AddedToSaved(string name) => $"\"{name}\" was added to the saved atlases.";
        public override string SavedListUpdateFailed(string message) => $"Couldn't update the list of saved atlases: {message}";

        public override string BadgeKrita => "KRITA";
        public override string BadgeFolder => "FOLDER";
        public override string DetailsSource(string path) => "Source: " + path;

        public override string ChangeSource => "source";
        public override string ChangePadding => "sprite padding";
        public override string ChangeBorder => "border padding";
        public override string ChangeExtrude => "extrude";
        public override string ChangeMaxSize => "maximum size";
        public override string ChangePowerOfTwo => "power of two";
        public override string ChangeSquare => "square atlas";
        public override string ChangeHidden => "hidden layers";
        public override string ChangeFormat => "sheet format";

        public override string SectionInput => "INPUT";
        public override string ReadKrita => "Read straight from Krita";
        public override string ReadKritaHint => ".kra or .krz, no Batch Exporter needed: layers named like name-name become sprites";
        public override string IgnoreHidden => "Ignore hidden layers";
        public override string IgnoreHiddenHint => "layers with the eye closed in Krita (or inside a hidden group) are left out";
        public override string InputLabelKrita => "Krita file";
        public override string InputLabelFolder => "Image folder";
        public override string Browse => "Browse";
        public override string BrowseFolderTitle => "Select the folder with the images";
        public override string BrowseKritaTitle => "Select the Krita file";
        public override string KritaFilter => "Krita files (*.kra, *.krz)|*.kra;*.krz";
        public override string StatusUsingNearbyKrita(string file) => $"Using {file}, found next to the atlas. Check that it's the right file.";

        public override string FolderPrompt => "Select a folder or drag it here. Subfolders are included.";
        public override string FolderMissing => "That folder doesn't exist.";
        public override string FolderNoImages => "No supported images found in that folder.";
        public override string FolderImageCount(int count) =>
            count == 1 ? "1 image found, including subfolders." : $"{count} images found, including subfolders.";
        public override string FolderReadFailed(string message) => $"Couldn't read the folder: {message}";

        public override string KritaPrompt => "Select the .kra or .krz, or drag it here. What counts is what's saved on disk: save in Krita before generating.";
        public override string NotKritaFile => "That isn't a Krita file (.kra or .krz).";
        public override string FileMissing => "That file doesn't exist.";
        public override string KritaNoSpriteLayers => "No layer is named in the name-name pattern (e.g. menu-button-play).";
        public override string KritaSpriteCount(int count) => count == 1 ? "1 layer becomes a sprite" : $"{count} layers become sprites";
        public override string KritaSaved(string when) => $" · file saved {when}.";
        public override string KritaIgnored(string layers) => $"\nSkipped: {layers}.";
        public override string KritaHidden(string layers) => $"\nHidden, left out: {layers}.";
        public override string KritaProblem(string path, string reason) => $"\nHeads up, {path}: {reason}.";
        public override string KritaReadFailed(string message) => $"Couldn't read the Krita file: {message}";
        public override string LayersBecomeSprites => "Become sprites:";
        public override string LayersHidden => "Hidden:";
        public override string LayersIgnored => "Skipped:";
        public override string NoLayers => "The file has no layers.";
        public override string AndMore(string list, int more) => $"{list} and {more} more";

        public override string StatusKritaSet(string path) => $"Krita file set: {path}";
        public override string StatusFolderSet(string path) => $"Source folder set: {path}";

        public override string SectionOutput => "OUTPUT";
        public override string OutputFolder => "Destination folder";
        public override string BrowseOutputTitle => "Select where to save the atlas";
        public override string AtlasName => "Atlas name";
        public override string SheetFormat => "Sheet";
        public override string SheetFormatHint =>
            "the Atlasmith importer for Unity reads the .atlasmith.json; TexturePacker Importer only accepts .tpsheet files made by TexturePacker";
        public override string FormatAtlasmith => "Atlasmith (.atlasmith.json)";
        public override string FormatTpSheet => "TexturePacker (.tpsheet)";
        public override string FormatBoth => "Both";
        public override string OutputSummary(string files) => $"Writes {files}.";
        public override string JoinAnd(IReadOnlyList<string> items) =>
            items.Count <= 1 ? string.Join("", items) : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];

        public override string SectionPacking => "PACKING";
        public override string Padding => "Sprite padding";
        public override string PaddingHint => "empty pixels between one sprite and the next";
        public override string Border => "Border padding";
        public override string BorderHint => "empty space along the atlas edges";
        public override string Extrude => "Extrude";
        public override string ExtrudeHint => "repeats the sprite edge outward to avoid bleeding";
        public override string MaxSize => "Maximum size";
        public override string PowerOfTwo => "Power-of-two size";
        public override string PowerOfTwoHint => "512, 1024, 2048… required by most engines";
        public override string Square => "Force square atlas";
        public override string SquareHint => "when off, the most balanced shape that fits is chosen";
        public override string StorageNote => "Saved atlases, settings and paths are kept in %AppData%\\Atlasmith.";

        public override string Generate => "Generate atlas";
        public override string Generating => "Generating…";
        public override string ChooseExistingKrita => "Choose a Krita file (.kra or .krz) that exists.";
        public override string SourceMustBeKrita => "The source must be a Krita file (.kra or .krz).";
        public override string ChooseExistingFolder => "Choose a source folder that exists.";
        public override string ChooseOutputFolder => "Choose the folder where the atlas will be saved.";
        public override string NameInvalidChar(char c) => $"The atlas name can't contain the character '{c}'.";
        public override string ConfirmSourceChange(string name, string before, string now) =>
            $"The atlas \"{name}\" was generated from:\n{before}\n\nThe source is now:\n{now}\n\nGenerate and replace its files?";
        public override string ConfirmStaleSheet(string stale, string written) =>
            $"There is a {stale} next to the atlas that would be out of date: the new atlas comes with {written}.\n\n" +
            "Yes — delete the old file.\nNo — keep both (the old one keeps describing the previous atlas).\n\n" +
            "In a Unity project that uses TexturePacker Importer, convert with the Atlasmith package in Unity first: " +
            "it keeps the sprite references.";
        public override string FormatFollowsDisk(string file) => $"The atlas already uses {file}: the sheet stays in that format.";
        public override string ConfirmTexturePackerSheet(string file) =>
            $"{file} was made by TexturePacker (or by an Atlasmith beta). In Unity, that file is read by " +
            "TexturePacker Importer, which refuses sheets written by other programs: the PNG would change and the " +
            "sprites would stay stuck to the previous atlas.\n\n" +
            "First, convert the atlas in Unity with the Atlasmith package (Tools → Atlasmith → Convert TexturePacker Sheets…). " +
            "After that, Atlasmith writes an .atlasmith.json for it.\n\n" +
            "Write the .tpsheet anyway?";
        public override string DeletedStale(string file) => $"Deleted {file} (out of date).";
        public override string SpritesLeaving(string names) => $"Leaving the current atlas: {names}.";
        public override string ConfirmRemovedSprites(int count, string names, string? hidden) =>
            (count == 1 ? $"1 sprite in the current atlas will be removed: {names}." : $"{count} sprites in the current atlas will be removed: {names}.") +
            (hidden is null ? "" : $"\n\nThey are on hidden Krita layers: {hidden}. To keep them, turn off “Ignore hidden layers” or make those layers visible.") +
            "\n\nIn Unity, anything using these sprites (prefabs, scenes, animations) loses its reference. Generate anyway?";
        public override string StatusCancelled => "Generation cancelled: nothing was written.";

        public override string StatusLoadingImages => "Loading images";
        public override string StatusReadingLayers => "Reading layers";
        public override string ReadingImagesFrom(string path) => $"Reading images from {path}";
        public override string ImagesLoaded(int count) => count == 1 ? "1 image loaded." : $"{count} images loaded.";
        public override string NoImagesFound => "No supported image was found.";
        public override string NoImagesFoundDetail => "No supported image was found in that folder.";
        public override string StatusPacking => "Finding the smallest atlas…";
        public override string StatusComposing => "Building the image…";
        public override string StatusSaving => "Saving…";
        public override string StatusGenerated(string files) => $"Done — {files} written.";
        public override string StatusRightOccupancy(int w, int h, double occupancy) =>
            string.Format(Culture, "{0}×{1} · {2:0.0}% used", w, h, occupancy * 100);
        public override string ErrorLog(string message) => $"ERROR: {message}";
        public override string StatusFailed => "Failed.";
        public override string GenerateFailed(string message) => $"Couldn't generate the atlas.\n\n{message}";
        public override string Ready => "Ready.";

        public override string MetricSize => "SIZE";
        public override string MetricOccupancy => "OCCUPANCY";
        public override string MetricSprites => "SPRITES";
        public override string MetricTime => "TIME";
        public override string OutlinesTip => "Show each sprite's outline";
        public override string Fit => "Fit";
        public override string OpenFolder => "Open folder";
        public override string EmptyTitle => "No atlas generated yet";
        public override string EmptyHint => "Choose the source (folder or Krita file) and click Generate atlas.";
        public override string StatusRightOnDisk(string file, string when) => $"{file} on disk · saved {when}";

        public override string JustNow => "just now";
        public override string MinutesAgo(int minutes) => $"{minutes} min ago";
        public override string HoursAgo(int hours) => $"{hours} h ago";
        public override string Today => "today";
        public override string Yesterday => "yesterday";
        public override string DaysAgo(int days) => $"{days} days ago";
        public override string OnDate(DateTime date) => "on " + date.ToString("MMM d, yyyy", Culture);

        public override string UnexpectedError(string chain, string logPath) =>
            $"An unexpected error occurred:\n\n{chain}\nFull details in:\n{logPath}";

        public override string InstallTitle => "Install Atlasmith";
        public override string UpdateTitle => "Update Atlasmith";
        public override string InstallIntro => "Builds sprite atlases straight from Krita files, for Unity or any engine.";
        public override string InstallLocation => "Installs for your user, without administrator rights, in:";
        public override string DesktopShortcut => "Desktop shortcut";
        public override string StartMenuShortcut => "Start menu shortcut";
        public override string LaunchAfterInstall => "Open Atlasmith when done";
        public override string InstallButton => "Install";
        public override string UpdateButton => "Update";
        public override string Cancel => "Cancel";
        public override string Installing => "Installing…";
        public override string InstallDone => "Done: Atlasmith is installed. It shows up in Settings → Apps, where it can also be uninstalled.";
        public override string InstallFailed(string message) => $"Couldn't install Atlasmith.\n\n{message}";
        public override string CloseRunningApp => "The installed Atlasmith is open. Close it and try again.";
        public override string UninstallTitle => "Uninstall Atlasmith";
        public override string UninstallConfirm(string dataFolder) =>
            $"Remove Atlasmith from this computer?\n\nGenerated atlases are not touched, and the settings stay in {dataFolder}.";
        public override string UninstallDone => "Atlasmith was removed.";
        public override string UninstallFailed(string message) => $"Couldn't fully remove Atlasmith.\n\n{message}";
    }
}

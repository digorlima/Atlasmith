using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Atlasmith.Core;
using Atlasmith.Localization;
using Atlasmith.Services;
// System.Windows.Input também tem um InputMode (do gerenciador de entrada do WPF).
using InputMode = Atlasmith.Services.InputMode;

namespace Atlasmith;

public partial class MainWindow : Window
{
    private static Texts T => Texts.Current;

    private readonly AppConfig _config = App.Config;
    private readonly StringBuilder _log = new();
    private readonly int[] _maxSizes = [512, 1024, 2048, 4096, 8192, 16384];
    private readonly SheetFormat[] _formats = [SheetFormat.Atlasmith, SheetFormat.TpSheet, SheetFormat.Both];

    private bool _initializing = true;
    private bool _busy;
    private string? _lastOutputFolder;

    // Ligado enquanto o código preenche os campos, para que os eventos de edição
    // não confundam isso com uma alteração feita pelo usuário.
    private bool _syncingUi;

    private List<AtlasProfile> _atlases = [];

    // Leituras em segundo plano levam um número; a que voltar atrasada é descartada.
    private int _previewVersion;
    private int _inputSummaryVersion;

    private RectI[]? _outlines;
    private int _atlasW, _atlasH;
    private Metrics? _metrics;
    private Func<string>? _statusRight;

    private Point _panStart;
    private bool _panning;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        Activated += OnActivated;
        ThemeManager.EffectiveThemeChanged += _ => ThemeManager.ApplyWindowChrome(this, ThemeManager.IsDark);
        Texts.Changed += OnTextsChanged;

        VersionBadge.Text = AtlasmithInfo.Version.EndsWith(".0", StringComparison.Ordinal)
            ? AtlasmithInfo.Version[..^2]
            : AtlasmithInfo.Version;

        RestoreWindowPlacement();
        LoadConfigIntoUi();
        _initializing = false;

        SetStatus(T.Ready);
        ReloadAtlases();
        UpdateOutputSummary();
        RefreshInputSummary();
        _ = ShowSavedAtlasAsync(_config.OutputFolder, _config.OutputName);
    }

    // ------------------------------------------------------------------
    // Ciclo de vida da janela
    // ------------------------------------------------------------------

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ThemeManager.ApplyWindowChrome(this, ThemeManager.IsDark);

        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        source.AddHook(WindowProc);
    }

    /// <summary>
    /// Ao voltar para a janela — depois de salvar no Krita, por exemplo — o arquivo e
    /// a lista de atlas (que outra janela do Atlasmith pode ter mudado) são relidos.
    /// </summary>
    private void OnActivated(object? sender, EventArgs e)
    {
        if (_initializing) return;
        ReloadAtlases();
        RefreshInputSummary();
    }

    private void RestoreWindowPlacement()
    {
        Width = _config.WindowWidth;
        Height = _config.WindowHeight;

        // Só restaura a posição se a barra de título ainda cair em algum monitor: o
        // segundo monitor pode ter sido desligado desde a última vez.
        if (_config.WindowLeft is double savedLeft && _config.WindowTop is double savedTop &&
            TitleBarOnSomeMonitor(savedLeft, savedTop, Width))
        {
            Left = savedLeft;
            Top = savedTop;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }

        if (_config.WindowMaximized) WindowState = WindowState.Maximized;
    }

    /// <summary>Um pedaço da barra de título (onde se arrasta a janela) aparece em algum monitor?</summary>
    private static bool TitleBarOnSomeMonitor(double left, double top, double width)
    {
        // Antes de existir, a janela mede em DIPs na escala do sistema.
        double scale = NativeMethods.GetDpiForSystem() / 96.0;
        var strip = new NativeMethods.Rect
        {
            Left = (int)((left + 40) * scale),
            Top = (int)((top + 4) * scale),
            Right = (int)((left + Math.Max(80, Math.Min(width, 400) - 40)) * scale),
            Bottom = (int)((top + 40) * scale)
        };
        return NativeMethods.MonitorFromRect(ref strip, NativeMethods.MonitorDefaultToNull) != IntPtr.Zero;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _config.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            _config.WindowWidth = Width;
            _config.WindowHeight = Height;
            _config.WindowLeft = Left;
            _config.WindowTop = Top;
        }
        _config.Save();
        Texts.Changed -= OnTextsChanged;
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestore(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        // Alterna o desenho do botão entre "maximizar" e "restaurar".
        bool maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = TryFindResource(maximized ? "Icon.Restore" : "Icon.Maximize");
        MaximizeButton.SetResourceReference(ToolTipProperty, maximized ? "T.Restore" : "T.Maximize");
    }

    // ------------------------------------------------------------------
    // Configuração <-> UI
    // ------------------------------------------------------------------

    private void LoadConfigIntoUi()
    {
        ShowRecipe();
        ThemeCombo.SelectedIndex = (int)_config.Theme;
        LanguageCombo.SelectedIndex = (int)_config.Language;
    }

    /// <summary>Preenche, a partir do config, os campos que formam a receita de um atlas.</summary>
    private void ShowRecipe() => Quietly(() =>
    {
        KraToggle.IsChecked = _config.InputMode == InputMode.Krita;
        UpdateInputLabel();
        InputBox.Text = _config.InputPath;
        IgnoreHiddenToggle.IsChecked = _config.IgnoreHiddenLayers;
        OutputBox.Text = _config.OutputFolder;
        NameBox.Text = _config.OutputName;
        FormatCombo.SelectedIndex = Math.Max(0, Array.IndexOf(_formats, _config.SheetFormat));

        PaddingBox.Value = _config.Padding;
        BorderBox.Value = _config.BorderPadding;
        ExtrudeBox.Value = _config.Extrude;
        PowerOfTwoToggle.IsChecked = _config.PowerOfTwo;
        SquareToggle.IsChecked = _config.ForceSquare;

        int sizeIndex = Array.IndexOf(_maxSizes, _config.MaxSize);
        MaxSizeCombo.SelectedIndex = sizeIndex >= 0 ? sizeIndex : 3;
    });

    private void CaptureUiIntoConfig()
    {
        // O campo de entrada mostra o caminho do modo atual; o do outro modo fica guardado.
        string input = PathText.Clean(InputBox.Text);
        if (_config.InputMode == InputMode.Krita) _config.InputKraFile = input;
        else _config.InputFolder = input;

        _config.IgnoreHiddenLayers = IgnoreHiddenToggle.IsChecked == true;
        _config.OutputFolder = PathText.Clean(OutputBox.Text);
        _config.OutputName = string.IsNullOrWhiteSpace(NameBox.Text) ? "Atlas" : NameBox.Text.Trim();
        _config.SheetFormat = _formats[Math.Clamp(FormatCombo.SelectedIndex, 0, _formats.Length - 1)];

        _config.Padding = PaddingBox.Value;
        _config.BorderPadding = BorderBox.Value;
        _config.Extrude = ExtrudeBox.Value;
        _config.PowerOfTwo = PowerOfTwoToggle.IsChecked == true;
        _config.ForceSquare = SquareToggle.IsChecked == true;

        int idx = Math.Clamp(MaxSizeCombo.SelectedIndex, 0, _maxSizes.Length - 1);
        _config.MaxSize = _maxSizes[idx];
    }

    /// <summary>Muda campos por código sem que os eventos de edição reajam.</summary>
    private void Quietly(Action change)
    {
        bool previous = _syncingUi;
        _syncingUi = true;
        try
        {
            change();
        }
        finally
        {
            _syncingUi = previous;
        }
    }

    // Assinaturas separadas por tipo de evento: uma sobrecarga só deixaria a
    // ligação do XAML ambígua entre RoutedEventHandler e EventHandler.
    private void OnToggleSetting(object sender, RoutedEventArgs e) => PersistSettings();

    private void OnComboSetting(object sender, SelectionChangedEventArgs e)
    {
        PersistSettings();
        if (ReferenceEquals(sender, FormatCombo)) UpdateOutputSummary();
    }

    private void OnNumberSetting(object? sender, EventArgs e) => PersistSettings();

    private void PersistSettings()
    {
        if (_initializing || _syncingUi) return;
        CaptureUiIntoConfig();
        _config.Save();
        UpdateAtlasState();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        var mode = (AppTheme)Math.Clamp(ThemeCombo.SelectedIndex, 0, 2);
        _config.Theme = mode;
        ThemeManager.Mode = mode;
        _config.Save();
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        _config.Language = (AppLanguage)Math.Clamp(LanguageCombo.SelectedIndex, 0, 2);
        _config.Save();
        Texts.Apply(_config.Language);
    }

    /// <summary>O XAML se atualiza sozinho; aqui são refeitos os textos montados no código.</summary>
    private void OnTextsChanged()
    {
        UpdateInputLabel();
        ReloadAtlases();
        UpdateOutputSummary();
        RefreshInputSummary();
        ShowMetrics(_metrics);
        SetStatusRight(_statusRight);
        if (!_busy) SetStatus(T.Ready);
    }

    // ------------------------------------------------------------------
    // Atlas salvos
    // ------------------------------------------------------------------

    private void ReloadAtlases()
    {
        _atlases = AtlasLibrary.Load();
        // ItemsSource novo também refaz os textos dos itens (origem, data) no idioma atual.
        Quietly(() => AtlasCombo.ItemsSource = _atlases);
        AtlasCombo.IsEnabled = _atlases.Count > 0 && !_busy;
        UpdateAtlasState();
    }

    /// <summary>Marca na lista o atlas do destino atual e diz o que gerar vai fazer com ele.</summary>
    private void UpdateAtlasState()
    {
        var current = _config.ToProfile();
        var match = _atlases.FirstOrDefault(p => p.Targets(current.OutputFolder, current.Name));

        Quietly(() => AtlasCombo.SelectedItem = match);
        RemoveAtlasButton.IsEnabled = match is not null;
        RevertAtlasButton.Visibility = Visibility.Collapsed;

        if (_atlases.Count == 0)
        {
            AtlasCombo.Tag = T.NoSavedAtlases;
            AtlasSummary.Text = T.SavedAtlasesEmpty;
            return;
        }

        AtlasCombo.Tag = T.ChooseSavedAtlas;

        if (match is null)
        {
            AtlasSummary.Text = T.AtlasNotInList;
            return;
        }

        var changes = match.DifferencesFrom(current);
        if (changes.Count > 0)
        {
            AtlasSummary.Text = T.AtlasChanged(string.Join(", ", changes));
            RevertAtlasButton.Visibility = Visibility.Visible;
            return;
        }

        AtlasSummary.Text = DescribeSavedAtlas(match);
    }

    private static string DescribeSavedAtlas(AtlasProfile atlas)
    {
        var parts = new List<string>();
        if (atlas.SpriteCount > 0) parts.Add(T.SpriteCount(atlas.SpriteCount));
        if (atlas.AtlasWidth > 0) parts.Add($"{atlas.AtlasWidth}×{atlas.AtlasHeight}");
        parts.Add(T.Generated(RelativeTime.Describe(atlas.LastGenerated)));
        return string.Join(" · ", parts) + ".";
    }

    private void OnAtlasSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || _syncingUi) return;
        if (AtlasCombo.SelectedItem is not AtlasProfile atlas) return;

        // O log era da geração de outro atlas.
        ClearLog();
        LoadAtlas(atlas);
        SetStatus(T.StatusAtlasLoaded(atlas.Name));
    }

    private void LoadAtlas(AtlasProfile atlas)
    {
        _config.Apply(atlas);
        ShowRecipe();
        _config.Save();

        UpdateOutputSummary();
        RefreshInputSummary();
        UpdateAtlasState();
        _ = ShowSavedAtlasAsync(atlas.OutputFolder, atlas.Name);
    }

    private void OnRevertAtlas(object sender, RoutedEventArgs e)
    {
        if (AtlasCombo.SelectedItem is not AtlasProfile atlas) return;

        LoadAtlas(atlas);
        SetStatus(T.StatusReverted(atlas.Name));
    }

    private void OnRemoveAtlas(object sender, RoutedEventArgs e)
    {
        if (AtlasCombo.SelectedItem is not AtlasProfile atlas) return;

        var answer = MessageBox.Show(
            this,
            T.ConfirmRemove(atlas.Name, T.JoinAnd(atlas.OutputFiles)),
            AtlasmithInfo.Name,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            AtlasLibrary.Remove(atlas);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn(T.SavedListFailed(ex.Message));
            return;
        }

        ReloadAtlases();
        SetStatus(T.StatusRemoved(atlas.Name));
    }

    private void OnNewAtlas(object sender, RoutedEventArgs e)
    {
        // Modo de entrada, margens e a opção das camadas desativadas ficam: costumam
        // se repetir de um atlas para o outro. A planilha volta para a do Atlasmith.
        _config.InputFolder = "";
        _config.InputKraFile = "";
        Quietly(() =>
        {
            InputBox.Text = "";
            OutputBox.Text = "";
            NameBox.Text = "";
            FormatCombo.SelectedIndex = 0;
        });

        PersistSettings();
        UpdateOutputSummary();
        RefreshInputSummary();
        ClearPreview();
        ClearLog();
        InputBox.Focus();
        SetStatus(T.StatusNewAtlas);
    }

    // ------------------------------------------------------------------
    // Entrada: pasta de imagens ou arquivo do Krita
    // ------------------------------------------------------------------

    // Checked/Unchecked, e não Click: leitores de tela e automação mudam o estado sem clicar.
    private void OnInputModeToggled(object sender, RoutedEventArgs e)
    {
        if (_initializing || _syncingUi) return;
        SetInputMode(KraToggle.IsChecked == true ? InputMode.Krita : InputMode.Folder);

        // Primeira vez no modo Krita: um único .kra/.krz por perto (ao lado da pasta
        // de export, dentro dela ou no destino) quase certamente é o do atlas.
        if (_config.InputMode == InputMode.Krita && _config.InputKraFile.Length == 0 &&
            FindNearbyKritaFile() is { } found)
        {
            UseKritaFile(found);
            SetStatus(T.StatusUsingNearbyKrita(Path.GetFileName(found)));
        }
    }

    private void OnIgnoreHiddenToggled(object sender, RoutedEventArgs e)
    {
        if (_initializing || _syncingUi) return;
        PersistSettings();
        RefreshInputSummary();
    }

    /// <summary>Troca o modo de entrada. Cada modo lembra o próprio caminho.</summary>
    private void SetInputMode(InputMode mode)
    {
        if (_config.InputMode != mode)
        {
            CaptureUiIntoConfig();
            _config.InputMode = mode;
            Quietly(() => InputBox.Text = _config.InputPath);
        }

        Quietly(() => KraToggle.IsChecked = mode == InputMode.Krita);
        UpdateInputLabel();
        PersistSettings();
        RefreshInputSummary();
    }

    private void UpdateInputLabel()
    {
        bool krita = _config.InputMode == InputMode.Krita;
        InputLabel.Text = krita ? T.InputLabelKrita : T.InputLabelFolder;
        HiddenLayersRow.Visibility = krita ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Usa um .kra/.krz como origem e completa destino e nome que estiverem em branco.</summary>
    private void UseKritaFile(string path)
    {
        if (_config.InputMode != InputMode.Krita) SetInputMode(InputMode.Krita);

        Quietly(() =>
        {
            InputBox.Text = path;

            if (PathText.Clean(OutputBox.Text).Length == 0)
                OutputBox.Text = Path.GetDirectoryName(path) ?? "";

            string name = NameBox.Text.Trim();
            if (name.Length == 0 || name.Equals("Atlas", StringComparison.OrdinalIgnoreCase))
            {
                string folder = PathText.Clean(OutputBox.Text);
                string suggested = AtlasNaming.Suggest(path, folder);
                NameBox.Text = suggested;

                // Atlas que já existe continua no formato em que está.
                if (AtlasNaming.ExistingFormat(folder, suggested) is { } format)
                    FormatCombo.SelectedIndex = Array.IndexOf(_formats, format);
            }
        });

        PersistSettings();
        UpdateOutputSummary();
        RefreshInputSummary();
    }

    /// <summary>
    /// O arquivo do Krita da pasta-mãe da entrada (onde ele fica quando a entrada é a
    /// pasta de export), da própria entrada ou do destino — se for o único da pasta.
    /// Autosaves do Krita (".nome-autosave.kra") não contam.
    /// </summary>
    private string? FindNearbyKritaFile()
    {
        var folders = new List<string>();
        if (_config.InputFolder.Length > 0)
        {
            if (Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(_config.InputFolder)) is { Length: > 0 } parent)
                folders.Add(parent);
            folders.Add(_config.InputFolder);
        }
        if (_config.OutputFolder.Length > 0) folders.Add(_config.OutputFolder);

        foreach (string folder in folders)
        {
            try
            {
                if (!Directory.Exists(folder)) continue;

                var found = Directory.EnumerateFiles(folder)
                    .Where(f => KraReader.IsKritaFile(f) && !KraReader.IsAutosave(f))
                    .Take(2)
                    .ToList();

                if (found.Count == 1) return found[0];
                if (found.Count > 1) return null; // mais de um: melhor o usuário escolher
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Pasta inacessível: tenta a próxima.
            }
        }

        return null;
    }

    private void OnBrowseInput(object sender, RoutedEventArgs e)
    {
        if (_config.InputMode == InputMode.Krita) BrowseKritaFile();
        else BrowseInputFolder();
    }

    private void BrowseInputFolder()
    {
        string current = PathText.Clean(InputBox.Text);
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = T.BrowseFolderTitle,
            InitialDirectory = Directory.Exists(current) ? current : ""
        };

        if (dialog.ShowDialog(this) == true)
        {
            Quietly(() => InputBox.Text = dialog.FolderName);
            PersistSettings();
            RefreshInputSummary();
        }
    }

    private void BrowseKritaFile()
    {
        string current = PathText.Clean(InputBox.Text);
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = T.BrowseKritaTitle,
            Filter = T.KritaFilter,
            // Sem arquivo ainda, abre perto da pasta de export ou do destino: o .kra costuma morar ali.
            InitialDirectory = FirstExistingFolder(
                current.Length > 0 ? Path.GetDirectoryName(current) : null,
                _config.InputFolder.Length > 0 ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(_config.InputFolder)) : null,
                _config.OutputFolder) ?? ""
        };

        if (dialog.ShowDialog(this) == true) UseKritaFile(dialog.FileName);
    }

    private static string? FirstExistingFolder(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && Directory.Exists(c));

    private void OnBrowseOutput(object sender, RoutedEventArgs e)
    {
        string current = PathText.Clean(OutputBox.Text);
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = T.BrowseOutputTitle,
            InitialDirectory = Directory.Exists(current) ? current : ""
        };

        if (dialog.ShowDialog(this) == true)
        {
            Quietly(() => OutputBox.Text = dialog.FolderName);
            PersistSettings();
            UpdateOutputSummary();
        }
    }

    private void OnInputPathEdited(object sender, RoutedEventArgs e)
    {
        string text = PathText.Clean(InputBox.Text);

        // Um arquivo do Krita colado vale como escolhido no Procurar: completa destino e
        // nome em branco e, se estava no modo pasta, troca o modo junto.
        if (KraReader.IsKritaFile(text) && File.Exists(text) && !PathText.Same(text, _config.InputKraFile))
        {
            if (_config.InputMode == InputMode.Folder) Quietly(() => InputBox.Text = _config.InputFolder);
            UseKritaFile(text);
            return;
        }

        if (_config.InputMode == InputMode.Krita && Directory.Exists(text))
        {
            Quietly(() => InputBox.Text = _config.InputKraFile);
            SetInputMode(InputMode.Folder);
        }

        if (text != InputBox.Text) Quietly(() => InputBox.Text = text);
        PersistSettings();
        RefreshInputSummary();
    }

    private void OnOutputPathEdited(object sender, RoutedEventArgs e)
    {
        string text = PathText.Clean(OutputBox.Text);
        if (text != OutputBox.Text) Quietly(() => OutputBox.Text = text);
        PersistSettings();
        UpdateOutputSummary();
    }

    private void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        PersistSettings();
        UpdateOutputSummary();
    }

    private void UpdateOutputSummary()
    {
        var profile = _config.ToProfile();
        profile.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Atlas" : NameBox.Text.Trim();
        profile.SheetFormat = _formats[Math.Clamp(FormatCombo.SelectedIndex, 0, _formats.Length - 1)];
        OutputSummary.Text = T.OutputSummary(T.JoinAnd(profile.OutputFiles));

        // Os campos são estreitos demais para um caminho completo; o tooltip mostra o resto.
        string folder = PathText.Clean(OutputBox.Text);
        OutputBox.ToolTip = folder.Length == 0
            ? null
            : Path.Combine(folder, profile.Name + ".png");
        ShowPathEnd(OutputBox);
    }

    /// <summary>
    /// Caminho longo não cabe no campo: rola até o fim, onde estão a pasta e o
    /// arquivo que interessam. Só fora do foco, para não atrapalhar quem digita.
    /// </summary>
    private void ShowPathEnd(TextBox box)
    {
        if (box.IsKeyboardFocusWithin) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!box.IsKeyboardFocusWithin) box.ScrollToHorizontalOffset(double.MaxValue);
        }), DispatcherPriority.Loaded);
    }

    private void RefreshInputSummary()
    {
        int version = ++_inputSummaryVersion;
        string path = PathText.Clean(InputBox.Text);
        InputBox.ToolTip = path.Length == 0 ? null : path;
        InputSummary.ToolTip = null;
        ShowPathEnd(InputBox);

        if (_config.InputMode == InputMode.Krita)
        {
            _ = RefreshKritaSummaryAsync(path, version);
            return;
        }

        if (path.Length == 0)
        {
            InputSummary.Text = T.FolderPrompt;
            return;
        }

        if (!Directory.Exists(path))
        {
            InputSummary.Text = T.FolderMissing;
            return;
        }

        try
        {
            int count = ImageLoader.FindImages(path).Length;
            InputSummary.Text = count == 0 ? T.FolderNoImages : T.FolderImageCount(count);
        }
        catch (Exception ex)
        {
            InputSummary.Text = T.FolderReadFailed(ex.Message);
        }
    }

    private async Task RefreshKritaSummaryAsync(string path, int version)
    {
        if (path.Length == 0)
        {
            InputSummary.Text = T.KritaPrompt;
            return;
        }

        if (!KraReader.IsKritaFile(path))
        {
            InputSummary.Text = T.NotKritaFile;
            return;
        }

        if (!File.Exists(path))
        {
            InputSummary.Text = T.FileMissing;
            return;
        }

        var options = new KraReadOptions { IgnoreHiddenLayers = _config.IgnoreHiddenLayers };
        try
        {
            // Só o maindoc.xml é lido: rápido o bastante para rodar a cada volta à janela.
            var (plan, written) = await Task.Run(() => (KraReader.Inspect(path, options), File.GetLastWriteTime(path)));
            if (version != _inputSummaryVersion) return;

            InputSummary.Text = DescribeKritaPlan(plan, written);
            InputSummary.ToolTip = DescribeKritaLayers(plan);
        }
        catch (Exception ex)
        {
            if (version != _inputSummaryVersion) return;
            InputSummary.Text = T.KritaReadFailed(ex.Message);
        }
    }

    private static string DescribeKritaPlan(KraPlan plan, DateTime written)
    {
        var sb = new StringBuilder();
        int count = plan.Sprites.Count;

        if (count == 0)
            sb.Append(T.KritaNoSpriteLayers);
        else
            sb.Append(T.KritaSpriteCount(count)).Append(T.KritaSaved(RelativeTime.Describe(written)));

        var ignored = SkippedPaths(plan, KraSkipKind.OutOfPattern);
        if (ignored.Count > 0) sb.Append(T.KritaIgnored(JoinShort(ignored, 4)));

        var hidden = SkippedPaths(plan, KraSkipKind.Hidden);
        if (hidden.Count > 0) sb.Append(T.KritaHidden(JoinShort(hidden, 4)));

        // Camadas com nome de sprite que mesmo assim ficam de fora quase sempre são engano.
        foreach (var problem in plan.Skipped.Where(s => s.Kind == KraSkipKind.Problem))
            sb.Append(T.KritaProblem(problem.LayerPath, problem.Reason));

        return sb.ToString();
    }

    private static List<string> SkippedPaths(KraPlan plan, KraSkipKind kind) =>
        plan.Skipped.Where(s => s.Kind == kind).Select(s => s.LayerPath).ToList();

    private static string DescribeKritaLayers(KraPlan plan)
    {
        var sb = new StringBuilder();

        void Section(string title, IEnumerable<string> lines)
        {
            var list = lines.ToList();
            if (list.Count == 0) return;
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(title);
            foreach (string line in list) sb.Append("\n   ").Append(line);
        }

        Section(T.LayersBecomeSprites, plan.Sprites.Select(s => s.LayerPath));
        Section(T.LayersHidden, plan.Skipped.Where(s => s.Kind == KraSkipKind.Hidden).Select(s => s.LayerPath));
        Section(T.LayersIgnored, plan.Skipped.Where(s => s.Kind != KraSkipKind.Hidden).Select(s => $"{s.LayerPath} — {s.Reason}"));

        return sb.Length == 0 ? T.NoLayers : sb.ToString();
    }

    /// <summary>"a, b, c e mais 4": lista curta para caber numa dica.</summary>
    private static string JoinShort(IReadOnlyList<string> items, int max) =>
        items.Count <= max
            ? string.Join(", ", items)
            : T.AndMore(string.Join(", ", items.Take(max)), items.Count - max);

    // ------------------------------------------------------------------
    // Arrastar e soltar
    // ------------------------------------------------------------------

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = GetDroppedInput(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        var dropped = GetDroppedInput(e);
        if (dropped is null) return;

        OpenSource(dropped.Value.Path);
    }

    /// <summary>Usa como origem um arquivo do Krita ou uma pasta: solto na janela ou passado ao abrir o app.</summary>
    public void OpenSource(string path)
    {
        if (KraReader.IsKritaFile(path))
        {
            UseKritaFile(path);
            SetStatus(T.StatusKritaSet(path));
            return;
        }

        SetInputMode(InputMode.Folder);
        Quietly(() => InputBox.Text = path);
        PersistSettings();
        RefreshInputSummary();
        SetStatus(T.StatusFolderSet(path));
    }

    private static (string Path, InputMode Mode)? GetDroppedInput(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return null;

        string first = paths[0];
        if (Directory.Exists(first)) return (first, InputMode.Folder);
        if (KraReader.IsKritaFile(first)) return (first, InputMode.Krita);

        // Soltar um arquivo de imagem aponta para a pasta que o contém.
        return ImageLoader.IsSupported(first) && Path.GetDirectoryName(first) is { } folder
            ? (folder, InputMode.Folder)
            : null;
    }

    // ------------------------------------------------------------------
    // Geração
    // ------------------------------------------------------------------

    private void OnGenerate(object sender, RoutedEventArgs e) => _ = GenerateAsync();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Key == Key.G && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            _ = GenerateAsync();
        }
    }

    private async Task GenerateAsync()
    {
        if (_busy) return;

        CaptureUiIntoConfig();
        _config.Save();

        // A receita fica fixa aqui: mexer nos campos durante a geração não muda o que é gerado.
        var recipe = _config.ToProfile();
        bool fromKrita = recipe.InputMode == InputMode.Krita;
        string input = recipe.InputPath;
        string outFolder = recipe.OutputFolder;
        string name = recipe.Name;

        if (fromKrita)
        {
            if (input.Length == 0 || !File.Exists(input))
            {
                Warn(T.ChooseExistingKrita);
                return;
            }

            if (!KraReader.IsKritaFile(input))
            {
                Warn(T.SourceMustBeKrita);
                return;
            }
        }
        else if (input.Length == 0 || !Directory.Exists(input))
        {
            Warn(T.ChooseExistingFolder);
            return;
        }

        if (string.IsNullOrWhiteSpace(outFolder))
        {
            Warn(T.ChooseOutputFolder);
            return;
        }

        if (AtlasNaming.InvalidCharIn(name) is char invalid)
        {
            Warn(T.NameInvalidChar(invalid));
            return;
        }

        // Atlas da beta que já foi convertido no Unity: continuar gravando .tpsheet
        // devolveria a textura ao TexturePacker Importer. A planilha segue o que está em disco.
        string? formatNote = null;
        if (recipe.SheetFormat == SheetFormat.TpSheet &&
            AtlasNaming.ExistingFormat(outFolder, name) == SheetFormat.Atlasmith)
        {
            recipe.SheetFormat = SheetFormat.Atlasmith;
            _config.SheetFormat = SheetFormat.Atlasmith;
            Quietly(() => FormatCombo.SelectedIndex = 0);
            _config.Save();
            UpdateOutputSummary();
            formatNote = T.FormatFollowsDisk(name + AtlasSheet.Extension);
        }

        var settings = _config.ToPackSettings();
        var request = new AtlasRequest(outFolder, name, settings)
        {
            Format = recipe.SheetFormat,
            Source = AtlasSource.For(
                fromKrita ? AtlasSourceKind.Krita : AtlasSourceKind.Folder,
                input, outFolder, recipe.IgnoreHiddenLayers)
        };

        if (!ConfirmSourceChange(recipe, request)) return;
        if (!ConfirmTexturePackerSheet(request)) return;
        if (!ConfirmStaleSheets(request, out var staleToDelete)) return;

        SetBusy(true);
        ClearLog();
        _previewVersion++; // um atlas salvo ainda carregando não deve cobrir o novo
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (formatNote is not null) AppendLog(formatNote);

            var report = await LoadSpritesAsync(recipe);
            if (report.Sprites.Length > 0)
                AppendLog(fromKrita ? CoreText.Current.LayersRead(report.Sprites.Length) : T.ImagesLoaded(report.Sprites.Length));

            if (report.Sprites.Length > 0 && !await ConfirmRemovedSpritesAsync(recipe, request, report))
            {
                SetStatus(T.StatusCancelled);
                return;
            }

            // O núcleo roda fora da thread da interface; log e etapas voltam pelo Dispatcher.
            var outcome = await Task.Run(() => AtlasBuilder.Build(
                report,
                request,
                line => Dispatcher.BeginInvoke(() => AppendLog(line)),
                stage => Dispatcher.BeginInvoke(() => ShowStage(stage))));

            if (outcome.Status == AtlasBuildStatus.NoSprites)
            {
                if (report.Failures.Length > 0)
                {
                    AppendLog(CoreText.Current.NoSpriteCouldBeRead);
                    Warn(CoreText.Current.NoSpriteCouldBeRead);
                }
                else if (fromKrita)
                {
                    AppendLog(CoreText.Current.NoLayerBecameSprite);
                    Warn(CoreText.Current.NoLayerBecameSpriteDetail);
                }
                else
                {
                    AppendLog(T.NoImagesFound);
                    Warn(T.NoImagesFoundDetail);
                }
                SetStatus(T.StatusFailed);
                return;
            }

            if (outcome.Status == AtlasBuildStatus.DoesNotFit)
            {
                Warn(CoreText.Current.DoesNotFit(settings.MaxSize));
                SetStatus(T.StatusFailed);
                return;
            }

            DeleteStaleSheets(staleToDelete);
            stopwatch.Stop();

            var pack = outcome.Pack!;
            ShowPreview(
                Bitmaps.FromBgra(outcome.Pixels!, pack.Width, pack.Height),
                pack.Width, pack.Height,
                pack.Placements.Select(p => new RectI(p.X, p.Y, p.W, p.H)).ToArray());

            _lastOutputFolder = outFolder;
            OpenFolderButton.IsEnabled = true;

            ShowMetrics(new Metrics(pack.Width, pack.Height, report.Sprites.Length, pack.Occupancy, stopwatch.ElapsedMilliseconds));

            RememberAtlas(recipe, report.Sprites.Length, pack);

            SetStatus(T.StatusGenerated(T.JoinAnd(recipe.OutputFiles)));
            SetStatusRight(() => T.StatusRightOccupancy(pack.Width, pack.Height, pack.Occupancy));
        }
        catch (Exception ex)
        {
            AppendLog(T.ErrorLog(ex.Message));
            SetStatus(T.StatusFailed);
            Warn(T.GenerateFailed(ex.Message));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowStage(AtlasBuildStage stage)
    {
        switch (stage)
        {
            case AtlasBuildStage.Packing:
                SetStatus(T.StatusPacking);
                Progress.IsIndeterminate = true;
                break;
            case AtlasBuildStage.Composing:
                SetStatus(T.StatusComposing);
                break;
            case AtlasBuildStage.Saving:
                SetStatus(T.StatusSaving);
                break;
        }
    }

    private Task<LoadReport> LoadSpritesAsync(AtlasProfile recipe)
    {
        bool fromKrita = recipe.InputMode == InputMode.Krita;
        string label = fromKrita ? T.StatusReadingLayers : T.StatusLoadingImages;

        Action<int, int> progress = (done, total) => Dispatcher.BeginInvoke(new Action(() =>
        {
            Progress.Value = total == 0 ? 0 : done * 100.0 / total;
            SetStatus($"{label}… {done}/{total}");
        }));

        if (fromKrita)
        {
            AppendLog(CoreText.Current.ReadingLayers(recipe.InputKraFile));
            var options = new KraReadOptions { IgnoreHiddenLayers = recipe.IgnoreHiddenLayers };
            return Task.Run(() => KraReader.LoadSprites(recipe.InputKraFile, options, progress));
        }

        AppendLog(T.ReadingImagesFrom(recipe.InputFolder));
        return Task.Run(() => ImageLoader.LoadFolder(recipe.InputFolder, progress));
    }

    /// <summary>
    /// Pede confirmação antes de gravar por cima de um atlas salvo que vinha de outra
    /// origem. O caso que isso pega: trocar o arquivo do Krita e esquecer de mudar o nome.
    /// </summary>
    private bool ConfirmSourceChange(AtlasProfile recipe, AtlasRequest request)
    {
        var saved = _atlases.FirstOrDefault(p => p.Targets(recipe.OutputFolder, recipe.Name));
        if (saved is null) return true;
        if (saved.InputMode == recipe.InputMode && PathText.Same(saved.InputPath, recipe.InputPath)) return true;
        if (!File.Exists(request.PngPath) && !File.Exists(request.SheetPath) && !File.Exists(request.TpSheetPath)) return true;

        var answer = MessageBox.Show(
            this,
            T.ConfirmSourceChange(saved.Name, saved.InputPath, recipe.InputPath),
            AtlasmithInfo.Name,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        return answer == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Sprite que sai de um atlas existente quebra, no Unity, tudo que o usa. Antes de
    /// gravar, a lista aparece — e, lendo do Krita, quais saem só por estarem desativados.
    /// </summary>
    private async Task<bool> ConfirmRemovedSpritesAsync(AtlasProfile recipe, AtlasRequest request, LoadReport report)
    {
        var removed = await Task.Run(() => request.RemovedSprites(report));
        if (removed.Count == 0) return true;

        var hidden = new List<string>();
        if (recipe.InputMode == InputMode.Krita && recipe.IgnoreHiddenLayers)
        {
            try
            {
                var options = new KraReadOptions { IgnoreHiddenLayers = true };
                var names = await Task.Run(() => KraReader.Inspect(recipe.InputKraFile, options).HiddenSpriteNames());
                hidden.AddRange(removed.Where(names.Contains));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException
                                           or UnauthorizedAccessException)
            {
                // Sem a lista das ocultas, a pergunta ainda vale.
            }
        }

        AppendLog(T.SpritesLeaving(JoinShort(removed, 12)));
        var answer = MessageBox.Show(
            this,
            T.ConfirmRemovedSprites(removed.Count, JoinShort(removed, 12), hidden.Count > 0 ? JoinShort(hidden, 12) : null),
            AtlasmithInfo.Name,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return answer == MessageBoxResult.Yes;
    }

    /// <summary>
    /// O .tpsheet em disco é lido pelo TexturePacker Importer, que recusa planilhas de
    /// outros programas: gravar por cima deixaria os sprites do Unity presos ao atlas
    /// anterior. O caminho certo é converter o atlas no Unity antes.
    /// </summary>
    private bool ConfirmTexturePackerSheet(AtlasRequest request)
    {
        if (!request.WritesTpSheet || !TpSheet.IsFromAnotherTool(request.TpSheetPath)) return true;

        var answer = MessageBox.Show(
            this,
            T.ConfirmTexturePackerSheet(Path.GetFileName(request.TpSheetPath)),
            AtlasmithInfo.Name,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer == MessageBoxResult.Yes) return true;
        SetStatus(T.StatusCancelled);
        return false;
    }

    /// <summary>
    /// A planilha do outro formato, com o mesmo nome, ficaria descrevendo o atlas
    /// anterior. Sim apaga, Não mantém, Cancelar desiste de gerar.
    /// </summary>
    private bool ConfirmStaleSheets(AtlasRequest request, out List<string> toDelete)
    {
        toDelete = [];
        var stale = request.StaleSheets();
        if (stale.Length == 0) return true;

        var written = new List<string>();
        if (request.WritesAtlasmith) written.Add(Path.GetFileName(request.SheetPath));
        if (request.WritesTpSheet) written.Add(Path.GetFileName(request.TpSheetPath));

        var answer = MessageBox.Show(
            this,
            T.ConfirmStaleSheet(T.JoinAnd(stale.Select(Path.GetFileName).ToList()!), T.JoinAnd(written)),
            AtlasmithInfo.Name,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (answer == MessageBoxResult.Cancel || answer == MessageBoxResult.None) return false;
        if (answer == MessageBoxResult.Yes) toDelete.AddRange(stale);
        return true;
    }

    /// <summary>Apaga as planilhas antigas (e o .meta delas, se o destino estiver num projeto Unity).</summary>
    private void DeleteStaleSheets(List<string> paths)
    {
        foreach (string path in paths)
        {
            try
            {
                File.Delete(path);
                if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
                AppendLog(T.DeletedStale(Path.GetFileName(path)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppendLog(T.ErrorLog(ex.Message));
            }
        }
    }

    /// <summary>Guarda (ou atualiza) o atlas na lista de salvos.</summary>
    private void RememberAtlas(AtlasProfile recipe, int spriteCount, PackResult result)
    {
        bool isNew = !_atlases.Any(p => p.Targets(recipe.OutputFolder, recipe.Name));

        recipe.LastGenerated = DateTime.Now;
        recipe.SpriteCount = spriteCount;
        recipe.AtlasWidth = result.Width;
        recipe.AtlasHeight = result.Height;

        try
        {
            AtlasLibrary.Record(recipe);
            if (isNew) AppendLog(T.AddedToSaved(recipe.Name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppendLog(T.SavedListUpdateFailed(ex.Message));
        }

        ReloadAtlases();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        GenerateButton.IsEnabled = !busy;
        GenerateButton.SetResourceReference(ContentProperty, busy ? "T.Generating" : "T.Generate");
        // Trocar de atlas no meio da geração deixaria o preview mostrando um e a lista outro.
        AtlasCombo.IsEnabled = !busy && _atlases.Count > 0;
        NewAtlasButton.IsEnabled = !busy;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Progress.IsIndeterminate = false;
        Progress.Value = 0;
        Cursor = busy ? Cursors.AppStarting : Cursors.Arrow;
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, AtlasmithInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);

    private void SetStatus(string text) => StatusText.Text = text;

    /// <summary>O canto direito guarda como montar o texto, para remontá-lo na troca de idioma.</summary>
    private void SetStatusRight(Func<string>? text)
    {
        _statusRight = text;
        StatusRight.Text = text?.Invoke() ?? "";
    }

    private void ClearLog()
    {
        _log.Clear();
        LogText.Text = "";
        SetStatusRight(null);
    }

    private void AppendLog(string line)
    {
        _log.AppendLine(line);
        LogText.Text = _log.ToString().TrimEnd();
        LogScroll.ScrollToEnd();
    }

    // ------------------------------------------------------------------
    // Pré-visualização
    // ------------------------------------------------------------------

    private void ShowPreview(ImageSource bitmap, int width, int height, RectI[] outlines)
    {
        _outlines = outlines;
        _atlasW = width;
        _atlasH = height;

        AtlasImage.Source = bitmap;
        AtlasImage.Width = width;
        AtlasImage.Height = height;
        EmptyState.Visibility = Visibility.Collapsed;

        BuildOutlines();

        // Na abertura da janela o viewport ainda não tem tamanho: enquadra depois do layout.
        if (Viewport.ActualWidth > 0) ZoomToFit();
        else Dispatcher.BeginInvoke(new Action(ZoomToFit), DispatcherPriority.Loaded);
    }

    private void ClearPreview()
    {
        _previewVersion++;
        _outlines = null;
        _atlasW = _atlasH = 0;

        AtlasImage.Source = null;
        OutlineLayer.Data = null;
        EmptyState.Visibility = Visibility.Visible;

        ShowMetrics(null);
        SetStatusRight(null);
        _lastOutputFolder = null;
        OpenFolderButton.IsEnabled = false;
    }

    /// <summary>Números do atlas mostrado; ficam guardados para a troca de idioma refazer o separador decimal.</summary>
    private sealed record Metrics(int Width, int Height, int? Sprites, double? Occupancy, long? Milliseconds);

    private void ShowMetrics(Metrics? metrics)
    {
        _metrics = metrics;
        MetricSize.Text = metrics is null ? "—" : $"{metrics.Width}×{metrics.Height}";
        MetricCount.Text = metrics?.Sprites?.ToString(T.Culture) ?? "—";
        MetricOccupancy.Text = metrics?.Occupancy is double occupancy
            ? (occupancy * 100).ToString("0.0", T.Culture) + "%"
            : "—";
        MetricTime.Text = metrics?.Milliseconds is long ms ? ms.ToString(T.Culture) + " ms" : "—";
    }

    private sealed record SavedAtlas(BitmapSource Bitmap, AtlasSheet? Sheet, DateTime Written);

    /// <summary>Mostra no preview o atlas que já está em disco nesse destino, se houver.</summary>
    private async Task ShowSavedAtlasAsync(string folder, string name)
    {
        int version = ++_previewVersion;

        SavedAtlas? saved = null;
        if (PathText.Clean(folder).Length > 0)
            saved = await Task.Run(() => LoadSavedAtlas(folder, name));

        if (version != _previewVersion) return;

        if (saved is null)
        {
            ClearPreview();
            return;
        }

        int w = saved.Bitmap.PixelWidth;
        int h = saved.Bitmap.PixelHeight;

        // Os retângulos só valem se a planilha descrever um atlas do mesmo tamanho do PNG.
        RectI[] rects = saved.Sheet is { } sheet && sheet.Width == w && sheet.Height == h
            ? sheet.Sprites.Select(s => s.Rect).ToArray()
            : [];

        ShowPreview(saved.Bitmap, w, h, rects);
        _lastOutputFolder = folder;
        OpenFolderButton.IsEnabled = true;

        ShowMetrics(rects.Length > 0
            ? new Metrics(w, h, rects.Length, rects.Sum(r => (double)r.Area) / ((double)w * h), null)
            : new Metrics(w, h, null, null, null));
        SetStatusRight(() => T.StatusRightOnDisk(name + ".png", RelativeTime.Describe(saved.Written)));
    }

    private static SavedAtlas? LoadSavedAtlas(string folder, string name)
    {
        try
        {
            string pngPath = Path.Combine(folder, name + ".png");
            if (!File.Exists(pngPath)) return null;

            BitmapSource bitmap;
            // OnLoad e FileShare amplo: o PNG não fica preso, e o Unity ou uma nova geração podem regravá-lo.
            using (var stream = new FileStream(pngPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                bitmap = decoder.Frames[0];
            }
            bitmap.Freeze();

            return new SavedAtlas(bitmap, AtlasNaming.TryReadSheet(folder, name), File.GetLastWriteTime(pngPath));
        }
        catch (Exception)
        {
            // O preview é cortesia: PNG ilegível ou no meio de uma gravação simplesmente não aparece.
            return null;
        }
    }

    private void BuildOutlines()
    {
        if (_outlines is null) return;

        var group = new GeometryGroup();
        foreach (var r in _outlines)
            group.Children.Add(new RectangleGeometry(new Rect(r.X + 0.5, r.Y + 0.5, r.W, r.H)));
        group.Freeze();

        OutlineLayer.Data = group;
    }

    private void OnToggleOutlines(object sender, RoutedEventArgs e)
    {
        OutlineLayer.Visibility = OutlineToggle.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnZoomFit(object sender, RoutedEventArgs e) => ZoomToFit();

    private void OnZoomActual(object sender, RoutedEventArgs e)
    {
        if (_atlasW == 0) return;
        SetZoom(1.0);
        CenterPreview();
    }

    private void ZoomToFit()
    {
        if (_atlasW == 0 || Viewport.ActualWidth <= 0) return;

        double scale = Math.Min(
            (Viewport.ActualWidth - 32) / _atlasW,
            (Viewport.ActualHeight - 32) / _atlasH);

        SetZoom(Math.Clamp(scale, 0.02, 8));
        CenterPreview();
    }

    private void SetZoom(double scale)
    {
        PreviewScale.ScaleX = scale;
        PreviewScale.ScaleY = scale;
        // O contorno deve manter 1px na tela independentemente do zoom.
        OutlineLayer.StrokeThickness = 1 / Math.Max(scale, 0.0001);
    }

    private void CenterPreview()
    {
        PreviewTranslate.X = (Viewport.ActualWidth - _atlasW * PreviewScale.ScaleX) / 2;
        PreviewTranslate.Y = (Viewport.ActualHeight - _atlasH * PreviewScale.ScaleY) / 2;
    }

    private void OnViewportWheel(object sender, MouseWheelEventArgs e)
    {
        if (_atlasW == 0) return;

        Point cursor = e.GetPosition(Viewport);
        double old = PreviewScale.ScaleX;
        double factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        double next = Math.Clamp(old * factor, 0.02, 32);
        if (Math.Abs(next - old) < 1e-6) return;

        // Mantém sob o cursor o mesmo ponto do atlas antes e depois do zoom.
        double atlasX = (cursor.X - PreviewTranslate.X) / old;
        double atlasY = (cursor.Y - PreviewTranslate.Y) / old;

        SetZoom(next);
        PreviewTranslate.X = cursor.X - atlasX * next;
        PreviewTranslate.Y = cursor.Y - atlasY * next;

        e.Handled = true;
    }

    private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_atlasW == 0) return;
        _panning = true;
        _panStart = e.GetPosition(Viewport);
        Viewport.CaptureMouse();
        Viewport.Cursor = Cursors.ScrollAll;
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning) return;

        Point now = e.GetPosition(Viewport);
        PreviewTranslate.X += now.X - _panStart.X;
        PreviewTranslate.Y += now.Y - _panStart.Y;
        _panStart = now;
    }

    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        Viewport.ReleaseMouseCapture();
        Viewport.Cursor = Cursors.Arrow;
    }

    private void OnOpenOutputFolder(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastOutputFolder) || !Directory.Exists(_lastOutputFolder)) return;
        Process.Start(new ProcessStartInfo(_lastOutputFolder) { UseShellExecute = true });
    }

    // ------------------------------------------------------------------
    // Maximizar sem cobrir a barra de tarefas
    // ------------------------------------------------------------------

    private const int WmGetMinMaxInfo = 0x0024;

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo) return IntPtr.Zero;

        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return IntPtr.Zero;

        var info = new NativeMethods.MonitorInfo { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

        var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MinMaxInfo>(lParam);
        mmi.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
        mmi.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;
        mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
        mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
        System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);

        handled = true;
        return IntPtr.Zero;
    }
}

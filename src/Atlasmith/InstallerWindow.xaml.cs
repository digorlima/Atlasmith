using System.IO;
using System.Windows;
using Atlasmith.Core;
using Atlasmith.Localization;
using Atlasmith.Services;

namespace Atlasmith;

/// <summary>A janela do setup: onde instala, quais atalhos criar e se abre o app no fim.</summary>
public partial class InstallerWindow : Window
{
    private static Texts T => Texts.Current;

    private readonly InstallLayout _layout;
    private readonly bool _update;
    private bool _done;

    public InstallerWindow(InstallLayout layout, bool? desktop, bool? startMenu, bool? launch)
    {
        InitializeComponent();
        _layout = layout;
        _update = Installer.IsInstalled(layout);

        SourceInitialized += (_, _) => ThemeManager.ApplyWindowChrome(this, ThemeManager.IsDark);
        ThemeManager.EffectiveThemeChanged += OnThemeChanged;
        Closed += (_, _) => ThemeManager.EffectiveThemeChanged -= OnThemeChanged;

        VersionBadge.Text = AtlasmithInfo.Version.EndsWith(".0", StringComparison.Ordinal)
            ? AtlasmithInfo.Version[..^2]
            : AtlasmithInfo.Version;
        LocationText.Text = layout.InstallFolder;

        // Numa atualização, as opções começam como a instalação atual está.
        var current = Installer.CurrentChoices(layout);
        DesktopToggle.IsChecked = desktop ?? current.DesktopShortcut;
        StartMenuToggle.IsChecked = startMenu ?? current.StartMenuShortcut;
        LaunchToggle.IsChecked = launch ?? true;

        HeadingText.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, _update ? "T.UpdateTitle" : "T.InstallTitle");
        InstallButton.SetResourceReference(ContentProperty, _update ? "T.UpdateButton" : "T.InstallButton");
    }

    private void OnThemeChanged(bool dark) => ThemeManager.ApplyWindowChrome(this, dark);

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        if (_done)
        {
            Close();
            return;
        }

        var choices = new InstallChoices
        {
            DesktopShortcut = DesktopToggle.IsChecked == true,
            StartMenuShortcut = StartMenuToggle.IsChecked == true
        };

        SetBusy(true);
        try
        {
            // Fora da thread da interface: copiar o .exe leva um instante. O ShellLink
            // do Windows aceita qualquer apartamento COM, então os atalhos vão junto.
            await Task.Run(() => Installer.Install(_layout, choices));
        }
        catch (Exception ex) when (ex is InstallerException or IOException or UnauthorizedAccessException
                                       or System.Runtime.InteropServices.COMException)
        {
            SetBusy(false);
            string message = ex is InstallerException ? ex.Message : T.InstallFailed(ex.Message);
            MessageBox.Show(this, message, AtlasmithInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (LaunchToggle.IsChecked == true && !_layout.IsTest)
        {
            Installer.Launch(_layout);
            Close();
            return;
        }

        _done = true;
        SetBusy(false);
        StatusText.Text = T.InstallDone;
        StatusText.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Collapsed;
        InstallButton.SetResourceReference(ContentProperty, "T.CloseWindow");
    }

    private void SetBusy(bool busy)
    {
        InstallButton.IsEnabled = !busy;
        CancelButton.IsEnabled = !busy;
        DesktopToggle.IsEnabled = StartMenuToggle.IsEnabled = LaunchToggle.IsEnabled = !busy;
        if (busy) InstallButton.SetResourceReference(ContentProperty, "T.Installing");
        else if (!_done) InstallButton.SetResourceReference(ContentProperty, _update ? "T.UpdateButton" : "T.InstallButton");
        Cursor = busy ? System.Windows.Input.Cursors.AppStarting : null;
    }
}

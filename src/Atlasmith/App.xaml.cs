using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Atlasmith.Core;
using Atlasmith.Localization;
using Atlasmith.Services;

namespace Atlasmith;

public partial class App : Application
{
    public static AppConfig Config { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = StartupArgs.Parse(e.Args, Installer.CurrentExe);

        // Antes de tudo: não lê nem grava configuração, só confere o executável.
        if (args.SelfCheck)
        {
            Shutdown(SelfCheck.Run(args.ReportPath));
            return;
        }

        Config = AppConfig.Load();
        Texts.Apply(Config.Language);
        ThemeManager.Initialize(Config.Theme);
        DispatcherUnhandledException += OnUnhandledException;

        var layout = args.TestRoot is { } root ? InstallLayout.ForTest(root) : InstallLayout.ForUser();

        if (args.Uninstall)
        {
            Shutdown(Uninstall(layout, args.Quiet));
            return;
        }

        if (args.Install)
        {
            if (args.Quiet)
            {
                Shutdown(InstallQuietly(layout, args));
                return;
            }

            MainWindow = new InstallerWindow(layout, args.Desktop, args.StartMenu, args.Launch);
            MainWindow.Show();
            return;
        }

        AtlasLibrary.SeedFrom(Config);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (args.OpenPath is { } path) window.OpenSource(path);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeManager.Shutdown();
        base.OnExit(e);
    }

    private static int InstallQuietly(InstallLayout layout, StartupArgs args)
    {
        var current = Installer.CurrentChoices(layout);
        var choices = new InstallChoices
        {
            DesktopShortcut = args.Desktop ?? current.DesktopShortcut,
            StartMenuShortcut = args.StartMenu ?? current.StartMenuShortcut
        };

        try
        {
            Installer.Install(layout, choices);
            return 0;
        }
        catch (Exception ex) when (ex is InstallerException or IOException or UnauthorizedAccessException
                                       or System.Runtime.InteropServices.COMException)
        {
            WriteCrashLog(ex);
            return 1;
        }
    }

    private static int Uninstall(InstallLayout layout, bool quiet)
    {
        var t = Texts.Current;
        if (!quiet)
        {
            var answer = MessageBox.Show(
                t.UninstallConfirm(AppConfig.Directory),
                t.UninstallTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return 1;
        }

        try
        {
            Installer.Uninstall(layout);
        }
        catch (Exception ex) when (ex is InstallerException or IOException or UnauthorizedAccessException
                                       or System.Security.SecurityException)
        {
            if (!quiet)
            {
                string message = ex is InstallerException ? ex.Message : t.UninstallFailed(ex.Message);
                MessageBox.Show(message, t.UninstallTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return 1;
        }

        if (!quiet) MessageBox.Show(t.UninstallDone, t.UninstallTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        return 0;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A mensagem de topo do WPF costuma ser genérica ("Provide value on ...
        // threw an exception"); o que interessa está nas exceções internas.
        string chain = DescribeChain(e.Exception);
        WriteCrashLog(e.Exception);

        MessageBox.Show(
            Texts.Current.UnexpectedError(chain, CrashLogPath),
            AtlasmithInfo.Name,
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    private static string CrashLogPath => Path.Combine(AppConfig.Directory, "last-error.log");

    private static string DescribeChain(Exception exception)
    {
        var sb = new StringBuilder();
        for (Exception? current = exception; current is not null; current = current.InnerException)
            sb.AppendLine($"{current.GetType().Name}: {current.Message}");
        return sb.ToString();
    }

    private static void WriteCrashLog(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppConfig.Directory);
            File.WriteAllText(
                CrashLogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n{exception}");
        }
        catch
        {
            // Sem log em disco, resta a mensagem na tela.
        }
    }
}

/// <summary>
/// A linha de comando. Sem argumentos abre o app; um .kra/.krz ou pasta passado
/// (arrastado sobre o atalho, "Abrir com") já vira a origem.
///
///   --install [--quiet] [--no-desktop] [--no-start-menu] [--no-launch]
///   --uninstall [--quiet]
///   --self-check [relatório]
///   --test-root pasta   instala/desinstala numa pasta e chave de registro descartáveis
/// </summary>
internal sealed class StartupArgs
{
    public bool SelfCheck { get; private set; }
    public string? ReportPath { get; private set; }
    public bool Install { get; private set; }
    public bool Uninstall { get; private set; }
    public bool Quiet { get; private set; }
    public bool? Desktop { get; private set; }
    public bool? StartMenu { get; private set; }
    public bool? Launch { get; private set; }
    public string? TestRoot { get; private set; }
    public string? OpenPath { get; private set; }

    public static StartupArgs Parse(string[] args, string exePath)
    {
        var result = new StartupArgs { Install = Installer.IsSetupExe(exePath) };

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string? Next() => i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : null;

            switch (arg.ToLowerInvariant())
            {
                case "--self-check":
                    result.SelfCheck = true;
                    result.ReportPath = Next();
                    break;
                case "--install":
                    result.Install = true;
                    break;
                case "--uninstall":
                    result.Uninstall = true;
                    break;
                case "--quiet":
                case "/s":
                    result.Quiet = true;
                    break;
                case "--no-desktop":
                    result.Desktop = false;
                    break;
                case "--no-start-menu":
                    result.StartMenu = false;
                    break;
                case "--no-launch":
                    result.Launch = false;
                    break;
                case "--test-root":
                    result.TestRoot = Next() is { } root ? Path.GetFullPath(root) : null;
                    break;
                default:
                    string path = PathText.Clean(arg);
                    if (result.OpenPath is null && (Directory.Exists(path) || (File.Exists(path) && KraReader.IsKritaFile(path))))
                        result.OpenPath = Path.GetFullPath(path);
                    break;
            }
        }

        // Desinstalar vence: o setup aberto com --uninstall não deve instalar antes.
        if (result.Uninstall) result.Install = false;
        return result;
    }
}

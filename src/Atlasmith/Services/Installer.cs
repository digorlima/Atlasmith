using System.Diagnostics;
using System.IO;
using Atlasmith.Core;
using Atlasmith.Localization;
using Microsoft.Win32;

namespace Atlasmith.Services;

/// <summary>
/// Onde a instalação mora. A instalação é por usuário, como a de VS Code e Discord:
/// nada de administrador, e o Windows a lista em Configurações → Aplicativos.
///
/// O modo de teste (--test-root) manda pasta, atalhos e chave do registro para
/// lugares descartáveis, para conferir o instalador sem mexer no sistema.
/// </summary>
public sealed class InstallLayout
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Atlasmith";
    public const string TestRegistryRoot = @"Software\AtlasmithTest";

    public required string InstallFolder { get; init; }
    public required string DesktopFolder { get; init; }
    public required string StartMenuFolder { get; init; }

    /// <summary>Subchave de HKEY_CURRENT_USER com a entrada de desinstalação.</summary>
    public required string RegistryKey { get; init; }

    public bool IsTest { get; init; }

    public string InstalledExe => Path.Combine(InstallFolder, Installer.ExeName);
    public string DesktopShortcut => Path.Combine(DesktopFolder, "Atlasmith.lnk");
    public string StartMenuShortcut => Path.Combine(StartMenuFolder, "Atlasmith.lnk");

    public static InstallLayout ForUser() => new()
    {
        InstallFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Atlasmith"),
        DesktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        StartMenuFolder = Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        RegistryKey = UninstallKey
    };

    public static InstallLayout ForTest(string root) => new()
    {
        InstallFolder = Path.Combine(root, "Programs", "Atlasmith"),
        DesktopFolder = Path.Combine(root, "Desktop"),
        StartMenuFolder = Path.Combine(root, "StartMenu"),
        RegistryKey = TestRegistryRoot + @"\Uninstall\Atlasmith",
        IsTest = true
    };
}

public sealed class InstallChoices
{
    public bool DesktopShortcut { get; set; } = true;
    public bool StartMenuShortcut { get; set; } = true;
}

/// <summary>Erro já explicado para o usuário (o texto vai direto para a mensagem).</summary>
public sealed class InstallerException(string message) : Exception(message);

/// <summary>
/// O próprio Atlasmith.exe é o instalador: copiado com "setup" no nome (ou aberto
/// com --install), ele se copia para a pasta do usuário, cria os atalhos escolhidos
/// e se registra para poder ser desinstalado pelo Windows.
/// </summary>
public static class Installer
{
    public const string ExeName = "Atlasmith.exe";

    public static string CurrentExe => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, ExeName);

    /// <summary>Quem baixa "Atlasmith-1.0.0-setup.exe" e dá dois cliques quer instalar.</summary>
    public static bool IsSetupExe(string exePath) =>
        Path.GetFileNameWithoutExtension(exePath).Contains("setup", StringComparison.OrdinalIgnoreCase);

    public static bool IsInstalled(InstallLayout layout) => File.Exists(layout.InstalledExe);

    /// <summary>A versão registrada na instalação existente, se houver.</summary>
    public static string? InstalledVersion(InstallLayout layout)
    {
        using var key = Registry.CurrentUser.OpenSubKey(layout.RegistryKey);
        return key?.GetValue("DisplayVersion") as string;
    }

    /// <summary>Os atalhos da instalação existente: numa atualização, as opções começam como estavam.</summary>
    public static InstallChoices CurrentChoices(InstallLayout layout)
    {
        if (!IsInstalled(layout)) return new InstallChoices();

        return new InstallChoices
        {
            DesktopShortcut = File.Exists(layout.DesktopShortcut),
            StartMenuShortcut = File.Exists(layout.StartMenuShortcut)
        };
    }

    /// <summary>Uma cópia instalada aberta (fora este processo) impede substituir o .exe.</summary>
    public static bool InstalledCopyIsRunning(InstallLayout layout)
    {
        int self = Environment.ProcessId;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
        {
            using (process)
            {
                try
                {
                    if (process.Id != self && process.MainModule?.FileName is { } path &&
                        PathText.Same(path, layout.InstalledExe))
                        return true;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Processo que já saiu ou de outro usuário: não é a nossa cópia.
                }
            }
        }
        return false;
    }

    public static void Install(InstallLayout layout, InstallChoices choices)
    {
        if (InstalledCopyIsRunning(layout)) throw new InstallerException(Texts.Current.CloseRunningApp);

        Directory.CreateDirectory(layout.InstallFolder);

        // Rodando de dentro da instalação (Atlasmith.exe --install), não há o que copiar.
        if (!PathText.Same(CurrentExe, layout.InstalledExe))
        {
            // Grava ao lado e troca no fim: uma cópia interrompida não estraga a instalação anterior.
            string temp = layout.InstalledExe + ".new";
            File.Copy(CurrentExe, temp, overwrite: true);
            File.Move(temp, layout.InstalledExe, overwrite: true);
        }

        SetShortcut(layout.DesktopShortcut, choices.DesktopShortcut, layout.InstalledExe);
        SetShortcut(layout.StartMenuShortcut, choices.StartMenuShortcut, layout.InstalledExe);
        Register(layout, choices);
    }

    private static void SetShortcut(string path, bool wanted, string target)
    {
        if (wanted)
            ShellLink.Create(path, target, Texts.Current.InstallIntro);
        else if (File.Exists(path))
            File.Delete(path);
    }

    private static void Register(InstallLayout layout, InstallChoices choices)
    {
        using var key = Registry.CurrentUser.CreateSubKey(layout.RegistryKey);
        string exe = layout.InstalledExe;

        key.SetValue("DisplayName", AtlasmithInfo.Name);
        key.SetValue("DisplayVersion", AtlasmithInfo.Version);
        key.SetValue("Publisher", "Daigo");
        key.SetValue("DisplayIcon", exe + ",0");
        key.SetValue("InstallLocation", layout.InstallFolder);
        key.SetValue("UninstallString", $"\"{exe}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{exe}\" --uninstall --quiet");
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
        key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

        // Só para a desinstalação saber o que apagar; o Windows ignora valores desconhecidos.
        key.SetValue("DesktopShortcut", choices.DesktopShortcut ? layout.DesktopShortcut : "");
        key.SetValue("StartMenuShortcut", choices.StartMenuShortcut ? layout.StartMenuShortcut : "");
    }

    /// <summary>
    /// Remove atalhos, registro e a pasta do programa. Configurações e atlas gerados
    /// ficam. Quando quem desinstala é o próprio .exe instalado (o caso normal, vindo
    /// de Configurações → Aplicativos), a pasta é apagada logo depois que ele fechar.
    /// </summary>
    public static void Uninstall(InstallLayout layout)
    {
        if (InstalledCopyIsRunning(layout)) throw new InstallerException(Texts.Current.CloseRunningApp);

        var shortcuts = new List<string> { layout.DesktopShortcut, layout.StartMenuShortcut };
        using (var key = Registry.CurrentUser.OpenSubKey(layout.RegistryKey))
        {
            foreach (string name in new[] { "DesktopShortcut", "StartMenuShortcut" })
            {
                if (key?.GetValue(name) is string { Length: > 0 } path) shortcuts.Add(path);
            }
        }

        foreach (string shortcut in shortcuts.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // Só apaga atalho que aponta para esta instalação.
            if (ShellLink.ReadTarget(shortcut) is { } target && PathText.Same(target, layout.InstalledExe))
                File.Delete(shortcut);
        }

        Registry.CurrentUser.DeleteSubKeyTree(layout.RegistryKey, throwOnMissingSubKey: false);
        if (layout.IsTest) DeleteEmptyTestKeys();

        RemoveInstallFolder(layout);
    }

    private static void RemoveInstallFolder(InstallLayout layout)
    {
        string folder = Path.TrimEndingDirectorySeparator(layout.InstallFolder);
        if (!Directory.Exists(folder)) return;

        // Rede de segurança: só some uma pasta chamada Atlasmith que tenha o nosso .exe.
        if (!Path.GetFileName(folder).Equals("Atlasmith", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(layout.InstalledExe))
            return;

        bool runningFromInside = CurrentExe.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!runningFromInside)
        {
            Directory.Delete(folder, recursive: true);
            return;
        }

        // Um .exe em uso não pode ser apagado: um cmd espera este processo fechar e apaga a pasta.
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = $"/d /c ping -n 3 127.0.0.1 >nul & rd /s /q \"{folder}\"",
            WorkingDirectory = Path.GetTempPath(),
            CreateNoWindow = true,
            UseShellExecute = false
        })?.Dispose();
    }

    private static void DeleteEmptyTestKeys()
    {
        using (var uninstall = Registry.CurrentUser.OpenSubKey(InstallLayout.TestRegistryRoot + @"\Uninstall"))
        {
            if (uninstall is null || uninstall.SubKeyCount > 0 || uninstall.ValueCount > 0) return;
        }
        Registry.CurrentUser.DeleteSubKeyTree(InstallLayout.TestRegistryRoot, throwOnMissingSubKey: false);
    }

    /// <summary>Abre o Atlasmith instalado, fora da pasta de instalação (para não prendê-la).</summary>
    public static void Launch(InstallLayout layout) =>
        Process.Start(new ProcessStartInfo(layout.InstalledExe)
        {
            UseShellExecute = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        })?.Dispose();
}

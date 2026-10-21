using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

using Atlasmith.Core;

namespace Atlasmith.Services;

/// <summary>
/// Troca a paleta em tempo de execução e acompanha a preferência de tema do Windows.
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static AppTheme _mode = AppTheme.System;
    private static bool _hooked;

    /// <summary>Disparado quando o tema efetivo muda (inclusive por mudança no Windows).</summary>
    public static event Action<bool>? EffectiveThemeChanged;

    /// <summary>True quando a paleta em uso é a escura.</summary>
    public static bool IsDark { get; private set; }

    public static AppTheme Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            Apply();
        }
    }

    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            if (key?.GetValue("AppsUseLightTheme") is int light) return light == 0;
        }
        catch
        {
            // Chave ausente (builds antigas do Windows): assume claro.
        }
        return false;
    }

    public static void Initialize(AppTheme mode)
    {
        _mode = mode;

        if (!_hooked)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _hooked = true;
        }

        Apply();
    }

    public static void Shutdown()
    {
        if (!_hooked) return;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _hooked = false;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        if (_mode != AppTheme.System) return;

        Application.Current?.Dispatcher.BeginInvoke(new Action(Apply));
    }

    private static void Apply()
    {
        var app = Application.Current;
        if (app is null) return;

        bool dark = _mode switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => SystemPrefersDark()
        };

        string uri = dark ? "Themes/Dark.xaml" : "Themes/Light.xaml";
        var dict = new ResourceDictionary { Source = new Uri(uri, UriKind.Relative) };

        // O dicionário de paleta é sempre o primeiro; os estilos vêm depois e o
        // referenciam por DynamicResource, então basta trocar este.
        if (app.Resources.MergedDictionaries.Count > 0)
            app.Resources.MergedDictionaries[0] = dict;
        else
            app.Resources.MergedDictionaries.Add(dict);

        bool changed = IsDark != dark;
        IsDark = dark;

        foreach (Window w in app.Windows)
            ApplyWindowChrome(w, dark);

        if (changed) EffectiveThemeChanged?.Invoke(dark);
    }

    /// <summary>Faz a moldura desenhada pelo Windows (sombra, cantos) acompanhar o tema.</summary>
    public static void ApplyWindowChrome(Window window, bool dark)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            int useDark = dark ? 1 : 0;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));

            int corner = DwmwcpRound;
            DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref corner, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // Windows sem DWM: a janela apenas fica com a moldura padrão.
        }
    }

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}

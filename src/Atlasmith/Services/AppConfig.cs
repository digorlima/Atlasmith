using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Atlasmith.Core;
using Atlasmith.Localization;
using JsonObject = System.Text.Json.Nodes.JsonObject;

namespace Atlasmith.Services;

public enum AppTheme
{
    System,
    Light,
    Dark
}

/// <summary>
/// Preferências gravadas em %AppData%\Atlasmith\config.json.
/// É o que faz os caminhos e as opções sobreviverem ao fechar o programa.
/// </summary>
public sealed class AppConfig
{
    public InputMode InputMode { get; set; } = InputMode.Folder;
    public string InputFolder { get; set; } = "";
    public string InputKraFile { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public string OutputName { get; set; } = "Atlas";

    public bool IgnoreHiddenLayers { get; set; } = true;
    public SheetFormat SheetFormat { get; set; } = SheetFormat.Atlasmith;

    public int Padding { get; set; } = PackSettings.DefaultPadding;
    public int BorderPadding { get; set; } = 0;
    public int Extrude { get; set; } = 0;
    public int MaxSize { get; set; } = PackSettings.DefaultMaxSize;
    public bool PowerOfTwo { get; set; } = true;
    public bool ForceSquare { get; set; } = false;
    public bool ShrinkToFit { get; set; } = true;

    public AppTheme Theme { get; set; } = AppTheme.System;
    public AppLanguage Language { get; set; } = AppLanguage.System;

    public double WindowWidth { get; set; } = 1180;
    public double WindowHeight { get; set; } = 760;

    // Anuláveis de propósito: JSON não tem NaN, então usar double.NaN como
    // "sem posição salva" faria a gravação inteira falhar em silêncio.
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    public bool WindowMaximized { get; set; }

    /// <summary>A origem do modo atual: a pasta de imagens ou o .kra.</summary>
    [JsonIgnore]
    public string InputPath => InputMode == InputMode.Krita ? InputKraFile : InputFolder;

    [JsonIgnore]
    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Atlasmith");

    /// <summary>Onde as versões beta (DaigoPacker) guardavam tudo.</summary>
    [JsonIgnore]
    public static string LegacyDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DaigoPacker");

    [JsonIgnore]
    public static string FilePath => Path.Combine(Directory, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Tolerante na leitura para não descartar arquivos gravados por versões
        // anteriores que escreveram NaN/Infinity.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppConfig Load()
    {
        MigrateFromDaigoPacker();

        try
        {
            if (!File.Exists(FilePath)) return new AppConfig();

            string json = File.ReadAllText(FilePath);

            var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            if (loaded is not null && loaded.LooksInitialized()) return loaded.Sanitized();

            // Arquivo legível mas sem nenhum campo conhecido: provavelmente é o
            // config da primeira versão, em Python.
            var migrated = TryMigrateLegacy(json);
            if (migrated is not null) return migrated.Sanitized();

            return loaded?.Sanitized() ?? new AppConfig();
        }
        catch
        {
            // Config corrompida não deve impedir o programa de abrir — mas fica
            // uma cópia, já que a próxima gravação vai substituí-la.
            SafeFile.KeepCorruptCopy(FilePath);
            return new AppConfig();
        }
    }

    /// <summary>
    /// Primeira abertura do Atlasmith depois das betas: copia config.json e
    /// atlases.json de %AppData%\DaigoPacker. A pasta antiga fica intacta, então
    /// voltar para a beta continua funcionando.
    ///
    /// Os atlas da beta tinham .tpsheet; continuam assim até o projeto ser
    /// convertido no Unity (aí a geração passa sozinha para o .atlasmith.json).
    /// </summary>
    private static void MigrateFromDaigoPacker()
    {
        try
        {
            if (System.IO.Directory.Exists(Directory) || !System.IO.Directory.Exists(LegacyDirectory)) return;

            System.IO.Directory.CreateDirectory(Directory);
            foreach (string file in new[] { "config.json", "atlases.json" })
            {
                string from = Path.Combine(LegacyDirectory, file);
                if (!File.Exists(from)) continue;

                string json = File.ReadAllText(from);
                SafeFile.WriteAllText(Path.Combine(Directory, file), MarkAsTpSheet(json));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Sem migração, o Atlasmith só começa do zero; a beta continua com os dados dela.
        }
    }

    /// <summary>Acrescenta "SheetFormat": "TpSheet" no config e em cada atlas salvo que não o tenha.</summary>
    private static string MarkAsTpSheet(string json)
    {
        var root = JsonNode.Parse(json);
        if (root is not JsonObject obj) return json;

        if (obj["Atlases"] is JsonArray atlases)
        {
            foreach (var atlas in atlases.OfType<JsonObject>())
                atlas.TryAdd(nameof(AtlasProfile.SheetFormat), nameof(Core.SheetFormat.TpSheet));
        }
        else
        {
            obj.TryAdd(nameof(SheetFormat), nameof(Core.SheetFormat.TpSheet));
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Distingue um config de verdade de um objeto JSON sem nenhum campo nosso.</summary>
    private bool LooksInitialized() =>
        !string.IsNullOrEmpty(InputFolder) ||
        !string.IsNullOrEmpty(InputKraFile) ||
        !string.IsNullOrEmpty(OutputFolder) ||
        WindowLeft is not null ||
        WindowMaximized;

    /// <summary>
    /// Importa o config da versão em Python, que gravava
    /// {"input_path": "...", "output_path": ".../atlas.png"} no mesmo arquivo.
    /// </summary>
    private static AppConfig? TryMigrateLegacy(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

        bool hasInput = doc.RootElement.TryGetProperty("input_path", out var inputEl);
        bool hasOutput = doc.RootElement.TryGetProperty("output_path", out var outputEl);
        if (!hasInput && !hasOutput) return null;

        var config = new AppConfig();

        if (hasInput && inputEl.GetString() is { Length: > 0 } input)
            config.InputFolder = Path.GetFullPath(input);

        if (hasOutput && outputEl.GetString() is { Length: > 0 } output)
        {
            // O formato antigo guardava o caminho completo do .png.
            config.OutputFolder = Path.GetDirectoryName(Path.GetFullPath(output)) ?? "";
            string stem = Path.GetFileNameWithoutExtension(output);
            if (!string.IsNullOrEmpty(stem)) config.OutputName = stem;
        }

        return config;
    }

    public void Save()
    {
        try
        {
            SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Sem permissão de escrita: seguir sem persistir é melhor que travar ao fechar.
        }
    }

    private AppConfig Sanitized()
    {
        InputFolder ??= "";
        InputKraFile ??= "";
        OutputFolder ??= "";
        if (!Enum.IsDefined(InputMode)) InputMode = InputMode.Folder;
        if (!Enum.IsDefined(SheetFormat)) SheetFormat = SheetFormat.Atlasmith;
        if (!Enum.IsDefined(Theme)) Theme = AppTheme.System;
        if (!Enum.IsDefined(Language)) Language = AppLanguage.System;
        Padding = Math.Clamp(Padding, 0, PackSettings.MaxPadding);
        BorderPadding = Math.Clamp(BorderPadding, 0, PackSettings.MaxPadding);
        Extrude = Math.Clamp(Extrude, 0, PackSettings.MaxExtrude);
        MaxSize = Math.Clamp(MaxSize, PackSettings.MinMaxSize, PackSettings.MaxMaxSize);
        if (string.IsNullOrWhiteSpace(OutputName)) OutputName = "Atlas";
        if (WindowWidth < 900) WindowWidth = 900;
        if (WindowHeight < 600) WindowHeight = 600;
        return this;
    }

    /// <summary>Os campos atuais como receita de atlas (ainda sem os dados da geração).</summary>
    public AtlasProfile ToProfile() => new()
    {
        Name = OutputName,
        OutputFolder = OutputFolder,
        InputMode = InputMode,
        InputFolder = InputFolder,
        InputKraFile = InputKraFile,
        IgnoreHiddenLayers = IgnoreHiddenLayers,
        SheetFormat = SheetFormat,
        Padding = Padding,
        BorderPadding = BorderPadding,
        Extrude = Extrude,
        MaxSize = MaxSize,
        PowerOfTwo = PowerOfTwo,
        ForceSquare = ForceSquare
    };

    /// <summary>Carrega um atlas salvo nos campos atuais.</summary>
    public void Apply(AtlasProfile profile)
    {
        OutputName = string.IsNullOrWhiteSpace(profile.Name) ? "Atlas" : profile.Name;
        OutputFolder = profile.OutputFolder;
        InputMode = profile.InputMode;
        InputFolder = profile.InputFolder;
        InputKraFile = profile.InputKraFile;
        IgnoreHiddenLayers = profile.IgnoreHiddenLayers;
        SheetFormat = profile.SheetFormat;
        Padding = profile.Padding;
        BorderPadding = profile.BorderPadding;
        Extrude = profile.Extrude;
        MaxSize = profile.MaxSize;
        PowerOfTwo = profile.PowerOfTwo;
        ForceSquare = profile.ForceSquare;
    }

    public PackSettings ToPackSettings() => new()
    {
        Padding = Padding,
        BorderPadding = BorderPadding,
        Extrude = Extrude,
        MaxSize = MaxSize,
        PowerOfTwo = PowerOfTwo,
        ForceSquare = ForceSquare,
        ShrinkToFit = ShrinkToFit,
        Bias = SizeBias.PreferSquare
    };
}

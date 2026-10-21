using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using Atlasmith.Core;

namespace Atlasmith.Services;

/// <summary>
/// Os atlas já gerados, em %AppData%\Atlasmith\atlases.json.
///
/// Fica fora do config.json de propósito: o config é regravado a cada ajuste e ao
/// fechar a janela, então com duas janelas abertas a última a fechar apagaria o
/// que a outra salvou. Aqui toda alteração relê o arquivo antes de gravar.
/// </summary>
public static class AtlasLibrary
{
    public static string FilePath => Path.Combine(AppConfig.Directory, "atlases.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed class Store
    {
        public List<AtlasProfile> Atlases { get; set; } = [];
    }

    /// <summary>Os atlas salvos, do gerado mais recentemente para o mais antigo.</summary>
    public static List<AtlasProfile> Load()
    {
        try
        {
            return Read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Arquivo momentaneamente preso por outra janela: a próxima leitura resolve.
            return [];
        }
    }

    /// <summary>Guarda o atlas, substituindo o que tiver o mesmo destino.</summary>
    public static void Record(AtlasProfile generated)
    {
        var atlases = Read();
        atlases.RemoveAll(p => p.Targets(generated.OutputFolder, generated.Name));
        atlases.Add(generated);
        Write(atlases);
    }

    /// <summary>Tira o atlas da lista. Os arquivos dele não são tocados.</summary>
    public static void Remove(AtlasProfile profile)
    {
        var atlases = Read();
        if (atlases.RemoveAll(p => p.Targets(profile.OutputFolder, profile.Name)) > 0)
            Write(atlases);
    }

    /// <summary>
    /// Na primeira abertura com a lista, o atlas que já estava configurado vira o
    /// primeiro item — desde que exista em disco, para não guardar uma receita que
    /// nunca chegou a ser gerada.
    /// </summary>
    public static void SeedFrom(AppConfig config)
    {
        try
        {
            if (File.Exists(FilePath)) return;

            var profile = config.ToProfile();
            if (profile.InputPath.Length == 0 || profile.OutputFolder.Length == 0) return;

            string pngPath = Path.Combine(profile.OutputFolder, profile.Name + ".png");
            if (!File.Exists(pngPath)) return;

            profile.LastGenerated = File.GetLastWriteTime(pngPath);
            if (AtlasNaming.ExistingFormat(profile.OutputFolder, profile.Name) is { } format) profile.SheetFormat = format;
            if (AtlasNaming.TryReadSheet(profile.OutputFolder, profile.Name) is { } sheet)
            {
                profile.SpriteCount = sheet.Sprites.Count;
                profile.AtlasWidth = sheet.Width;
                profile.AtlasHeight = sheet.Height;
            }

            Write([profile]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Sem a semente a lista só começa vazia.
        }
    }

    /// <summary>Lança IOException se não conseguir ler: quem vai gravar não pode partir de uma lista vazia por engano.</summary>
    private static List<AtlasProfile> Read()
    {
        if (!File.Exists(FilePath)) return [];

        string json = File.ReadAllText(FilePath);
        try
        {
            var store = JsonSerializer.Deserialize<Store>(json, JsonOptions);
            return Sanitize(store?.Atlases ?? []);
        }
        catch (JsonException)
        {
            SafeFile.KeepCorruptCopy(FilePath);
            return [];
        }
    }

    private static void Write(List<AtlasProfile> atlases) =>
        SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(new Store { Atlases = atlases }, JsonOptions));

    private static List<AtlasProfile> Sanitize(List<AtlasProfile> atlases)
    {
        var clean = new List<AtlasProfile>();

        foreach (var p in atlases.OrderByDescending(p => p.LastGenerated))
        {
            if (string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.OutputFolder)) continue;
            // Repetido (arquivo editado à mão): vale o gerado por último.
            if (clean.Any(c => c.Targets(p.OutputFolder, p.Name))) continue;

            p.InputFolder ??= "";
            p.InputKraFile ??= "";
            if (!Enum.IsDefined(p.InputMode)) p.InputMode = InputMode.Folder;
            if (!Enum.IsDefined(p.SheetFormat)) p.SheetFormat = SheetFormat.Atlasmith;
            p.Padding = Math.Clamp(p.Padding, 0, PackSettings.MaxPadding);
            p.BorderPadding = Math.Clamp(p.BorderPadding, 0, PackSettings.MaxPadding);
            p.Extrude = Math.Clamp(p.Extrude, 0, PackSettings.MaxExtrude);
            p.MaxSize = Math.Clamp(p.MaxSize, PackSettings.MinMaxSize, PackSettings.MaxMaxSize);

            clean.Add(p);
        }

        return clean;
    }
}

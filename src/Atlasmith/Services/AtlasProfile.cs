using System.IO;
using System.Text.Json.Serialization;
using Atlasmith.Core;
using Atlasmith.Localization;

namespace Atlasmith.Services;

public enum InputMode
{
    /// <summary>Uma pasta de imagens (por exemplo, a saída do Batch Exporter).</summary>
    Folder,

    /// <summary>Camadas lidas direto de um arquivo do Krita (.kra ou .krz).</summary>
    Krita
}

/// <summary>
/// A receita de um atlas já gerado: de onde vieram os sprites, para onde foram e
/// com que margens. Um atlas é identificado pelo arquivo de saída (pasta + nome),
/// então gerar de novo no mesmo lugar atualiza a receita em vez de duplicá-la.
/// </summary>
public sealed class AtlasProfile
{
    public string Name { get; set; } = "";
    public string OutputFolder { get; set; } = "";

    public InputMode InputMode { get; set; } = InputMode.Folder;

    // Os dois caminhos ficam guardados: trocar de modo e voltar não perde o outro.
    public string InputFolder { get; set; } = "";
    public string InputKraFile { get; set; } = "";

    public bool IgnoreHiddenLayers { get; set; } = true;
    public SheetFormat SheetFormat { get; set; } = SheetFormat.Atlasmith;

    public int Padding { get; set; } = PackSettings.DefaultPadding;
    public int BorderPadding { get; set; }
    public int Extrude { get; set; }
    public int MaxSize { get; set; } = PackSettings.DefaultMaxSize;
    public bool PowerOfTwo { get; set; } = true;
    public bool ForceSquare { get; set; }

    public DateTime LastGenerated { get; set; }
    public int SpriteCount { get; set; }
    public int AtlasWidth { get; set; }
    public int AtlasHeight { get; set; }

    [JsonIgnore]
    public string InputPath => InputMode == InputMode.Krita ? InputKraFile : InputFolder;

    [JsonIgnore]
    public bool IsKrita => InputMode == InputMode.Krita;

    [JsonIgnore]
    public string SourceBadge => IsKrita ? Texts.Current.BadgeKrita : Texts.Current.BadgeFolder;

    /// <summary>Fim do caminho de saída, para distinguir na lista atlas de mesmo nome.</summary>
    [JsonIgnore]
    public string FolderHint => PathText.Tail(OutputFolder, 3);

    [JsonIgnore]
    public string WhenText => RelativeTime.Describe(LastGenerated);

    [JsonIgnore]
    public string Details => $"{Path.Combine(OutputFolder, Name)}.png\n{Texts.Current.DetailsSource(InputPath)}";

    public bool Targets(string outputFolder, string name) =>
        string.Equals(Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase) &&
        PathText.Same(OutputFolder, outputFolder);

    /// <summary>O que mudou de uma receita para a outra, em palavras. O destino não entra: ele é a identidade.</summary>
    public List<string> DifferencesFrom(AtlasProfile other)
    {
        var t = Texts.Current;
        var changes = new List<string>();
        if (InputMode != other.InputMode || !PathText.Same(InputPath, other.InputPath)) changes.Add(t.ChangeSource);
        // Só faz diferença lendo do Krita; na pasta de imagens não há camadas.
        if (IsKrita && other.IsKrita && IgnoreHiddenLayers != other.IgnoreHiddenLayers) changes.Add(t.ChangeHidden);
        if (Padding != other.Padding) changes.Add(t.ChangePadding);
        if (BorderPadding != other.BorderPadding) changes.Add(t.ChangeBorder);
        if (Extrude != other.Extrude) changes.Add(t.ChangeExtrude);
        if (MaxSize != other.MaxSize) changes.Add(t.ChangeMaxSize);
        if (PowerOfTwo != other.PowerOfTwo) changes.Add(t.ChangePowerOfTwo);
        if (ForceSquare != other.ForceSquare) changes.Add(t.ChangeSquare);
        if (SheetFormat != other.SheetFormat) changes.Add(t.ChangeFormat);
        return changes;
    }

    /// <summary>Os arquivos que gerar este atlas grava: o PNG e as planilhas do formato escolhido.</summary>
    [JsonIgnore]
    public string[] OutputFiles
    {
        get
        {
            var files = new List<string> { Name + ".png" };
            if (SheetFormat != SheetFormat.TpSheet) files.Add(Name + AtlasSheet.Extension);
            if (SheetFormat != SheetFormat.Atlasmith) files.Add(Name + TpSheet.Extension);
            return files.ToArray();
        }
    }
}

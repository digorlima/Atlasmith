using System.IO;

using Atlasmith.Core;

namespace Atlasmith.Services;

/// <summary>Gravação dos arquivos de configuração sem risco de deixá-los pela metade.</summary>
internal static class SafeFile
{
    /// <summary>
    /// Escreve num temporário ao lado e troca de uma vez: se o processo cair no
    /// meio, sobra o arquivo antigo inteiro em vez de um JSON truncado.
    /// </summary>
    public static void WriteAllText(string path, string content)
    {
        string? folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        // Um temporário por processo: duas janelas abertas não disputam o mesmo arquivo.
        string temp = $"{path}.{Environment.ProcessId}.tmp";
        try
        {
            File.WriteAllText(temp, content);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>Guarda uma cópia de um arquivo ilegível antes que a próxima gravação o substitua.</summary>
    public static void KeepCorruptCopy(string path)
    {
        try
        {
            string copy = Path.Combine(
                Path.GetDirectoryName(path) ?? "",
                Path.GetFileNameWithoutExtension(path) + ".corrompido" + Path.GetExtension(path));
            File.Copy(path, copy, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sem a cópia, o pior caso é o mesmo de antes: começar do zero.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-or-later
namespace DeXter;

/// <summary>Onde o app guarda configuracao e log: %LOCALAPPDATA%\DeXter.</summary>
internal static class Caminhos
{
    public static string Dados { get; } = Descobrir();

    private static string Descobrir()
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeXter");
        try
        {
            Directory.CreateDirectory(local);
            return local;
        }
        catch
        {
            return Path.GetTempPath();
        }
    }
}

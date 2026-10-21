// SPDX-License-Identifier: GPL-3.0-or-later
namespace DeXter;

/// <summary>
/// Onde o app guarda configuracao e log.
/// Instalado como MSIX, a pasta do app (WindowsApps) e somente leitura, entao usa a pasta de dados
/// do pacote (LocalState). E um caminho real: o Bloco de Notas abre de fora do pacote.
/// Os dados sobrevivem a atualizacoes e sao apagados ao desinstalar.
/// </summary>
internal static class Caminhos
{
    public static string Dados { get; } = Descobrir();

    private static string Descobrir()
    {
        try
        {
            var pasta = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            if (!string.IsNullOrEmpty(pasta))
            {
                return pasta;
            }
        }
        catch
        {
            // sem identidade de pacote (exe executado direto): cai para a pasta do usuario
        }

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

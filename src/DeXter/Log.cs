// SPDX-License-Identifier: GPL-3.0-or-later
namespace DeXter;

/// <summary>Log em arquivo (dexter.log na pasta de dados do app; rotaciona em 1 MB).</summary>
internal static class Log
{
    private static readonly object Trava = new();
    private const long TamanhoMaximo = 1024 * 1024; // 1 MB, depois vira .old

    public static string Arquivo { get; } = Path.Combine(Caminhos.Dados, "dexter.log");

    public static void Info(string msg)
    {
        var linha = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {msg}";
        lock (Trava)
        {
            try
            {
                var fi = new FileInfo(Arquivo);
                if (fi.Exists && fi.Length > TamanhoMaximo)
                {
                    File.Move(Arquivo, Arquivo + ".old", overwrite: true);
                }
                File.AppendAllText(Arquivo, linha + Environment.NewLine);
            }
            catch
            {
                // log nunca derruba o app
            }
        }
        System.Diagnostics.Debug.WriteLine(linha);
    }

    public static void Erro(string onde, Exception ex) =>
        Info($"[ERRO] {onde}: {ex.GetType().Name}: {ex.Message} (HRESULT 0x{ex.HResult:X8})");
}

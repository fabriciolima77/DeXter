// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;

namespace DeXter;

/// <summary>
/// DeX nativo: envia o servidor (server\dexter-server.jar) para o celular e o executa via app_process.
/// O servidor faz o Wi-Fi Display da Samsung conectar no proprio celular (127.0.0.1) em modo "DeX sem fio",
/// entao o DeX abre num display real, sem a janelinha do display simulado e sem Wi-Fi/Miracast no PC.
/// Ideia e base do servidor: ScrcpyDeX (Apache-2.0), veja THIRD_PARTY_NOTICES.md.
///
/// O servidor roda destacado (setsid + nohup): sobrevive a queda do adb, entao o DeX continua quando
/// o cabo sai e o adb passa para o Wi-Fi.
/// </summary>
internal sealed partial class DexNativo
{
    private const string JarRemoto = "/data/local/tmp/dexter-server.jar";
    private const string LogRemoto = "/data/local/tmp/dexter-server.log";
    private const string Classe = "dexter.server.Server";

    // padrao para pgrep/pkill: o "[d]" impede que o proprio comando do shell (que contem o texto) case
    private const string PadraoProcesso = "[d]exter.server.Server";

    private readonly Adb _adb;

    public DexNativo(Adb adb) => _adb = adb;

    /// <summary>Caminho do servidor ao lado do exe (null se o build nao gerou).</summary>
    public static string? JarLocal
    {
        get
        {
            var p = Path.Combine(AppContext.BaseDirectory, "server", "dexter-server.jar");
            return File.Exists(p) ? p : null;
        }
    }

    /// <summary>Ultimo motivo de falha (para o log/aviso).</summary>
    public string UltimoErro { get; private set; } = "";

    /// <summary>true quando a falha indica que o aparelho nao suporta o modo nativo.</summary>
    public bool UltimoFoiIncompativel { get; private set; }

    [GeneratedRegex(@"DEXTER_DISPLAY_ID=(\d+)")]
    private static partial Regex RegexId();

    [GeneratedRegex(@"DEXTER_(INCOMPATIVEL|ERRO)=(.*)")]
    private static partial Regex RegexErro();

    /// <summary>
    /// Garante o DeX nativo ativo e devolve o id do display. Reaproveita uma sessao ja aberta
    /// (ex.: depois de trocar do cabo para o Wi-Fi). null = falhou (veja UltimoErro).
    /// </summary>
    public async Task<int?> AtivarAsync(string serial, Func<int, Task<bool>> displayExiste, CancellationToken ct)
    {
        UltimoErro = "";
        UltimoFoiIncompativel = false;

        var jar = JarLocal;
        if (jar == null)
        {
            UltimoErro = "server\\dexter-server.jar nao encontrado ao lado do DeXter.exe";
            UltimoFoiIncompativel = true;
            return null;
        }

        // 1. sessao ja rodando?
        if (await RodandoAsync(serial, ct))
        {
            var (_, log) = await _adb.ShellAsync(serial, $"cat {LogRemoto}", 8_000, ct);
            var m = RegexId().Match(log);
            if (m.Success)
            {
                var idExistente = int.Parse(m.Groups[1].Value);
                if (await displayExiste(idExistente))
                {
                    Log.Info($"[nativo] reaproveitando a sessao do DeX (display {idExistente}).");
                    return idExistente;
                }
            }
            await PararAsync(serial, ct); // sessao velha/quebrada
        }

        // 2. envia o servidor se mudou
        if (!await JarAtualAsync(serial, jar, ct) && !await _adb.PushAsync(serial, jar, JarRemoto, ct))
        {
            UltimoErro = "nao consegui enviar o servidor para o celular";
            return null;
        }

        // 3. inicia destacado
        Log.Info("[nativo] iniciando o DeX nativo no celular...");
        await _adb.ShellAsync(serial,
            $"rm -f {LogRemoto}; CLASSPATH={JarRemoto} setsid nohup app_process / {Classe} activate > {LogRemoto} 2>&1 < /dev/null &",
            10_000, ct);

        // 4. espera o id do display (ate ~30 s)
        var fim = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        var ultimoLog = "";
        while (DateTime.UtcNow < fim && !ct.IsCancellationRequested)
        {
            await Task.Delay(700, ct);
            var (_, log) = await _adb.ShellAsync(serial, $"cat {LogRemoto}", 8_000, ct);
            ultimoLog = log;
            var m = RegexId().Match(log);
            if (m.Success)
            {
                var id = int.Parse(m.Groups[1].Value);
                Log.Info($"[nativo] DeX nativo ativo no display {id}.");
                return id;
            }
            var e = RegexErro().Match(log);
            if (e.Success)
            {
                UltimoFoiIncompativel = e.Groups[1].Value == "INCOMPATIVEL";
                UltimoErro = e.Groups[2].Value.Trim();
                break;
            }
        }

        if (UltimoErro.Length == 0)
        {
            UltimoErro = "o servidor nao respondeu em 30 s";
        }
        Log.Info($"[nativo] falhou: {UltimoErro}");
        foreach (var linha in ultimoLog.Split('\n').TakeLast(15))
        {
            if (!string.IsNullOrWhiteSpace(linha))
            {
                Log.Info($"[nativo:celular] {linha.TrimEnd()}");
            }
        }
        await PararAsync(serial, CancellationToken.None);
        return null;
    }

    /// <summary>Encerra o DeX nativo (mata o servidor e desconecta o Wi-Fi Display).</summary>
    public async Task PararAsync(string serial, CancellationToken ct)
    {
        await _adb.ShellAsync(serial,
            $"pkill -f {PadraoProcesso}; CLASSPATH={JarRemoto} app_process / {Classe} disconnect",
            10_000, ct);
        Log.Info("[nativo] DeX nativo encerrado.");
    }

    private async Task<bool> RodandoAsync(string serial, CancellationToken ct)
    {
        var (_, saida) = await _adb.ShellAsync(serial, $"pgrep -f {PadraoProcesso}", 8_000, ct);
        return saida.Split('\n').Any(l => int.TryParse(l.Trim(), out _));
    }

    private async Task<bool> JarAtualAsync(string serial, string jar, CancellationToken ct)
    {
        var (_, saida) = await _adb.ShellAsync(serial, $"stat -c %s {JarRemoto} 2>/dev/null", 8_000, ct);
        return long.TryParse(saida.Trim(), out var tam) && tam == new FileInfo(jar).Length;
    }
}

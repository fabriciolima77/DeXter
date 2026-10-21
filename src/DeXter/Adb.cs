// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace DeXter;

internal sealed record Dispositivo(string Serial, string Estado, bool Wifi)
{
    public bool Pronto => Estado == "device";
    public override string ToString() => $"{Serial} [{(Wifi ? "wifi" : "usb")}, {Estado}]";
}

/// <summary>
/// adb (o que vem junto do scrcpy): lista celulares, escolhe USB ou Wi-Fi e prepara a conexao sem cabo.
/// </summary>
internal sealed partial class Adb
{
    public const int PortaTcpip = 5555;

    private readonly string _exe;
    private readonly SemaphoreSlim _conectando = new(1, 1);

    private Adb(string exe) => _exe = exe;

    /// <summary>Este adb e o que vem dentro do app (pasta scrcpy\ ao lado do exe).</summary>
    public bool Embutido => _exe.StartsWith(Path.Combine(AppContext.BaseDirectory, "scrcpy"), StringComparison.OrdinalIgnoreCase);

    /// <summary>Encerra o servidor do adb (libera os arquivos do app para atualizar/recompilar).</summary>
    public Task PararServidorAsync() => RodarAsync("kill-server", 5_000, CancellationToken.None);

    public static Adb? Criar(string scrcpyExe)
    {
        var adb = Path.Combine(Path.GetDirectoryName(scrcpyExe) ?? "", "adb.exe");
        return File.Exists(adb) ? new Adb(adb) : null;
    }

    [GeneratedRegex(@"_adb-tls-connect\._tcp\.?\s+(\d{1,3}(?:\.\d{1,3}){3}:\d+)")]
    private static partial Regex RegexMdns();

    [GeneratedRegex(@"inet (\d{1,3}(?:\.\d{1,3}){3})/")]
    private static partial Regex RegexIp();

    /// <summary>adb devices</summary>
    public async Task<List<Dispositivo>> ListarAsync(CancellationToken ct = default)
    {
        var (_, saida) = await RodarAsync("devices", 10_000, ct);
        var lista = new List<Dispositivo>();
        foreach (var linha in saida.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (linha.StartsWith("List of devices") || linha.StartsWith('*'))
            {
                continue;
            }
            var partes = linha.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length < 2)
            {
                continue;
            }
            var serial = partes[0];
            var wifi = serial.Contains(':') || serial.Contains("._adb-tls-connect", StringComparison.OrdinalIgnoreCase);
            lista.Add(new Dispositivo(serial, partes[1], wifi));
        }
        return lista;
    }

    /// <summary>
    /// Escolhe o celular conforme o modo ("auto", "usb", "wifi").
    /// Sem dispositivo Wi-Fi conectado, tenta reconectar (endereco salvo, depois mDNS da depuracao sem fio).
    /// </summary>
    public async Task<Dispositivo?> EscolherAsync(string modo, string enderecoSalvo, CancellationToken ct)
    {
        var lista = await ListarAsync(ct);
        foreach (var d in lista.Where(d => !d.Pronto))
        {
            Log.Info($"[adb] dispositivo nao pronto: {d}" +
                     (d.Estado == "unauthorized" ? " -> autorize a depuracao na tela do celular" : ""));
        }

        var usb = lista.FirstOrDefault(d => d.Pronto && !d.Wifi);
        var wifi = lista.FirstOrDefault(d => d.Pronto && d.Wifi);

        modo = (modo ?? "auto").Trim().ToLowerInvariant();
        if (modo == "usb")
        {
            return usb;
        }
        if (modo == "auto" && usb != null)
        {
            return usb;
        }
        if (wifi != null)
        {
            return wifi;
        }

        // Nenhum Wi-Fi pronto: tenta reconectar
        if (await ReconectarWifiAsync(enderecoSalvo, ct))
        {
            lista = await ListarAsync(ct);
            return lista.FirstOrDefault(d => d.Pronto && d.Wifi);
        }
        return null;
    }

    /// <summary>Tenta "adb connect" no endereco salvo e nos anunciados por mDNS (depuracao sem fio pareada).</summary>
    public async Task<bool> ReconectarWifiAsync(string enderecoSalvo, CancellationToken ct)
    {
        if (!await _conectando.WaitAsync(0, ct))
        {
            return false; // ja tem alguem tentando
        }
        try
        {
            var enderecos = new List<string>();
            if (!string.IsNullOrWhiteSpace(enderecoSalvo))
            {
                enderecos.Add(enderecoSalvo.Trim());
            }
            enderecos.AddRange(await MdnsAsync(ct));

            foreach (var end in enderecos.Distinct())
            {
                if (await ConectarAsync(end, ct))
                {
                    return true;
                }
            }
            return false;
        }
        finally
        {
            _conectando.Release();
        }
    }

    /// <summary>Enderecos ip:porta da "Depuracao sem fio" anunciados na rede (adb mdns services).</summary>
    public async Task<List<string>> MdnsAsync(CancellationToken ct)
    {
        var (_, saida) = await RodarAsync("mdns services", 10_000, ct);
        var ends = RegexMdns().Matches(saida).Select(m => m.Groups[1].Value).Distinct().ToList();
        if (ends.Count > 0)
        {
            Log.Info($"[adb] mDNS: {string.Join(", ", ends)}");
        }
        return ends;
    }

    public async Task<bool> ConectarAsync(string endereco, CancellationToken ct)
    {
        var (_, saida) = await RodarAsync($"connect {endereco}", 8_000, ct);
        var ok = saida.Contains("connected to", StringComparison.OrdinalIgnoreCase) &&
                 !saida.Contains("cannot", StringComparison.OrdinalIgnoreCase) &&
                 !saida.Contains("failed", StringComparison.OrdinalIgnoreCase);
        Log.Info($"[adb] connect {endereco}: {(ok ? "OK" : saida.Trim())}");
        return ok;
    }

    /// <summary>
    /// Com o celular no cabo: liga o adb por TCP na porta 5555 e conecta pelo IP do Wi-Fi.
    /// Vale ate o celular reiniciar. Retorna o endereco ip:porta ou null.
    /// </summary>
    public async Task<string?> PrepararPeloCaboAsync(string serialUsb, CancellationToken ct)
    {
        var (_, ipSaida) = await RodarAsync($"-s {serialUsb} shell ip -f inet addr show wlan0", 8_000, ct);
        var m = RegexIp().Match(ipSaida);
        if (!m.Success)
        {
            Log.Info($"[adb] nao achei o IP do Wi-Fi do celular: {ipSaida.Trim()}");
            return null;
        }
        var endereco = $"{m.Groups[1].Value}:{PortaTcpip}";
        Log.Info($"[adb] IP do celular: {m.Groups[1].Value}");

        var (_, tcp) = await RodarAsync($"-s {serialUsb} tcpip {PortaTcpip}", 10_000, ct);
        Log.Info($"[adb] tcpip: {tcp.Trim()}");

        // o adbd do celular reinicia; espera um pouco e tenta conectar algumas vezes
        for (var i = 0; i < 5 && !ct.IsCancellationRequested; i++)
        {
            await Task.Delay(1500, ct);
            if (await ConectarAsync(endereco, ct))
            {
                return endereco;
            }
        }
        return null;
    }

    [GeneratedRegex(@"Physical density:\s*(\d+)")]
    private static partial Regex RegexDensidade();

    [GeneratedRegex(@"Physical size:\s*(\d+)x(\d+)")]
    private static partial Regex RegexTamanho();

    /// <summary>Tamanho e densidade originais (sem override) de um display: wm size/density -d.</summary>
    public async Task<(int largura, int altura, int densidade)?> LerDisplayAsync(string serial, int displayId, CancellationToken ct)
    {
        var (_, tam) = await RodarAsync($"{Sel(serial)}shell wm size -d {displayId}", 8_000, ct);
        var (_, den) = await RodarAsync($"{Sel(serial)}shell wm density -d {displayId}", 8_000, ct);
        var mt = RegexTamanho().Match(tam);
        var md = RegexDensidade().Match(den);
        if (!mt.Success || !md.Success)
        {
            Log.Info($"[adb] nao consegui ler o display {displayId}: {tam.Trim()} / {den.Trim()}");
            return null;
        }
        return (int.Parse(mt.Groups[1].Value), int.Parse(mt.Groups[2].Value), int.Parse(md.Groups[1].Value));
    }

    /// <summary>
    /// Muda a resolucao logica do display do DeX (wm size -d) e a densidade (wm density -d).
    /// largura/altura 0 = volta ao tamanho original; densidade 0 = volta a original.
    /// </summary>
    public async Task<bool> AplicarDisplayAsync(string serial, int displayId, int largura, int altura, int densidade, CancellationToken ct)
    {
        var tamanho = largura > 0 && altura > 0 ? $"{largura}x{altura}" : "reset";
        var dens = densidade > 0 ? densidade.ToString() : "reset";
        var (c1, s1) = await RodarAsync($"{Sel(serial)}shell wm size {tamanho} -d {displayId}", 8_000, ct);
        var (c2, s2) = await RodarAsync($"{Sel(serial)}shell wm density {dens} -d {displayId}", 8_000, ct);
        var ok = c1 == 0 && c2 == 0 && !s1.Contains("Error", StringComparison.OrdinalIgnoreCase)
                 && !s2.Contains("Error", StringComparison.OrdinalIgnoreCase);
        Log.Info($"[adb] display {displayId}: tamanho {tamanho}, densidade {dens} -> {(ok ? "OK" : (s1 + s2).Trim())}");
        return ok;
    }

    private static string Sel(string serial) => string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";

    /// <summary>adb pair ip:porta codigo (Depuracao sem fio -> Parear com codigo).</summary>
    public async Task<bool> ParearAsync(string endereco, string codigo, CancellationToken ct)
    {
        var (_, saida) = await RodarAsync($"pair {endereco} {codigo}", 20_000, ct);
        var ok = saida.Contains("Successfully paired", StringComparison.OrdinalIgnoreCase);
        Log.Info($"[adb] pair {endereco}: {(ok ? "OK" : saida.Trim())}");
        return ok;
    }

    private async Task<(int codigo, string saida)> RodarAsync(string args, int limiteMs, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(_exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(_exe) ?? "",
        };
        var saida = new StringBuilder();
        try
        {
            using var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (saida) saida.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (saida) saida.AppendLine(e.Data); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            using var tempo = CancellationTokenSource.CreateLinkedTokenSource(ct);
            tempo.CancelAfter(limiteMs);
            try
            {
                await p.WaitForExitAsync(tempo.Token);
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignora */ }
                return (-1, $"timeout: adb {args}");
            }
            p.WaitForExit();
            lock (saida) return (p.ExitCode, saida.ToString());
        }
        catch (Exception ex)
        {
            Log.Erro($"adb {args}", ex);
            return (-1, ex.Message);
        }
    }
}

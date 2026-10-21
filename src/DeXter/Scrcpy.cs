// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace DeXter;

/// <summary>
/// Descobre o display do DeX (scrcpy --list-displays) e controla o processo do scrcpy.
/// Substitui o C:\dex\dex.ps1.
/// </summary>
internal sealed partial class Scrcpy
{
    /// <summary>codigo de saida, tempo que ficou aberto, ultima linha relevante da saida.</summary>
    public event Action<int, TimeSpan, string>? Fechou;

    private readonly string _exe;
    private readonly object _trava = new();
    private Process? _proc;
    private DateTime _inicio;
    private string _ultimaLinha = "";
    private bool _fechandoDeProposito;

    public Scrcpy(string exe) => _exe = exe;

    public string Exe => _exe;

    public bool Aberto
    {
        get { lock (_trava) return _proc is { HasExited: false }; }
    }

    public int? DisplayAtual { get; private set; }

    /// <summary>Serial do celular usado pela janela aberta (USB ou ip:porta).</summary>
    public string? SerialAtual { get; private set; }

    [GeneratedRegex(@"--display-id=(\d+)")]
    private static partial Regex RegexDisplay();

    /// <summary>
    /// Espera o display do DeX aparecer. Regra (a mesma do dex.ps1): maior id diferente de 0.
    /// Prefere ids que nao existiam antes da conexao (linhaDeBase), se houver.
    /// </summary>
    public async Task<int?> DescobrirDisplayAsync(string serial, IReadOnlySet<int> linhaDeBase, TimeSpan limite, CancellationToken ct)
    {
        var fim = DateTime.UtcNow + limite;
        var tentativa = 0;
        while (DateTime.UtcNow < fim && !ct.IsCancellationRequested)
        {
            tentativa++;
            var (ids, saida) = await ListarDisplaysAsync(serial, ct);
            var candidatos = ids.Where(i => i != 0).ToList();
            var novos = candidatos.Where(i => !linhaDeBase.Contains(i)).ToList();

            if (novos.Count > 0 || candidatos.Count > 0)
            {
                var id = (novos.Count > 0 ? novos : candidatos).Max();
                Log.Info($"[scrcpy] display do DeX encontrado: {id} (displays: {string.Join(", ", ids)}; tentativa {tentativa})");
                return id;
            }

            if (tentativa == 1 || tentativa % 5 == 0)
            {
                Log.Info($"[scrcpy] display do DeX ainda nao apareceu (tentativa {tentativa}). Saida: {Resumir(saida)}");
            }

            try
            {
                await Task.Delay(1500, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        return null;
    }

    /// <summary>Roda scrcpy --list-displays e devolve os ids encontrados.</summary>
    public async Task<(List<int> ids, string saida)> ListarDisplaysAsync(string serial, CancellationToken ct)
    {
        var psi = NovoPsi($"{Selecao(serial)}--list-displays");
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
            tempo.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                await p.WaitForExitAsync(tempo.Token);
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignora */ }
                return (new List<int>(), "timeout em --list-displays");
            }
            p.WaitForExit(); // garante que a saida assincrona terminou
        }
        catch (Exception ex)
        {
            Log.Erro("executando scrcpy --list-displays", ex);
            return (new List<int>(), ex.Message);
        }

        string texto;
        lock (saida) texto = saida.ToString();
        var ids = RegexDisplay().Matches(texto)
            .Select(m => int.Parse(m.Groups[1].Value))
            .Distinct()
            .ToList();
        return (ids, texto);
    }

    /// <summary>Abre o scrcpy no display indicado. Se ja houver um aberto, nao faz nada.</summary>
    public bool Abrir(int displayId, string serial, string args)
    {
        lock (_trava)
        {
            if (_proc is { HasExited: false })
            {
                return true;
            }

            var linha = $"{Selecao(serial)}--display-id={displayId} {args}";
            Log.Info($"[scrcpy] scrcpy {linha}");
            var psi = NovoPsi(linha);
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => RegistrarSaida(e.Data);
            p.ErrorDataReceived += (_, e) => RegistrarSaida(e.Data);
            p.Exited += AoSair;
            try
            {
                _fechandoDeProposito = false;
                _ultimaLinha = "";
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                Log.Erro("abrindo o scrcpy", ex);
                p.Dispose();
                return false;
            }

            _proc = p;
            _inicio = DateTime.UtcNow;
            DisplayAtual = displayId;
            SerialAtual = serial;
            Log.Info($"[scrcpy] aberto (pid {p.Id}) no display {displayId} via {serial}.");
            return true;
        }
    }

    /// <summary>Fecha o scrcpy: primeiro pede para a janela fechar, depois mata.</summary>
    public void Fechar()
    {
        Process? p;
        lock (_trava)
        {
            p = _proc;
            if (p == null)
            {
                return;
            }
            _fechandoDeProposito = true;
        }

        try
        {
            if (!p.HasExited)
            {
                Log.Info("[scrcpy] fechando...");
                p.CloseMainWindow();
                if (!p.WaitForExit(3000))
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(3000);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Erro("fechando o scrcpy", ex);
        }
    }

    private void AoSair(object? sender, EventArgs e)
    {
        Process? p;
        bool proposital;
        TimeSpan duracao;
        lock (_trava)
        {
            p = _proc;
            if (p == null || !ReferenceEquals(p, sender))
            {
                return;
            }
            _proc = null;
            proposital = _fechandoDeProposito;
            duracao = DateTime.UtcNow - _inicio;
        }

        int codigo;
        try
        {
            p.WaitForExit(); // esvazia a saida assincrona
            codigo = p.ExitCode;
        }
        catch
        {
            codigo = -1;
        }
        p.Dispose();
        DisplayAtual = null;
        SerialAtual = null;

        Log.Info($"[scrcpy] fechou (codigo {codigo}, {duracao.TotalSeconds:F0} s{(proposital ? ", pelo app" : "")}).");
        if (!proposital)
        {
            Fechou?.Invoke(codigo, duracao, _ultimaLinha);
        }
    }

    private void RegistrarSaida(string? linha)
    {
        if (string.IsNullOrWhiteSpace(linha))
        {
            return;
        }
        Log.Info($"[scrcpy] {linha}");
        if (linha.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
            linha.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            linha.Contains("unrecognized", StringComparison.OrdinalIgnoreCase) ||
            linha.Contains("unknown option", StringComparison.OrdinalIgnoreCase) ||
            linha.Contains("WARN", StringComparison.OrdinalIgnoreCase))
        {
            _ultimaLinha = linha.Trim();
        }
    }

    private ProcessStartInfo NovoPsi(string argumentos) => new(_exe, argumentos)
    {
        UseShellExecute = false,
        CreateNoWindow = true, // sem janela de console; a janela do scrcpy (SDL) aparece normalmente
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
        WorkingDirectory = Path.GetDirectoryName(_exe) ?? "",
    };

    /// <summary>--serial=... (vazio quando nao ha adb para escolher: o scrcpy pega o unico celular).</summary>
    private static string Selecao(string serial) => string.IsNullOrEmpty(serial) ? "" : $"--serial={serial} ";

    private static string Resumir(string saida)
    {
        var linhas = saida.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var relevante = linhas.LastOrDefault(l => l.Contains("ERROR") || l.Contains("adb") || l.Contains("device"))
                        ?? linhas.LastOrDefault() ?? "(vazia)";
        return relevante.Length > 200 ? relevante[..200] : relevante;
    }
}

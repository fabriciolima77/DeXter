// SPDX-License-Identifier: GPL-3.0-or-later
using Windows.Media.Miracast;
using Windows.Media.Playback;

namespace DeXter;

/// <summary>
/// Receptor Miracast: anuncia o PC como "TV", aceita o celular sem PIN e consome o stream
/// em reproducao silenciosa so para manter a sessao viva (o video que aparece e o do scrcpy).
/// Todas as chamadas WinRT rodam em threads MTA do pool (como na Fase 1).
/// Os eventos chegam em threads do pool; quem consome deve voltar para a thread da UI.
/// </summary>
internal sealed class MiracastHost : IDisposable
{
    public event Action<string>? Conectado;        // nome do celular
    public event Action? StreamIniciado;
    public event Action<string>? Desconectado;     // nome do celular

    private readonly string _nomeTela;
    private readonly object _trava = new();
    private MiracastReceiver? _receiver;
    private MiracastReceiverSession? _session;
    private MiracastReceiverConnection? _conexao;
    private MediaPlayer? _player;

    public MiracastHost(string nomeTela) => _nomeTela = nomeTela;

    /// <summary>A ultima tentativa de iniciar falhou com AccessDenied ao aplicar as configuracoes.</summary>
    public bool UltimoFoiAcessoNegado { get; private set; }

    /// <summary>Ha um celular conectado agora.</summary>
    public bool TemConexao
    {
        get { lock (_trava) return _conexao != null; }
    }

    /// <summary>Le o status de escuta (para o watchdog). Nunca lanca.</summary>
    public Task<MiracastReceiverListeningStatus?> LerStatusAsync() => Task.Run(() =>
    {
        try
        {
            MiracastReceiver? r;
            lock (_trava) r = _receiver;
            return r?.GetStatus().ListeningStatus;
        }
        catch (Exception ex)
        {
            Log.Erro("lendo status do receptor", ex);
            return (MiracastReceiverListeningStatus?)null;
        }
    });

    /// <summary>(Re)inicia o receptor do zero. Retorna true se ficou escutando.</summary>
    public Task<bool> IniciarAsync() => Task.Run(Iniciar);

    /// <summary>Derruba conexao, sessao e receptor.</summary>
    public Task PararAsync() => Task.Run(PararInterno);

    public void Dispose()
    {
        try
        {
            PararAsync().Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            Log.Erro("encerrando o receptor", ex);
        }
    }

    private bool Iniciar()
    {
        PararInterno();
        UltimoFoiAcessoNegado = false;
        try
        {
            Log.Info($"[miracast] criando receptor (thread {Environment.CurrentManagedThreadId}, {Thread.CurrentThread.GetApartmentState()})...");
            var receiver = new MiracastReceiver();
            receiver.StatusChanged += (r, _) =>
            {
                try
                {
                    var st = r.GetStatus();
                    Log.Info($"[miracast] status: escuta={st.ListeningStatus} wifi={st.WiFiStatus}");
                }
                catch
                {
                    // receptor ja descartado
                }
            };

            var settings = receiver.GetDefaultSettings();
            settings.FriendlyName = _nomeTela;
            settings.ModelName = _nomeTela;
            settings.ModelNumber = "2.0";
            settings.AuthorizationMethod = MiracastReceiverAuthorizationMethod.None;
            settings.RequireAuthorizationFromKnownTransmitters = false;

            var apply = receiver.DisconnectAllAndApplySettings(settings);
            Log.Info($"[miracast] configuracao: {apply?.Status} {apply?.ExtendedError?.Message}");
            if (apply?.Status != MiracastReceiverApplySettingsStatus.Success)
            {
                if (apply?.Status == MiracastReceiverApplySettingsStatus.AccessDenied)
                {
                    UltimoFoiAcessoNegado = true;
                    Log.Info("[miracast] AccessDenied. Causas conhecidas: sem identidade de pacote; outro receptor ativo " +
                             "(app Conectar / \"Projetar neste PC\"); app sem janela em primeiro plano.");
                    RegistrarSuspeitos();
                }
                return false;
            }

            var session = receiver.CreateSession(null);
            session.AllowConnectionTakeover = true;
            session.MaxSimultaneousConnections = 1;
            session.ConnectionCreated += AoConectar;
            session.MediaSourceCreated += AoReceberStream;
            session.Disconnected += AoDesconectar;

            // Start() as vezes trava para sempre (visto na Fase 1 sem identidade): limite de 20 s.
            var start = Task.Run(() => session.Start());
            if (!start.Wait(TimeSpan.FromSeconds(20)))
            {
                Log.Info("[miracast] TIMEOUT: session.Start() nao respondeu em 20 s.");
                session.Dispose();
                return false;
            }

            var r = start.Result;
            Log.Info($"[miracast] sessao: {r?.Status} {r?.ExtendedError?.Message}");
            if (r?.Status != MiracastReceiverSessionStartStatus.Success)
            {
                session.Dispose();
                return false;
            }

            lock (_trava)
            {
                _receiver = receiver;
                _session = session;
            }
            Log.Info($"[miracast] escutando como \"{_nomeTela}\".");
            return true;
        }
        catch (Exception ex)
        {
            Log.Erro("iniciando o receptor", ex);
            return false;
        }
    }

    /// <summary>Diagnostico: processos que podem estar segurando o receptor Miracast.</summary>
    private static void RegistrarSuspeitos()
    {
        try
        {
            var termos = new[] { "connect", "wirelessdisplay", "miracast", "projection", "displayswitch", "dexbridge", "dexter" };
            var eu = Environment.ProcessId;
            var achados = System.Diagnostics.Process.GetProcesses()
                .Where(p => p.Id != eu) // outra instancia (DeXter ou o antigo DexBridge) tambem segura o receptor
                .Select(p => $"{p.ProcessName} (pid {p.Id})")
                .Where(n => termos.Any(t => n.Contains(t, StringComparison.OrdinalIgnoreCase)))
                .Distinct()
                .ToList();
            Log.Info($"[miracast] processos suspeitos: {(achados.Count == 0 ? "nenhum" : string.Join(", ", achados))}");
        }
        catch (Exception ex)
        {
            Log.Erro("listando processos", ex);
        }
    }

    private void AoConectar(MiracastReceiverSession s, MiracastReceiverConnectionCreatedEventArgs e)
    {
        var nome = e.Connection?.Transmitter?.Name ?? "celular";
        Log.Info($"[miracast] conectado: {nome} ({e.Connection?.Transmitter?.MacAddress})");
        lock (_trava) _conexao = e.Connection;
        Conectado?.Invoke(nome);
    }

    private void AoReceberStream(MiracastReceiverSession s, MiracastReceiverMediaSourceCreatedEventArgs e)
    {
        Log.Info("[miracast] stream recebido; mantendo a sessao viva (reproducao silenciosa).");
        try
        {
            MediaPlayer novo = new()
            {
                IsMuted = true,
                RealTimePlayback = true,
                Source = e.MediaSource,
            };
            MediaPlayer? antigo;
            lock (_trava)
            {
                antigo = _player;
                _player = novo;
            }
            antigo?.Dispose();
            novo.Play();
        }
        catch (Exception ex)
        {
            Log.Erro("iniciando reproducao silenciosa", ex);
        }
        StreamIniciado?.Invoke();
    }

    private void AoDesconectar(MiracastReceiverSession s, MiracastReceiverDisconnectedEventArgs e)
    {
        var nome = e.Connection?.Transmitter?.Name ?? "celular";
        Log.Info($"[miracast] desconectado: {nome}");
        MediaPlayer? player;
        lock (_trava)
        {
            player = _player;
            _player = null;
            _conexao = null;
        }
        try { player?.Dispose(); } catch { /* ignora */ }
        Desconectado?.Invoke(nome);
    }

    private void PararInterno()
    {
        MediaPlayer? player;
        MiracastReceiverConnection? conexao;
        MiracastReceiverSession? session;
        lock (_trava)
        {
            player = _player;
            conexao = _conexao;
            session = _session;
            _player = null;
            _conexao = null;
            _session = null;
            _receiver = null;
        }

        if (player == null && conexao == null && session == null)
        {
            return;
        }

        Log.Info("[miracast] encerrando sessao...");
        try { player?.Dispose(); } catch (Exception ex) { Log.Erro("descartando player", ex); }
        try { conexao?.Disconnect(MiracastReceiverDisconnectReason.Finished); } catch (Exception ex) { Log.Erro("desconectando celular", ex); }
        if (session != null)
        {
            session.ConnectionCreated -= AoConectar;
            session.MediaSourceCreated -= AoReceberStream;
            session.Disconnected -= AoDesconectar;
            try { session.Dispose(); } catch (Exception ex) { Log.Erro("descartando sessao", ex); }
        }
        Log.Info("[miracast] sessao encerrada.");
    }
}

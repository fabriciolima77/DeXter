// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Win32;

namespace DeXter;

internal enum Estado
{
    Iniciando,   // cinza
    Aguardando,  // azul: esperando o celular aparecer no adb (cabo ou Wi-Fi)
    Conectando,  // amarelo: abrindo o DeX / a tela do celular
    Ativo,       // verde: DeX (ou tela do celular) na janela do scrcpy
    Pausado,     // verde-claro: janela fechada ou DeX encerrado pelo usuario
    Erro,        // vermelho
}

/// <summary>
/// App de bandeja: abre o Samsung DeX numa janela do scrcpy.
/// DeX nativo (padrao): o celular conecta o DeX sem fio nele mesmo (servidor em server\dexter-server.jar).
/// Compatibilidade: display simulado das Opcoes do desenvolvedor (mostra uma janelinha no celular).
/// Tela do celular: espelha a tela normal do celular, sem o DeX.
/// Toda mudanca de estado acontece na thread da UI (eventos de fundo passam por NaUi).
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly Config _cfg;
    private readonly NotifyIcon _icone;
    private readonly ToolStripMenuItem _itemStatus;
    private readonly ToolStripMenuItem _itemAbrir;
    private readonly ToolStripMenuItem _itemCelular;
    private readonly ToolStripMenuItem _itemEncerrarDex;
    private readonly ToolStripMenuItem _itemTela;
    private readonly ToolStripMenuItem _itemIniciarComWindows;
    private readonly ToolStripMenuItem _menuResolucao = new("Resolucao do DeX");
    private readonly ToolStripMenuItem _menuEscala = new("Tamanho dos itens do DeX");
    private readonly ToolStripMenuItem _menuQualidade = new("Qualidade do video");
    private readonly ToolStripMenuItem _menuSom = new("Som");
    private readonly ToolStripMenuItem _menuModoDex = new("Modo do DeX");
    private readonly ToolStripMenuItem _itemTelaCheia;
    private readonly ToolStripMenuItem _itemModoAuto;
    private readonly ToolStripMenuItem _itemModoUsb;
    private readonly ToolStripMenuItem _itemModoWifi;
    private readonly Control _ui;
    private readonly System.Windows.Forms.Timer _watchdog;
    private readonly Scrcpy? _scrcpy;
    private readonly Adb? _adb;
    private readonly DexNativo? _nativo;
    private readonly Dictionary<Estado, Icon> _icones = new();

    private Estado _estado = Estado.Iniciando;
    private Action? _aoClicarAviso;    // acao do ultimo aviso (balao) clicavel
    private bool _resolucaoAplicada;   // ha override de tamanho/densidade no display do DeX (modo nativo)
    private CancellationTokenSource? _ctsDescoberta;
    private int _falhasScrcpy;
    private bool _saindo;
    private bool _semOpcoesTela;       // o scrcpy recusou --turn-screen-off/--screen-off-timeout nesta sessao
    private bool _abrindo;             // AbrirDexAsync em andamento
    private bool _sessaoEncerrada;     // usuario encerrou (nao reabrir sozinho)
    private bool _modoCelular;         // espelhando a tela normal do celular (sem DeX)
    private bool _nativoIndisponivel;  // modo auto: o celular nao aceitou o DeX nativo nesta execucao
    private bool _usandoSimulado;      // a sessao atual do DeX esta no display simulado
    private string? _serialSimulado;   // celular onde criamos o display simulado (para remover ao sair)
    private string? _serialNativo;     // celular onde o DeX nativo esta ativo (para encerrar ao sair)
    private int _ticks;

    /// <summary>Ha algo para manter aberto (DeX ou tela do celular).</summary>
    private bool SessaoAtiva => !_sessaoEncerrada;

    private string ModoDex => _cfg.ModoDex is "nativo" or "compatibilidade" ? _cfg.ModoDex : "auto";

    private const string TextoAguardando = "Conecte o celular (cabo ou Wi-Fi do adb)";

    public TrayApp()
    {
        _cfg = Config.Carregar();

        // Controle invisivel so para voltar para a thread da UI
        _ui = new Control();
        _ = _ui.Handle; // forca a criacao do handle (BeginInvoke precisa dele)

        foreach (Estado e in Enum.GetValues<Estado>())
        {
            _icones[e] = CriarIcone(e);
        }

        _itemStatus = new ToolStripMenuItem("Iniciando...") { Enabled = false };
        _itemAbrir = new ToolStripMenuItem("Abrir o DeX", null, (_, _) => _ = VoltarParaDexAsync());
        _itemCelular = new ToolStripMenuItem("Abrir a tela do celular (sem DeX)", null, (_, _) => _ = AbrirCelularAsync());
        _itemEncerrarDex = new ToolStripMenuItem("Encerrar o DeX no celular", null, (_, _) => _ = EncerrarDexAsync());
        _itemTela = new ToolStripMenuItem("Apagar a tela do celular durante o uso", null, (_, _) => AlternarTela())
        {
            Checked = _cfg.DesligarTelaCelular,
        };
        _itemModoAuto = new ToolStripMenuItem("Automatico (USB, senao Wi-Fi)", null, (_, _) => DefinirModo("auto"));
        _itemModoUsb = new ToolStripMenuItem("Somente USB", null, (_, _) => DefinirModo("usb"));
        _itemModoWifi = new ToolStripMenuItem("Somente Wi-Fi", null, (_, _) => DefinirModo("wifi"));
        var menuConexao = new ToolStripMenuItem("Conexao com o celular");
        menuConexao.DropDownItems.Add(_itemModoAuto);
        menuConexao.DropDownItems.Add(_itemModoUsb);
        menuConexao.DropDownItems.Add(_itemModoWifi);
        menuConexao.DropDownItems.Add(new ToolStripSeparator());
        menuConexao.DropDownItems.Add(new ToolStripMenuItem("Preparar Wi-Fi pelo cabo (adb tcpip)", null, (_, _) => _ = PrepararWifiPeloCaboAsync()));
        menuConexao.DropDownItems.Add(new ToolStripMenuItem("Parear sem cabo (Depuracao sem fio)...", null, (_, _) => _ = ParearSemCaboAsync()));
        menuConexao.DropDownItems.Add(new ToolStripMenuItem("Conectar por Wi-Fi agora", null, (_, _) => _ = ConectarWifiAgoraAsync()));

        _itemTelaCheia = new ToolStripMenuItem("Abrir em tela cheia", null, (_, _) => AlternarTelaCheia());
        MontarMenusResolucao();
        MontarMenusSomEModo();
        _itemIniciarComWindows = new ToolStripMenuItem("Iniciar com o Windows", null, (_, _) => AlternarInicio());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_itemStatus);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_itemAbrir);
        menu.Items.Add(_itemCelular);
        menu.Items.Add(_itemEncerrarDex);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_itemTela);
        menu.Items.Add(_menuResolucao);
        menu.Items.Add(_menuEscala);
        menu.Items.Add(_menuQualidade);
        menu.Items.Add(_menuSom);
        menu.Items.Add(_itemTelaCheia);
        menu.Items.Add(menuConexao);
        menu.Items.Add(_menuModoDex);
        menu.Items.Add(_itemIniciarComWindows);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Abrir log", null, (_, _) => AbrirArquivo(Log.Arquivo)));
        menu.Items.Add(new ToolStripMenuItem("Abrir configuracao", null, (_, _) => AbrirArquivo(Config.Arquivo)));
        menu.Items.Add(new ToolStripMenuItem($"Sobre o DeXter {Projeto.Versao}", null, (_, _) => AbrirSite()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Sair", null, (_, _) => _ = SairAsync()));
        menu.Opening += (_, _) => AtualizarMenu();

        _icone = new NotifyIcon
        {
            Icon = _icones[Estado.Iniciando],
            Text = "DeXter",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icone.DoubleClick += (_, _) => AoClicarDuasVezes();
        _icone.BalloonTipClicked += (_, _) =>
        {
            var acao = _aoClicarAviso;
            _aoClicarAviso = null;
            acao?.Invoke();
        };

        var exe = _cfg.AcharScrcpy();
        if (exe != null)
        {
            Log.Info($"[app] scrcpy: {exe}{(string.Equals(exe, Config.CaminhoEmbutido, StringComparison.OrdinalIgnoreCase) ? " (embutido)" : "")}");
            _scrcpy = new Scrcpy(exe);
            _adb = Adb.Criar(exe);
            if (_adb != null)
            {
                _nativo = new DexNativo(_adb);
            }
            Log.Info(_adb != null
                ? $"[app] conexao com o celular: {_cfg.Conexao}"
                : "[app] adb.exe nao encontrado ao lado do scrcpy: sem escolha USB/Wi-Fi.");
            _scrcpy.Fechou += (codigo, duracao, linha) => NaUi(() => AoFecharScrcpy(codigo, duracao, linha));
        }
        else
        {
            Log.Info("[app] scrcpy.exe nao encontrado (nem o embutido). Reinstale o DeXter ou ajuste ScrcpyPath no dexter.json.");
        }

        _watchdog = new System.Windows.Forms.Timer { Interval = 5_000 };
        _watchdog.Tick += async (_, _) => await VigiarAsync();

        SystemEvents.SessionEnding += AoEncerrarSessaoWindows;

        // So comeca depois que o loop de mensagens estiver rodando
        _ui.BeginInvoke(new Action(() => _ = IniciarAsync()));
    }

    // ---------------------------------------------------------------- ciclo de vida

    private async Task IniciarAsync()
    {
        Log.Info($"=== DeXter {Projeto.Versao} ===");
        Log.Info($"[app] modo do DeX: {ModoDex}; servidor nativo: {(DexNativo.JarLocal != null ? "presente" : "ausente")}");

        if (_scrcpy == null)
        {
            Avisar("scrcpy nao encontrado",
                $"Ajuste \"ScrcpyPath\" em {Config.Arquivo} ou reinstale o DeXter.",
                ToolTipIcon.Warning);
        }

        DefinirEstado(Estado.Aguardando, TextoAguardando);
        _watchdog.Start();
        await AbrirDexAsync(manual: false);
    }

    /// <summary>Fecha a janela e encerra o DeX (ou a tela do celular); so volta com clique.</summary>
    private async Task EncerrarDexAsync()
    {
        _sessaoEncerrada = true;
        CancelarDescoberta();
        await Task.Run(() => _scrcpy?.Fechar());
        await PararDexNoCelularAsync();
        _modoCelular = false;
        DefinirEstado(Estado.Pausado, "Encerrado - clique duas vezes no icone para abrir o DeX");
    }

    /// <summary>Encerra o DeX no celular, seja nativo ou display simulado.</summary>
    private async Task PararDexNoCelularAsync()
    {
        var serial = _serialNativo;
        _serialNativo = null;
        if (serial != null && _nativo != null)
        {
            await Task.WhenAny(_nativo.PararAsync(serial, CancellationToken.None), Task.Delay(10_000));
        }
        await RemoverDisplaySimuladoAsync();
        _resolucaoAplicada = false;
    }

    /// <summary>Menu "Abrir a tela do celular": encerra o DeX e espelha a tela normal.</summary>
    private async Task AbrirCelularAsync()
    {
        _modoCelular = true;
        _sessaoEncerrada = false;
        _falhasScrcpy = 0;
        CancelarDescoberta();
        await Task.Run(() => _scrcpy?.Fechar());
        await PararDexNoCelularAsync();
        await AbrirDexAsync(manual: true);
    }

    /// <summary>Menu "Abrir o DeX" / "Voltar para o DeX".</summary>
    private async Task VoltarParaDexAsync()
    {
        var estavaNoCelular = _modoCelular;
        _modoCelular = false;
        _sessaoEncerrada = false;
        _falhasScrcpy = 0;
        if (estavaNoCelular)
        {
            CancelarDescoberta();
            await Task.Run(() => _scrcpy?.Fechar());
        }
        await AbrirDexAsync(manual: true);
    }

    /// <summary>Remove um display simulado que tenha ficado de outra sessao (antes de abrir o DeX nativo).</summary>
    private async Task LimparDisplaySimuladoAsync(string serial, CancellationToken ct)
    {
        if (_adb == null)
        {
            return;
        }
        if (await _adb.LerDisplaySimuladoAsync(serial, ct) != null)
        {
            await _adb.DefinirDisplaySimuladoAsync(serial, null, ct);
        }
        _serialSimulado = null;
    }

    /// <summary>Cria (ou ajusta) o display simulado no celular. Retorna o tamanho usado.</summary>
    private async Task<Size> GarantirDisplaySimuladoAsync(string serial, CancellationToken ct)
    {
        var tamanho = ResolucaoDesejada();
        if (tamanho.IsEmpty)
        {
            tamanho = new Size(1920, 1080);
        }
        if (_adb == null)
        {
            return tamanho;
        }
        var dpi = (int)Math.Round(Math.Clamp(_cfg.DensidadeSimulado, 80, 640) * Math.Clamp(_cfg.EscalaDex, 50, 200) / 100.0);
        var valor = $"{tamanho.Width}x{tamanho.Height}/{dpi}";
        var atual = await _adb.LerDisplaySimuladoAsync(serial, ct);
        if (atual != valor)
        {
            await _adb.DefinirDisplaySimuladoAsync(serial, valor, ct);
            await Task.Delay(1500, ct); // o Android cria o display e o DeX sobe nele
        }
        _serialSimulado = serial;
        return tamanho;
    }

    private async Task RemoverDisplaySimuladoAsync()
    {
        var serial = _serialSimulado;
        if (_adb == null || serial == null)
        {
            return;
        }
        _serialSimulado = null;
        await Task.WhenAny(_adb.DefinirDisplaySimuladoAsync(serial, null, CancellationToken.None), Task.Delay(8000));
    }

    private async Task SairAsync()
    {
        if (_saindo)
        {
            return;
        }
        _saindo = true;
        Log.Info("[app] encerrando...");
        _watchdog.Stop();
        SystemEvents.SessionEnding -= AoEncerrarSessaoWindows;
        CancelarDescoberta();

        _icone.Text = "DeXter - encerrando...";
        var serialDex = _scrcpy?.SerialAtual;
        var displayDex = _scrcpy?.DisplayAtual;
        await Task.Run(() => _scrcpy?.Fechar());
        if (_resolucaoAplicada && _adb != null && displayDex != null && !_modoCelular)
        {
            await Task.WhenAny(_adb.AplicarDisplayAsync(serialDex ?? "", displayDex.Value, 0, 0, 0, CancellationToken.None), Task.Delay(5000));
        }
        await PararDexNoCelularAsync(); // devolve o celular ao normal
        if (_adb is { Embutido: true })
        {
            // o servidor do adb embutido ficaria rodando de dentro da pasta do app
            Log.Info("[app] encerrando o servidor do adb embutido...");
            await Task.WhenAny(_adb.PararServidorAsync(), Task.Delay(5000));
        }

        _icone.Visible = false;
        _icone.Dispose();
        foreach (var i in _icones.Values)
        {
            i.Dispose();
        }
        Log.Info("[app] encerrado.");
        ExitThread();
    }

    // ---------------------------------------------------------------- eventos

    private void AoFecharScrcpy(int codigo, TimeSpan duracao, string ultimaLinha)
    {
        if (_saindo || !SessaoAtiva)
        {
            return;
        }

        if (codigo == 0)
        {
            // Usuario fechou a janela: o DeX continua no celular, so nao reabre sozinho.
            _falhasScrcpy = 0;
            DefinirEstado(Estado.Pausado, _modoCelular
                ? "Tela do celular fechada - clique duas vezes para abrir de novo"
                : "Janela do DeX fechada - clique duas vezes para abrir");
            return;
        }

        // scrcpy recusou as opcoes de tela (versao antiga ou aparelho): tira e reabre
        if (!_semOpcoesTela && duracao < TimeSpan.FromSeconds(15) &&
            (_cfg.DesligarTelaCelular || _cfg.TimeoutTelaSegundos > 0) &&
            (ultimaLinha.Contains("unrecognized", StringComparison.OrdinalIgnoreCase) ||
             ultimaLinha.Contains("unknown option", StringComparison.OrdinalIgnoreCase) ||
             ultimaLinha.Contains("screen", StringComparison.OrdinalIgnoreCase)))
        {
            _semOpcoesTela = true;
            Log.Info($"[app] scrcpy recusou as opcoes de tela ({ultimaLinha}); reabrindo sem elas.");
            Avisar("Tela do celular", "Este scrcpy nao aceitou apagar a tela automaticamente. Use Alt+O na janela do DeX para apagar.", ToolTipIcon.Warning);
            _ = ReabrirDepoisAsync(TimeSpan.FromSeconds(1));
            return;
        }

        // Ficou bastante tempo aberto: queda pontual, zera o contador
        if (duracao > TimeSpan.FromMinutes(1))
        {
            _falhasScrcpy = 0;
        }
        _falhasScrcpy++;

        if (_falhasScrcpy > _cfg.TentativasScrcpy)
        {
            if (codigo == 2)
            {
                // celular saiu (cabo tirado / Wi-Fi caiu): o watchdog reabre quando ele voltar
                DefinirEstado(Estado.Aguardando, TextoAguardando);
                return;
            }
            DefinirEstado(Estado.Erro, "scrcpy falhou varias vezes");
            if (_falhasScrcpy != _cfg.TentativasScrcpy + 1)
            {
                return; // ja avisou
            }
            Avisar("Nao consegui abrir o DeX",
                string.IsNullOrEmpty(ultimaLinha)
                    ? "O scrcpy fechou com erro. Confira o cabo USB ou a conexao Wi-Fi do adb; depois use \"Abrir o DeX\"."
                    : $"scrcpy: {ultimaLinha}",
                ToolTipIcon.Error);
            return;
        }

        var espera = TimeSpan.FromSeconds(2 * _falhasScrcpy);
        Log.Info($"[app] scrcpy caiu (codigo {codigo}); reabrindo em {espera.TotalSeconds:F0} s (tentativa {_falhasScrcpy}/{_cfg.TentativasScrcpy}).");
        DefinirEstado(Estado.Conectando, $"Reconectando o scrcpy ({_falhasScrcpy}/{_cfg.TentativasScrcpy})...");
        _ = ReabrirDepoisAsync(espera);
    }

    private async Task ReabrirDepoisAsync(TimeSpan espera)
    {
        await Task.Delay(espera);
        if (!_saindo && SessaoAtiva)
        {
            await AbrirDexAsync(manual: false);
        }
    }

    private void AoClicarDuasVezes()
    {
        if (_scrcpy is { Aberto: true })
        {
            return;
        }
        _sessaoEncerrada = false;
        _falhasScrcpy = 0;
        _ = AbrirDexAsync(manual: true);
    }

    private void AoEncerrarSessaoWindows(object? sender, SessionEndingEventArgs e)
    {
        Log.Info("[app] Windows encerrando a sessao.");
        // Limpeza sincrona: nao da para esperar tarefas aqui
        _saindo = true;
        _scrcpy?.Fechar();
        try { Task.Run(PararDexNoCelularAsync).Wait(TimeSpan.FromSeconds(5)); } catch { /* sem tempo */ }
        _icone.Visible = false;
    }

    // ---------------------------------------------------------------- scrcpy

    private async Task AbrirDexAsync(bool manual)
    {
        if (_saindo || _scrcpy == null)
        {
            if (manual && _scrcpy == null)
            {
                Avisar("scrcpy nao encontrado", $"Ajuste \"ScrcpyPath\" em {Config.Arquivo}.", ToolTipIcon.Warning);
            }
            return;
        }
        if (manual)
        {
            _sessaoEncerrada = false;
            _falhasScrcpy = 0;
        }
        if (!SessaoAtiva || _scrcpy.Aberto || _abrindo)
        {
            return;
        }
        _abrindo = true;
        try
        {
            await AbrirInternoAsync(manual);
        }
        finally
        {
            _abrindo = false;
        }
    }

    private int _falhasNativo;

    private async Task AbrirInternoAsync(bool manual)
    {
        var scrcpy = _scrcpy!;
        CancelarDescoberta();
        var cts = new CancellationTokenSource();
        _ctsDescoberta = cts;
        DefinirEstado(Estado.Conectando, "Procurando o celular...");

        // 1. qual celular/caminho (USB ou Wi-Fi). Sem pedido do usuario, so uma olhada (o watchdog repete).
        var limite = DateTime.UtcNow + (manual ? TimeSpan.FromSeconds(_cfg.DescobertaSegundos) : TimeSpan.Zero);
        Dispositivo? disp = null;
        while (!cts.IsCancellationRequested)
        {
            disp = await Task.Run(() => EscolherDispositivoAsync(cts.Token));
            if (disp != null || DateTime.UtcNow >= limite)
            {
                break;
            }
            try
            {
                await Task.Delay(2000, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
        if (cts.IsCancellationRequested || _saindo || !SessaoAtiva)
        {
            return;
        }
        if (disp == null)
        {
            DefinirEstado(Estado.Aguardando, TextoAguardando);
            if (manual)
            {
                Avisar("Celular nao encontrado",
                    _cfg.Conexao == "usb"
                        ? "Conecte o cabo USB com a Depuracao USB ativa."
                        : "Conecte o cabo (com Depuracao USB) ou prepare o Wi-Fi em \"Conexao com o celular\".",
                    ToolTipIcon.Warning);
            }
            return;
        }
        var via = disp.Wifi ? "Wi-Fi" : "USB";
        var serial = disp.Serial;

        // 2a. tela normal do celular (sem DeX)
        if (_modoCelular)
        {
            DefinirEstado(Estado.Conectando, $"Abrindo a tela do celular ({via})...");
            if (scrcpy.Abrir(0, serial, MontarArgs(disp.Wifi, celular: true)))
            {
                DefinirEstado(Estado.Ativo, $"Tela do celular ({via})");
            }
            else
            {
                DefinirEstado(Estado.Erro, "Falha ao abrir o scrcpy");
            }
            return;
        }

        // 2b. DeX: nativo (sem janelinha) ou compatibilidade (display simulado)
        int? id = null;
        try
        {
            var tentarNativo = ModoDex == "nativo" || (ModoDex == "auto" && !_nativoIndisponivel);
            if (tentarNativo && _nativo != null)
            {
                DefinirEstado(Estado.Conectando, $"Abrindo o DeX ({via})...");
                await LimparDisplaySimuladoAsync(serial, cts.Token);
                id = await _nativo.AtivarAsync(serial,
                    async d => (await scrcpy.ListarDisplaysAsync(serial, cts.Token)).ids.Contains(d), cts.Token);
                if (id != null)
                {
                    _serialNativo = serial;
                    _usandoSimulado = false;
                    _falhasNativo = 0;
                }
                else
                {
                    _falhasNativo++;
                    var desistir = ModoDex == "auto" && (_nativo.UltimoFoiIncompativel || _falhasNativo >= 2);
                    if (desistir)
                    {
                        _nativoIndisponivel = true;
                        Avisar("Usando o modo compatibilidade",
                            $"O DeX nativo nao abriu ({_nativo.UltimoErro}). Vou usar o display simulado, que mostra uma janelinha no celular.",
                            ToolTipIcon.Info);
                    }
                    else
                    {
                        DefinirEstado(Estado.Erro, "O DeX nativo nao abriu (tento de novo)");
                        if (manual)
                        {
                            Avisar("O DeX nao abriu", $"{_nativo.UltimoErro}. Detalhes no log.", ToolTipIcon.Warning);
                        }
                        return;
                    }
                }
            }

            if (id == null)
            {
                DefinirEstado(Estado.Conectando, $"Abrindo o DeX no display simulado ({via})...");
                var tamanho = await GarantirDisplaySimuladoAsync(serial, cts.Token);
                _usandoSimulado = true;
                id = await Task.Run(() => scrcpy.DescobrirDisplayAsync(
                    serial, new HashSet<int>(), TimeSpan.FromSeconds(_cfg.DescobertaSegundos), cts.Token, tamanho), cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cts.IsCancellationRequested || _saindo || !SessaoAtiva || _modoCelular)
        {
            return;
        }
        if (ReferenceEquals(_ctsDescoberta, cts))
        {
            _ctsDescoberta = null;
        }
        cts.Dispose();

        if (id == null)
        {
            DefinirEstado(Estado.Erro, "Display do DeX nao encontrado");
            if (manual)
            {
                Avisar("Display do DeX nao encontrado",
                    $"O adb ve o celular ({via}), mas o DeX nao abriu. Confira se o DeX esta habilitado no celular. Detalhes no log.",
                    ToolTipIcon.Warning);
            }
            return;
        }

        if (!_usandoSimulado)
        {
            await AplicarResolucaoAsync(serial, id.Value); // no simulado o tamanho vem do proprio display
        }

        if (scrcpy.Abrir(id.Value, serial, MontarArgs(disp.Wifi)))
        {
            DefinirEstado(Estado.Ativo, $"DeX ativo ({(_usandoSimulado ? "compatibilidade" : "nativo")}, {via})");
        }
        else
        {
            DefinirEstado(Estado.Erro, "Falha ao abrir o scrcpy");
        }
    }

    /// <summary>Escolhe o celular conforme o modo. Sem adb, deixa o scrcpy escolher (serial vazio).</summary>
    private async Task<Dispositivo?> EscolherDispositivoAsync(CancellationToken ct, bool reconectarWifi = true)
    {
        if (_adb == null)
        {
            return new Dispositivo("", "device", false);
        }
        var d = await _adb.EscolherAsync(_cfg.Conexao, _cfg.EnderecoWifi, ct, reconectarWifi);
        var serial = d?.Serial;
        if (serial != _ultimoSerial)
        {
            _ultimoSerial = serial;
            Log.Info(d != null ? $"[adb] usando {d}" : "[adb] nenhum celular pronto");
        }
        return d;
    }

    private string? _ultimoSerial;

    private string MontarArgs(bool wifi, bool celular = false)
    {
        var args = _cfg.ScrcpyArgs;
        var qualidade = Config.Qualidades.FirstOrDefault(q => q.id == _cfg.Qualidade);
        if (celular)
        {
            // tela normal do celular: teclado fisico simulado (acentos/atalhos), como no script do usuario
            args += " --window-title=\"DeXter - Celular\" --keyboard=uhid";
        }
        else if (_cfg.TelaCheia)
        {
            args += " --fullscreen";
        }
        if (wifi && !string.IsNullOrWhiteSpace(_cfg.ScrcpyArgsWifi))
        {
            args += " " + _cfg.ScrcpyArgsWifi; // depois dos padroes: os ultimos valores valem
        }
        if (qualidade.id != null)
        {
            // vem por ultimo: no scrcpy o ultimo valor de uma opcao repetida e o que vale
            args += $" --video-bit-rate={(wifi ? qualidade.wifi : qualidade.cabo)}M";
        }
        args += _cfg.Som switch
        {
            "celular" => " --no-audio",  // som fica no celular (alto-falante ou fones do celular)
            "ambos" => " --audio-dup",   // toca no PC e no celular (Android 13+)
            _ => "",                     // padrao do scrcpy: toca no PC
        };
        if (!_semOpcoesTela)
        {
            if (_cfg.DesligarTelaCelular)
            {
                args += " --turn-screen-off";
            }
            if (_cfg.TimeoutTelaSegundos > 0)
            {
                args += $" --screen-off-timeout={_cfg.TimeoutTelaSegundos}";
            }
        }
        return args;
    }

    /// <summary>Fecha e reabre a janela do DeX (para aplicar uma opcao nova).</summary>
    private async Task ReabrirScrcpyAsync()
    {
        if (_scrcpy is not { Aberto: true })
        {
            return; // vale na proxima abertura
        }
        await Task.Run(() => _scrcpy.Fechar());
        await AbrirDexAsync(manual: true);
    }

    // ---------------------------------------------------------------- Wi-Fi e tela

    private void AlternarTela()
    {
        _cfg.DesligarTelaCelular = !_cfg.DesligarTelaCelular;
        _cfg.Salvar();
        _semOpcoesTela = false;
        Log.Info($"[app] apagar tela do celular: {_cfg.DesligarTelaCelular}");
        _ = ReabrirScrcpyAsync();
    }

    private void DefinirModo(string modo)
    {
        if (_cfg.Conexao == modo)
        {
            return;
        }
        _cfg.Conexao = modo;
        _cfg.Salvar();
        Log.Info($"[app] conexao com o celular: {modo}");
        _ = ReabrirScrcpyAsync();
    }

    private async Task PrepararWifiPeloCaboAsync()
    {
        if (_adb == null)
        {
            Avisar("adb nao encontrado", "O adb.exe precisa estar na pasta do scrcpy.", ToolTipIcon.Warning);
            return;
        }
        var usb = (await _adb.ListarAsync()).FirstOrDefault(d => d.Pronto && !d.Wifi);
        if (usb == null)
        {
            Avisar("Conecte o cabo", "Para preparar o Wi-Fi, o celular precisa estar no USB com a depuracao ativa (so desta vez).", ToolTipIcon.Info);
            return;
        }

        Avisar("Preparando Wi-Fi", "O adb do celular vai reiniciar; a janela do DeX pode piscar.", ToolTipIcon.Info);
        var endereco = await _adb.PrepararPeloCaboAsync(usb.Serial, CancellationToken.None);
        if (endereco == null)
        {
            Avisar("Wi-Fi nao ficou pronto", "O celular e o notebook estao na mesma rede Wi-Fi? Detalhes no log.", ToolTipIcon.Error);
            return;
        }
        _cfg.EnderecoWifi = endereco;
        _cfg.Salvar();
        Avisar("Wi-Fi pronto",
            $"Celular em {endereco}. Pode tirar o cabo: o DeX segue por Wi-Fi. Vale ate o celular reiniciar.",
            ToolTipIcon.Info);
    }

    private async Task ParearSemCaboAsync()
    {
        if (_adb == null)
        {
            Avisar("adb nao encontrado", "O adb.exe precisa estar na pasta do scrcpy.", ToolTipIcon.Warning);
            return;
        }
        var dados = PedirPareamento();
        if (dados == null)
        {
            return;
        }
        var (endereco, codigo) = dados.Value;
        if (!await _adb.ParearAsync(endereco, codigo, CancellationToken.None))
        {
            Avisar("Pareamento falhou", "Confira o IP:porta e o codigo (eles mudam a cada vez que a tela de pareamento abre).", ToolTipIcon.Error);
            return;
        }
        await Task.Delay(2000); // o celular passa a anunciar a porta de conexao por mDNS
        var ok = await _adb.ReconectarWifiAsync("", CancellationToken.None);
        Avisar(ok ? "Pareado e conectado" : "Pareado",
            ok
                ? "Pronto: o DeX pode funcionar sem cabo, inclusive depois de reiniciar o celular (com a Depuracao sem fio ligada)."
                : "Pareado, mas ainda nao conectou. Deixe a Depuracao sem fio ligada e use \"Conectar por Wi-Fi agora\".",
            ToolTipIcon.Info);
    }

    private async Task ConectarWifiAgoraAsync()
    {
        if (_adb == null)
        {
            return;
        }
        var ok = await _adb.ReconectarWifiAsync(_cfg.EnderecoWifi, CancellationToken.None);
        Avisar(ok ? "Wi-Fi conectado" : "Wi-Fi nao conectou",
            ok
                ? "O adb esta falando com o celular pela rede."
                : "Prepare o Wi-Fi pelo cabo ou pareie pela Depuracao sem fio. Detalhes no log.",
            ok ? ToolTipIcon.Info : ToolTipIcon.Warning);
        if (ok && SessaoAtiva && _scrcpy is { Aberto: false })
        {
            await AbrirDexAsync(manual: true);
        }
    }

    /// <summary>Janelinha para o IP:porta e o codigo de "Parear dispositivo com codigo de pareamento".</summary>
    private static (string endereco, string codigo)? PedirPareamento()
    {
        using var f = new Form
        {
            Text = "Parear sem cabo",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(380, 190),
            MinimizeBox = false,
            MaximizeBox = false,
            TopMost = true,
        };
        var ajuda = new Label
        {
            Text = "No celular: Opcoes do desenvolvedor > Depuracao sem fio > " +
                   "Parear dispositivo com codigo de pareamento.",
            Location = new Point(12, 10),
            Size = new Size(356, 36),
        };
        var lblEnd = new Label { Text = "IP e porta:", Location = new Point(12, 56), AutoSize = true };
        var txtEnd = new TextBox { Location = new Point(110, 52), Width = 258, PlaceholderText = "192.168.0.10:37123" };
        var lblCod = new Label { Text = "Codigo:", Location = new Point(12, 92), AutoSize = true };
        var txtCod = new TextBox { Location = new Point(110, 88), Width = 120, PlaceholderText = "123456" };
        var ok = new Button { Text = "Parear", DialogResult = DialogResult.OK, Location = new Point(212, 145), Width = 75 };
        var cancelar = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Location = new Point(293, 145), Width = 75 };
        f.Controls.AddRange(new Control[] { ajuda, lblEnd, txtEnd, lblCod, txtCod, ok, cancelar });
        f.AcceptButton = ok;
        f.CancelButton = cancelar;

        if (f.ShowDialog() != DialogResult.OK)
        {
            return null;
        }
        var end = txtEnd.Text.Trim();
        var cod = txtCod.Text.Trim();
        if (end.Length == 0 || cod.Length == 0 || end.Contains(' ') || cod.Contains(' '))
        {
            return null;
        }
        return (end, cod);
    }

    private void CancelarDescoberta()
    {
        var cts = _ctsDescoberta;
        _ctsDescoberta = null;
        if (cts != null)
        {
            try { cts.Cancel(); } catch { /* ignora */ }
        }
    }

    // ---------------------------------------------------------------- watchdog

    /// <summary>A cada 5 s: quando o celular aparece no adb (cabo ou Wi-Fi), abre o DeX/tela do celular sozinho.</summary>
    private async Task VigiarAsync()
    {
        if (_saindo || _scrcpy == null || _abrindo || _scrcpy.Aberto || _sessaoEncerrada || _estado == Estado.Pausado)
        {
            return;
        }
        if (_estado == Estado.Erro)
        {
            // depois de erro, so tenta de novo a cada ~30 s
            if (++_ticks % 6 != 0)
            {
                return;
            }
            _falhasScrcpy = 0;
        }
        else
        {
            _ticks++;
        }

        // reconectar o Wi-Fi do adb (connect/mDNS) e mais lento: so a cada ~30 s
        var reconectar = _ticks % 6 == 1;
        var disp = await Task.Run(() => EscolherDispositivoAsync(CancellationToken.None, reconectar));
        if (disp == null)
        {
            if (_estado != Estado.Aguardando)
            {
                DefinirEstado(Estado.Aguardando, TextoAguardando);
            }
            return;
        }
        await AbrirDexAsync(manual: false);
    }

    // ---------------------------------------------------------------- UI

    private void DefinirEstado(Estado estado, string texto)
    {
        if (_saindo && estado != Estado.Iniciando)
        {
            return;
        }
        if (estado != _estado || _itemStatus.Text != texto)
        {
            Log.Info($"[estado] {estado}: {texto}");
        }
        _estado = estado;
        _itemStatus.Text = texto;
        _icone.Icon = _icones[estado];
        var dica = $"DeXter - {texto}";
        _icone.Text = dica.Length > 127 ? dica[..127] : dica; // limite do Windows
    }

    // ---------------------------------------------------------------- resolucao do DeX

    private static readonly string[] Presets = { "1280x720", "1600x900", "1920x1080", "2560x1080", "2560x1440", "3440x1440" };
    private static readonly int[] Escalas = { 75, 90, 100, 115, 125, 150 };

    private void MontarMenusResolucao()
    {
        var tela = TamanhoTelaPc();
        _menuResolucao.DropDownItems.Add(ItemResolucao("Padrao (1920 x 1080)", ""));
        _menuResolucao.DropDownItems.Add(ItemResolucao($"Igual a tela do PC ({tela.Width} x {tela.Height})", "tela"));
        _menuResolucao.DropDownItems.Add(new ToolStripSeparator());
        foreach (var p in Presets)
        {
            var rotulo = p.Replace("x", " x ") + (p is "2560x1080" or "3440x1440" ? " (ultrawide)" : "");
            _menuResolucao.DropDownItems.Add(ItemResolucao(rotulo, p));
        }
        _menuResolucao.DropDownItems.Add(new ToolStripSeparator());
        _menuResolucao.DropDownItems.Add(new ToolStripMenuItem("Personalizada...", null, (_, _) => EscolherResolucaoPersonalizada()) { Tag = "*" });

        foreach (var q in Config.Qualidades)
        {
            var item = new ToolStripMenuItem($"{q.rotulo} ({q.cabo} Mbps no cabo / {q.wifi} no Wi-Fi)", null,
                (_, _) => DefinirQualidade(q.id)) { Tag = q.id };
            _menuQualidade.DropDownItems.Add(item);
        }
        _menuQualidade.DropDownItems.Add(new ToolStripSeparator());
        _menuQualidade.DropDownItems.Add(new ToolStripMenuItem("Personalizada (valores do dexter.json)", null,
            (_, _) => DefinirQualidade("")) { Tag = "" });

        foreach (var pct in Escalas)
        {
            var rotulo = pct switch
            {
                < 100 => $"Menores ({pct}%) - mais espaco",
                100 => "Padrao (100%)",
                _ => $"Maiores ({pct}%)",
            };
            var item = new ToolStripMenuItem(rotulo, null, (_, _) => DefinirEscala(pct)) { Tag = pct };
            _menuEscala.DropDownItems.Add(item);
        }
    }

    private ToolStripMenuItem ItemResolucao(string rotulo, string valor) =>
        new(rotulo, null, (_, _) => DefinirResolucao(valor)) { Tag = valor };

    private static Size TamanhoTelaPc()
    {
        var b = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        return new Size(b.Width, b.Height);
    }

    /// <summary>Resolucao pedida em pixels; (0,0) = padrao do celular.</summary>
    private Size ResolucaoDesejada()
    {
        var r = (_cfg.ResolucaoDex ?? "").Trim().ToLowerInvariant();
        if (r == "tela")
        {
            var t = TamanhoTelaPc();
            return new Size(t.Width & ~1, t.Height & ~1); // pares
        }
        var partes = r.Split('x');
        if (partes.Length == 2 && int.TryParse(partes[0], out var w) && int.TryParse(partes[1], out var h) && w >= 640 && h >= 480)
        {
            return new Size(w & ~1, h & ~1);
        }
        return Size.Empty;
    }

    /// <summary>Aplica resolucao/escala no display do DeX (antes de abrir o scrcpy).</summary>
    private async Task AplicarResolucaoAsync(string serial, int displayId)
    {
        if (_adb == null)
        {
            return;
        }
        var tamanho = ResolucaoDesejada();
        var escala = Math.Clamp(_cfg.EscalaDex, 50, 200);
        var padrao = tamanho.IsEmpty && escala == 100;

        var densidade = 0;
        if (escala != 100)
        {
            var original = await _adb.LerDisplayAsync(serial, displayId, CancellationToken.None);
            if (original != null)
            {
                densidade = (int)Math.Round(original.Value.densidade * escala / 100.0);
            }
        }

        // no padrao tambem manda "reset": o Android pode guardar o override de uma sessao anterior
        var ok = await _adb.AplicarDisplayAsync(serial, displayId, tamanho.Width, tamanho.Height, densidade, CancellationToken.None);
        _resolucaoAplicada = !tamanho.IsEmpty || densidade > 0;
        if (!ok && !padrao)
        {
            Avisar("Resolucao do DeX", "O celular recusou a mudanca de resolucao. Detalhes no log.", ToolTipIcon.Warning);
        }
    }

    private void DefinirResolucao(string valor)
    {
        if (_cfg.ResolucaoDex == valor)
        {
            return;
        }
        _cfg.ResolucaoDex = valor;
        _cfg.Salvar();
        Log.Info($"[app] resolucao do DeX: {(valor == "" ? "padrao" : valor)}");
        _ = ReabrirScrcpyAsync();
    }

    private void DefinirEscala(int pct)
    {
        if (_cfg.EscalaDex == pct)
        {
            return;
        }
        _cfg.EscalaDex = pct;
        _cfg.Salvar();
        Log.Info($"[app] tamanho dos itens do DeX: {pct}%");
        _ = ReabrirScrcpyAsync();
    }

    private void DefinirQualidade(string id)
    {
        if (_cfg.Qualidade == id)
        {
            return;
        }
        _cfg.Qualidade = id;
        _cfg.Salvar();
        Log.Info($"[app] qualidade do video: {(id == "" ? "personalizada" : id)}");
        _ = ReabrirScrcpyAsync(); // so a janela reabre; a sessao do DeX continua
    }

    private void AlternarTelaCheia()
    {
        _cfg.TelaCheia = !_cfg.TelaCheia;
        _cfg.Salvar();
        _ = ReabrirScrcpyAsync();
    }

    private void EscolherResolucaoPersonalizada()
    {
        var atual = ResolucaoDesejada();
        if (atual.IsEmpty)
        {
            atual = TamanhoTelaPc();
        }

        using var f = new Form
        {
            Text = "Resolucao do DeX",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(300, 130),
            MinimizeBox = false,
            MaximizeBox = false,
            TopMost = true,
        };
        var lblL = new Label { Text = "Largura:", Location = new Point(12, 18), AutoSize = true };
        var numL = new NumericUpDown { Location = new Point(90, 14), Width = 90, Minimum = 640, Maximum = 7680, Increment = 2, Value = Math.Clamp(atual.Width, 640, 7680) };
        var lblA = new Label { Text = "Altura:", Location = new Point(12, 52), AutoSize = true };
        var numA = new NumericUpDown { Location = new Point(90, 48), Width = 90, Minimum = 480, Maximum = 4320, Increment = 2, Value = Math.Clamp(atual.Height, 480, 4320) };
        var ok = new Button { Text = "Aplicar", DialogResult = DialogResult.OK, Location = new Point(132, 92), Width = 75 };
        var cancelar = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Location = new Point(213, 92), Width = 75 };
        f.Controls.AddRange(new Control[] { lblL, numL, lblA, numA, ok, cancelar });
        f.AcceptButton = ok;
        f.CancelButton = cancelar;

        if (f.ShowDialog() == DialogResult.OK)
        {
            DefinirResolucao($"{(int)numL.Value}x{(int)numA.Value}");
        }
    }

    // ---------------------------------------------------------------- iniciar com o Windows

    private static void AbrirSite() => AbrirUri(Projeto.Url);

    private static void AbrirUri(string uri)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Erro($"abrindo {uri}", ex);
        }
    }

    private void AtualizarMenu()
    {
        var aberto = _scrcpy is { Aberto: true };
        _itemAbrir.Text = _modoCelular ? "Voltar para o DeX" : "Abrir o DeX";
        _itemAbrir.Enabled = _scrcpy != null && (_modoCelular || !aberto);
        _itemCelular.Enabled = _scrcpy != null && !_modoCelular;
        _itemEncerrarDex.Text = _modoCelular ? "Fechar a tela do celular" : "Encerrar o DeX no celular";
        _itemEncerrarDex.Enabled = !_sessaoEncerrada;
        _itemTela.Checked = _cfg.DesligarTelaCelular;
        _itemTelaCheia.Checked = _cfg.TelaCheia;
        _itemIniciarComWindows.Checked = IniciarComWindows.Ligado;
        foreach (ToolStripItem i in _menuResolucao.DropDownItems)
        {
            if (i is ToolStripMenuItem m && m.Tag is string valor)
            {
                m.Checked = valor == "*"
                    ? !string.IsNullOrEmpty(_cfg.ResolucaoDex) && _cfg.ResolucaoDex != "tela" && !Presets.Contains(_cfg.ResolucaoDex)
                    : valor == _cfg.ResolucaoDex;
                if (valor == "*" && m.Checked)
                {
                    m.Text = $"Personalizada ({_cfg.ResolucaoDex.Replace("x", " x ")})...";
                }
            }
        }
        foreach (ToolStripItem i in _menuQualidade.DropDownItems)
        {
            if (i is ToolStripMenuItem m && m.Tag is string id)
            {
                m.Checked = id == (_cfg.Qualidade ?? "");
            }
        }
        foreach (ToolStripItem i in _menuEscala.DropDownItems)
        {
            if (i is ToolStripMenuItem m && m.Tag is int pct)
            {
                m.Checked = pct == _cfg.EscalaDex;
            }
        }
        foreach (ToolStripItem i in _menuSom.DropDownItems)
        {
            if (i is ToolStripMenuItem m && m.Tag is string som)
            {
                m.Checked = som == (_cfg.Som is "celular" or "ambos" ? _cfg.Som : "pc");
            }
        }
        foreach (ToolStripItem i in _menuModoDex.DropDownItems)
        {
            if (i is ToolStripMenuItem m && m.Tag is string modo)
            {
                m.Checked = modo == ModoDex;
            }
        }
        _itemModoAuto.Checked = _cfg.Conexao == "auto";
        _itemModoUsb.Checked = _cfg.Conexao == "usb";
        _itemModoWifi.Checked = _cfg.Conexao == "wifi";
    }

    private void MontarMenusSomEModo()
    {
        foreach (var (id, rotulo) in new[]
                 {
                     ("pc", "No PC"),
                     ("celular", "No celular (alto-falante ou fones do celular)"),
                     ("ambos", "Nos dois (Android 13+)"),
                 })
        {
            _menuSom.DropDownItems.Add(new ToolStripMenuItem(rotulo, null, (_, _) => DefinirSom(id)) { Tag = id });
        }

        foreach (var (id, rotulo) in new[]
                 {
                     ("auto", "Automatico (nativo; se nao der, compatibilidade)"),
                     ("nativo", "Nativo (sem janelinha no celular)"),
                     ("compatibilidade", "Compatibilidade (display simulado, com janelinha)"),
                 })
        {
            _menuModoDex.DropDownItems.Add(new ToolStripMenuItem(rotulo, null, (_, _) => _ = DefinirModoDexAsync(id)) { Tag = id });
        }
    }

    private void DefinirSom(string som)
    {
        if (_cfg.Som == som)
        {
            return;
        }
        _cfg.Som = som;
        _cfg.Salvar();
        Log.Info($"[app] som: {som}");
        _ = ReabrirScrcpyAsync();
    }

    private async Task DefinirModoDexAsync(string modo)
    {
        if (ModoDex == modo)
        {
            return;
        }
        _cfg.ModoDex = modo;
        _cfg.Salvar();
        _nativoIndisponivel = false;
        _falhasNativo = 0;
        Log.Info($"[app] modo do DeX: {modo}");
        if (_modoCelular || _sessaoEncerrada)
        {
            return; // vale na proxima vez que o DeX abrir
        }
        CancelarDescoberta();
        await Task.Run(() => _scrcpy?.Fechar());
        await PararDexNoCelularAsync();
        await AbrirDexAsync(manual: true);
    }

    private void AlternarInicio()
    {
        try
        {
            var novo = !IniciarComWindows.Ligado;
            IniciarComWindows.Definir(novo);
            Log.Info($"[app] iniciar com o Windows: {novo}");
        }
        catch (Exception ex)
        {
            Log.Erro("alterando o inicio com o Windows", ex);
        }
    }

    private void Avisar(string titulo, string texto, ToolTipIcon tipo, Action? aoClicar = null)
    {
        if (_saindo)
        {
            return;
        }
        _aoClicarAviso = aoClicar;
        Log.Info($"[aviso] {titulo}: {texto}");
        _icone.ShowBalloonTip(5000, titulo, texto, tipo);
    }

    private void NaUi(Action acao)
    {
        if (_saindo || _ui.IsDisposed)
        {
            return;
        }
        try
        {
            _ui.BeginInvoke(acao);
        }
        catch (Exception ex)
        {
            Log.Erro("despachando para a UI", ex);
        }
    }

    private static void AbrirArquivo(string caminho)
    {
        try
        {
            if (!File.Exists(caminho))
            {
                File.WriteAllText(caminho, "");
            }
            // notepad explicito: abre ate .log/.json sem associacao
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", $"\"{caminho}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Erro($"abrindo {caminho}", ex);
        }
    }

    private static Icon CriarIcone(Estado estado)
    {
        var cor = estado switch
        {
            Estado.Iniciando => Color.FromArgb(140, 140, 140),
            Estado.Aguardando => Color.FromArgb(40, 120, 220),
            Estado.Conectando => Color.FromArgb(235, 170, 20),
            Estado.Ativo => Color.FromArgb(30, 170, 80),
            Estado.Pausado => Color.FromArgb(120, 200, 140),
            _ => Color.FromArgb(215, 50, 50),
        };

        // icone do app (embutido no exe) + bolinha de status no canto inferior direito
        const int tam = 32;
        using var bmp = new Bitmap(tam, tam);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            using var recurso = typeof(TrayApp).Assembly.GetManifestResourceStream("DeXter.Icone.png");
            if (recurso != null)
            {
                using var logo = Image.FromStream(recurso);
                g.DrawImage(logo, 0, 0, tam, tam);
            }

            const float d = tam * 0.42f;   // diametro da bolinha
            const float x = tam - d;
            const float y = tam - d;
            using var contorno = new SolidBrush(Color.FromArgb(255, 10, 10, 20));
            g.FillEllipse(contorno, x - 1.5f, y - 1.5f, d + 1.5f, d + 1.5f);
            using var fundo = new SolidBrush(cor);
            g.FillEllipse(fundo, x + 0.5f, y + 0.5f, d - 2.5f, d - 2.5f);
        }

        // handle mantido vivo de proposito (sao so 6 icones, criados uma vez)
        return Icon.FromHandle(bmp.GetHicon());
    }
}

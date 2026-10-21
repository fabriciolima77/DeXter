// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Win32;
using Windows.Media.Miracast;

namespace DeXter;

internal enum Estado
{
    Iniciando,   // cinza
    Aguardando,  // azul: receptor escutando, sem celular
    Conectando,  // amarelo: celular conectado, procurando display / abrindo scrcpy
    Ativo,       // verde: DeX na janela do scrcpy
    Pausado,     // verde-claro: DeX conectado, janela fechada pelo usuario
    Erro,        // vermelho
}

/// <summary>
/// App de bandeja: orquestra receptor Miracast + scrcpy.
/// Toda mudanca de estado acontece na thread da UI (eventos de fundo passam por NaUi).
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly Config _cfg;
    private readonly NotifyIcon _icone;
    private readonly ToolStripMenuItem _itemStatus;
    private readonly ToolStripMenuItem _itemAbrir;
    private readonly ToolStripMenuItem _itemReiniciar;
    private readonly Control _ui;
    private readonly System.Windows.Forms.Timer _watchdog;
    private readonly MiracastHost _miracast;
    private readonly Scrcpy? _scrcpy;
    private readonly Adb? _adb;
    private readonly ToolStripMenuItem _itemTela;
    private readonly ToolStripMenuItem _itemIniciarComWindows;
    private readonly ToolStripMenuItem _menuResolucao = new("Resolucao do DeX");
    private readonly ToolStripMenuItem _menuEscala = new("Tamanho dos itens do DeX");
    private readonly ToolStripMenuItem _itemTelaCheia;
    private bool _resolucaoAplicada; // ha override de tamanho/densidade no display do DeX
    private readonly ToolStripMenuItem _itemModoAuto;
    private readonly ToolStripMenuItem _itemModoUsb;
    private readonly ToolStripMenuItem _itemModoWifi;
    private readonly Dictionary<Estado, Icon> _icones = new();

    private Estado _estado = Estado.Iniciando;
    private CancellationTokenSource? _ctsDescoberta;
    private Task<HashSet<int>>? _linhaDeBase;
    private int _falhasScrcpy;
    private int _falhasReceptor;
    private int _avisosNaoEscutando;
    private bool _reiniciando;
    private bool _saindo;
    private bool _semOpcoesTela; // o scrcpy recusou --turn-screen-off/--screen-off-timeout nesta sessao

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
        _itemAbrir = new ToolStripMenuItem("Abrir janela do DeX", null, (_, _) => _ = AbrirDexAsync(manual: true));
        _itemReiniciar = new ToolStripMenuItem("Reiniciar receptor", null, (_, _) => _ = ReiniciarReceptorAsync("pedido do usuario"));
        _itemTela = new ToolStripMenuItem("Apagar a tela do celular durante o DeX", null, (_, _) => AlternarTela())
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

        var menu = new ContextMenuStrip();
        menu.Items.Add(_itemStatus);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_itemAbrir);
        menu.Items.Add(_itemReiniciar);
        _itemTelaCheia = new ToolStripMenuItem("Abrir em tela cheia", null, (_, _) => AlternarTelaCheia());
        MontarMenusResolucao();
        menu.Items.Add(_itemTela);
        menu.Items.Add(_menuResolucao);
        menu.Items.Add(_menuEscala);
        menu.Items.Add(_itemTelaCheia);
        menu.Items.Add(menuConexao);
        _itemIniciarComWindows = new ToolStripMenuItem("Iniciar com o Windows", null, (_, _) => _ = AlternarInicioAsync());
        menu.Items.Add(_itemIniciarComWindows);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Abrir log", null, (_, _) => AbrirArquivo(Log.Arquivo)));
        menu.Items.Add(new ToolStripMenuItem("Abrir configuracao", null, (_, _) => AbrirArquivo(Config.Arquivo)));
        menu.Items.Add(new ToolStripMenuItem($"Sobre o DeXter {Projeto.Versao}", null, (_, _) => AbrirSite()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Sair", null, (_, _) => _ = SairAsync()));
        menu.Opening += (_, _) => AtualizarMenu();
        _ = AtualizarInicioAsync();

        _icone = new NotifyIcon
        {
            Icon = _icones[Estado.Iniciando],
            Text = "DeXter",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icone.DoubleClick += (_, _) => AoClicarDuasVezes();

        _miracast = new MiracastHost(_cfg.NomeTelaEfetivo);
        _miracast.Conectado += nome => NaUi(() => AoConectar(nome));
        _miracast.StreamIniciado += () => NaUi(() => _ = AbrirDexAsync(manual: false));
        _miracast.Desconectado += nome => NaUi(() => AoDesconectar(nome));

        var exe = _cfg.AcharScrcpy();
        if (exe != null)
        {
            Log.Info($"[app] scrcpy: {exe}{(string.Equals(exe, Config.CaminhoEmbutido, StringComparison.OrdinalIgnoreCase) ? " (embutido)" : "")}");
            _scrcpy = new Scrcpy(exe);
            _adb = Adb.Criar(exe);
            Log.Info(_adb != null
                ? $"[app] conexao com o celular: {_cfg.Conexao}"
                : "[app] adb.exe nao encontrado ao lado do scrcpy: sem escolha USB/Wi-Fi.");
            _scrcpy.Fechou += (codigo, duracao, linha) => NaUi(() => AoFecharScrcpy(codigo, duracao, linha));
        }
        else
        {
            Log.Info("[app] scrcpy.exe nao encontrado (nem o embutido). Reinstale o DeXter ou ajuste ScrcpyPath no dexter.json.");
        }

        _watchdog = new System.Windows.Forms.Timer { Interval = 10_000 };
        _watchdog.Tick += async (_, _) => await VigiarAsync();

        SystemEvents.PowerModeChanged += AoMudarEnergia;
        SystemEvents.SessionEnding += AoEncerrarSessaoWindows;

        // So comeca depois que o loop de mensagens estiver rodando
        _ui.BeginInvoke(new Action(() => _ = IniciarAsync()));
    }

    // ---------------------------------------------------------------- ciclo de vida

    private async Task IniciarAsync()
    {
        Log.Info($"=== DeXter {Projeto.Versao} ===");

        if (!TemIdentidadeDePacote())
        {
            DefinirEstado(Estado.Erro, "Sem identidade de pacote");
            Log.Info("[app] sem identidade de pacote: a API Miracast vai negar acesso.");
            MessageBox.Show(
                "O DeXter precisa ser iniciado como pacote:\n\n" +
                "  • pelo menu Iniciar (\"DeXter\"), ou\n" +
                "  • pelo comando  dexter  no terminal.\n\n" +
                "Executar o DeXter.exe direto da pasta bin nao funciona.",
                "DeXter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            await SairAsync();
            return;
        }

        if (_scrcpy == null)
        {
            Avisar("scrcpy nao encontrado",
                $"O receptor vai funcionar, mas sem a janela do DeX. Ajuste \"ScrcpyPath\" em {Config.Arquivo}.",
                ToolTipIcon.Warning);
        }

        await ReiniciarReceptorAsync("inicio");
        _watchdog.Start();
    }

    private async Task ReiniciarReceptorAsync(string motivo)
    {
        if (_reiniciando || _saindo)
        {
            return;
        }
        _reiniciando = true;
        try
        {
            Log.Info($"[app] (re)iniciando receptor: {motivo}");
            CancelarDescoberta();
            _scrcpy?.Fechar();
            DefinirEstado(Estado.Iniciando, "Iniciando receptor...");

            var ok = await _miracast.IniciarAsync();
            if (!ok && _miracast.UltimoFoiAcessoNegado && !_saindo &&
                motivo is "inicio" or "pedido do usuario") // nao abre janela nas tentativas automaticas
            {
                // Hipotese: a API so libera o receptor para um app com janela em primeiro plano
                // (na Fase 1 a janela do console cumpria esse papel).
                ok = await TentarComJanelaAsync();
            }
            if (_saindo)
            {
                return;
            }

            if (ok)
            {
                _falhasReceptor = 0;
                _avisosNaoEscutando = 0;
                DefinirEstado(Estado.Aguardando, $"Aguardando o celular (\"{_cfg.NomeTelaEfetivo}\")");
            }
            else
            {
                _falhasReceptor++;
                DefinirEstado(Estado.Erro, "Receptor Miracast nao iniciou");
                if (_falhasReceptor == 1)
                {
                    Avisar("Receptor nao iniciou",
                        _miracast.UltimoFoiAcessoNegado
                            ? "Acesso negado pelo Windows. Feche o app \"Conectar\" e desative \"Projetar neste PC\" nas Configuracoes. Vou tentar de novo sozinho."
                            : "Feche o app \"Conectar\" (so um receptor Miracast roda por vez) e confira o Wi-Fi. Vou tentar de novo sozinho.",
                        ToolTipIcon.Error);
                }
            }
        }
        finally
        {
            _reiniciando = false;
        }
    }

    /// <summary>Repete a inicializacao com uma janelinha visivel e ativa.</summary>
    private async Task<bool> TentarComJanelaAsync()
    {
        using var janela = new Form
        {
            Text = "DeXter",
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(340, 70),
            TopMost = true,
            ShowInTaskbar = true,
            MinimizeBox = false,
            MaximizeBox = false,
        };
        janela.Controls.Add(new Label
        {
            Text = "Ativando o receptor Miracast...",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
        });
        janela.Show();
        janela.Activate();
        await Task.Delay(500);

        var emPrimeiroPlano = GetForegroundWindow() == janela.Handle;
        Log.Info($"[app] tentando de novo com janela visivel (primeiro plano: {(emPrimeiroPlano ? "sim" : "nao")})...");
        var ok = await _miracast.IniciarAsync();
        Log.Info($"[app] resultado com janela visivel: {(ok ? "OK - receptor ativo" : "falhou")}");
        janela.Close();
        return ok;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private async Task SairAsync()
    {
        if (_saindo)
        {
            return;
        }
        _saindo = true;
        Log.Info("[app] encerrando...");
        _watchdog.Stop();
        SystemEvents.PowerModeChanged -= AoMudarEnergia;
        SystemEvents.SessionEnding -= AoEncerrarSessaoWindows;
        CancelarDescoberta();

        _icone.Text = "DeXter - encerrando...";
        var serialDex = _scrcpy?.SerialAtual;
        var displayDex = _scrcpy?.DisplayAtual;
        await Task.Run(() => _scrcpy?.Fechar());
        if (_resolucaoAplicada && _adb != null && displayDex != null && _miracast.TemConexao)
        {
            // devolve o DeX ao tamanho original no celular
            await Task.WhenAny(_adb.AplicarDisplayAsync(serialDex ?? "", displayDex.Value, 0, 0, 0, CancellationToken.None), Task.Delay(5000));
        }
        if (_adb is { Embutido: true })
        {
            // o servidor do adb embutido ficaria rodando de dentro da pasta do app
            Log.Info("[app] encerrando o servidor do adb embutido...");
            await Task.WhenAny(_adb.PararServidorAsync(), Task.Delay(5000));
        }
        await Task.WhenAny(_miracast.PararAsync(), Task.Delay(5000));

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

    private void AoConectar(string nome)
    {
        _falhasScrcpy = 0;
        DefinirEstado(Estado.Conectando, $"{nome} conectado, aguardando o DeX...");

        // Displays que ja existiam antes do DeX criar o dele (ajuda a escolher o certo)
        if (_scrcpy != null)
        {
            var scrcpy = _scrcpy;
            _linhaDeBase = Task.Run(async () =>
            {
                var disp = await EscolherDispositivoAsync(CancellationToken.None);
                if (disp == null)
                {
                    return new HashSet<int>();
                }
                var (ids, _) = await scrcpy.ListarDisplaysAsync(disp.Serial, CancellationToken.None);
                return ids.ToHashSet();
            });
        }
    }

    private void AoDesconectar(string nome)
    {
        if (_saindo)
        {
            return;
        }
        CancelarDescoberta();
        _linhaDeBase = null;
        _resolucaoAplicada = false; // o display do DeX deixou de existir
        _ = Task.Run(() => _scrcpy?.Fechar());
        DefinirEstado(Estado.Aguardando, $"Aguardando o celular (\"{_cfg.NomeTelaEfetivo}\")");
        Avisar("DeX desconectado",
            $"{nome} saiu da tela \"{_cfg.NomeTelaEfetivo}\". Para voltar: no celular, DeX → conectar a TV/monitor.",
            ToolTipIcon.Info);
    }

    private void AoFecharScrcpy(int codigo, TimeSpan duracao, string ultimaLinha)
    {
        if (_saindo || !_miracast.TemConexao)
        {
            return;
        }

        if (codigo == 0)
        {
            // Usuario fechou a janela: o DeX continua no celular, so nao reabre sozinho.
            _falhasScrcpy = 0;
            DefinirEstado(Estado.Pausado, "DeX conectado (janela fechada) - clique duas vezes para abrir");
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
            DefinirEstado(Estado.Erro, "scrcpy falhou varias vezes");
            Avisar("Nao consegui abrir o DeX",
                string.IsNullOrEmpty(ultimaLinha)
                    ? "O scrcpy fechou com erro. Confira o cabo USB ou a conexao Wi-Fi do adb; depois use \"Abrir janela do DeX\"."
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
        if (!_saindo && _miracast.TemConexao)
        {
            await AbrirDexAsync(manual: false);
        }
    }

    private void AoClicarDuasVezes()
    {
        if (_miracast.TemConexao)
        {
            _ = AbrirDexAsync(manual: true);
        }
        else
        {
            Avisar("DeXter", $"No celular: DeX → conectar a TV/monitor → \"{_cfg.NomeTelaEfetivo}\".", ToolTipIcon.Info);
        }
    }

    private void AoMudarEnergia(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            // Depois de suspender, a sessao Miracast costuma morrer: recria o receptor
            NaUi(() => _ = ReabrirAposSuspensaoAsync());
        }
    }

    private async Task ReabrirAposSuspensaoAsync()
    {
        Log.Info("[app] voltou da suspensao.");
        await Task.Delay(5000); // da tempo do Wi-Fi voltar
        if (!_miracast.TemConexao)
        {
            await ReiniciarReceptorAsync("retorno da suspensao");
        }
    }

    private void AoEncerrarSessaoWindows(object? sender, SessionEndingEventArgs e)
    {
        Log.Info("[app] Windows encerrando a sessao.");
        // Limpeza sincrona: nao da para esperar tarefas aqui
        _saindo = true;
        _scrcpy?.Fechar();
        _miracast.Dispose();
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
        if (!_miracast.TemConexao)
        {
            if (manual)
            {
                AoClicarDuasVezes();
            }
            return;
        }
        if (_scrcpy.Aberto)
        {
            return;
        }
        if (manual)
        {
            _falhasScrcpy = 0;
        }

        CancelarDescoberta();
        var cts = new CancellationTokenSource();
        _ctsDescoberta = cts;

        DefinirEstado(Estado.Conectando, "Procurando o display do DeX...");

        var baseVazia = new HashSet<int>();
        var linhaDeBase = baseVazia;
        if (_linhaDeBase != null)
        {
            try
            {
                linhaDeBase = await _linhaDeBase.WaitAsync(TimeSpan.FromSeconds(15), cts.Token);
            }
            catch
            {
                linhaDeBase = baseVazia;
            }
            _linhaDeBase = null; // so vale para a primeira descoberta da conexao
        }

        // 1. qual celular/caminho (USB ou Wi-Fi), esperando ele aparecer no adb
        var limite = DateTime.UtcNow + TimeSpan.FromSeconds(_cfg.DescobertaSegundos);
        Dispositivo? disp = null;
        while (!cts.IsCancellationRequested && DateTime.UtcNow < limite)
        {
            disp = await Task.Run(() => EscolherDispositivoAsync(cts.Token));
            if (disp != null)
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
        if (cts.IsCancellationRequested || _saindo || !_miracast.TemConexao)
        {
            return;
        }
        if (disp == null)
        {
            DefinirEstado(Estado.Erro, "Celular nao encontrado pelo adb");
            Avisar("Celular nao encontrado",
                _cfg.Conexao == "usb"
                    ? "Conecte o cabo USB com a depuracao USB ativa."
                    : "Sem cabo e sem adb por Wi-Fi. Use \"Conexao com o celular\" para preparar o Wi-Fi.",
                ToolTipIcon.Warning);
            return;
        }
        var via = disp.Wifi ? "Wi-Fi" : "USB";
        DefinirEstado(Estado.Conectando, $"Procurando o display do DeX ({via})...");

        // 2. qual display e o do DeX
        var scrcpy = _scrcpy;
        var serial = disp.Serial;
        int? id;
        try
        {
            id = await Task.Run(() => scrcpy.DescobrirDisplayAsync(
                serial, linhaDeBase, TimeSpan.FromSeconds(_cfg.DescobertaSegundos), cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cts.IsCancellationRequested || _saindo || !_miracast.TemConexao)
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
            Avisar("Display do DeX nao encontrado",
                $"O adb ve o celular ({via}), mas nao achei o display do DeX. Depois use \"Abrir janela do DeX\".",
                ToolTipIcon.Warning);
            return;
        }

        await AplicarResolucaoAsync(serial, id.Value);
        if (cts.IsCancellationRequested || _saindo || !_miracast.TemConexao)
        {
            return;
        }

        if (scrcpy.Abrir(id.Value, serial, MontarArgs(disp.Wifi)))
        {
            DefinirEstado(Estado.Ativo, $"DeX ativo (display {id}, {via})");
        }
        else
        {
            DefinirEstado(Estado.Erro, "Falha ao abrir o scrcpy");
        }
    }

    /// <summary>Escolhe o celular conforme o modo. Sem adb, deixa o scrcpy escolher (serial vazio).</summary>
    private async Task<Dispositivo?> EscolherDispositivoAsync(CancellationToken ct)
    {
        if (_adb == null)
        {
            return new Dispositivo("", "device", false);
        }
        var d = await _adb.EscolherAsync(_cfg.Conexao, _cfg.EnderecoWifi, ct);
        if (d != null)
        {
            Log.Info($"[adb] usando {d}");
        }
        return d;
    }

    private string MontarArgs(bool wifi)
    {
        var args = _cfg.ScrcpyArgs;
        if (_cfg.TelaCheia)
        {
            args += " --fullscreen";
        }
        if (wifi && !string.IsNullOrWhiteSpace(_cfg.ScrcpyArgsWifi))
        {
            args += " " + _cfg.ScrcpyArgsWifi; // depois dos padroes: os ultimos valores valem
        }
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
            return;
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
        if (ok && _miracast.TemConexao && _scrcpy is { Aberto: false })
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

    private async Task VigiarAsync()
    {
        if (_saindo || _reiniciando)
        {
            return;
        }

        // Receptor nao iniciou: tenta de novo (a cada 10 s nas 3 primeiras, depois a cada 60 s)
        if (_estado == Estado.Erro && !_miracast.TemConexao && _falhasReceptor > 0)
        {
            _falhasReceptor++;
            if (_falhasReceptor <= 4 || _falhasReceptor % 6 == 0)
            {
                await ReiniciarReceptorAsync("nova tentativa apos falha");
            }
            return;
        }

        if (_miracast.TemConexao)
        {
            _avisosNaoEscutando = 0;
            return;
        }

        var st = await _miracast.LerStatusAsync();
        var escutando = st is MiracastReceiverListeningStatus.Listening
            or MiracastReceiverListeningStatus.ConnectionPending
            or MiracastReceiverListeningStatus.Connected;

        if (escutando)
        {
            _avisosNaoEscutando = 0;
            return;
        }

        _avisosNaoEscutando++;
        Log.Info($"[watchdog] receptor nao esta escutando ({st?.ToString() ?? "desconhecido"}), {_avisosNaoEscutando}x.");
        if (st == MiracastReceiverListeningStatus.DisabledByPolicy)
        {
            DefinirEstado(Estado.Erro, "Miracast desativado por politica do Windows");
            return;
        }
        if (_avisosNaoEscutando >= 2)
        {
            await ReiniciarReceptorAsync($"watchdog ({st})");
        }
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
        _menuResolucao.DropDownItems.Add(ItemResolucao("Padrao do celular", ""));
        _menuResolucao.DropDownItems.Add(ItemResolucao($"Igual a tela do PC ({tela.Width} x {tela.Height})", "tela"));
        _menuResolucao.DropDownItems.Add(new ToolStripSeparator());
        foreach (var p in Presets)
        {
            var rotulo = p.Replace("x", " x ") + (p is "2560x1080" or "3440x1440" ? " (ultrawide)" : "");
            _menuResolucao.DropDownItems.Add(ItemResolucao(rotulo, p));
        }
        _menuResolucao.DropDownItems.Add(new ToolStripSeparator());
        _menuResolucao.DropDownItems.Add(new ToolStripMenuItem("Personalizada...", null, (_, _) => EscolherResolucaoPersonalizada()) { Tag = "*" });

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

    private async Task AtualizarInicioAsync()
    {
        try
        {
            var tarefa = await Windows.ApplicationModel.StartupTask.GetAsync(Projeto.IdTarefaInicio);
            _itemIniciarComWindows.Checked = tarefa.State is Windows.ApplicationModel.StartupTaskState.Enabled
                or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
            _itemIniciarComWindows.Enabled = tarefa.State is not (Windows.ApplicationModel.StartupTaskState.DisabledByPolicy
                or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy);
        }
        catch (Exception ex)
        {
            // sem identidade de pacote ou manifesto sem a StartupTask
            _itemIniciarComWindows.Enabled = false;
            Log.Erro("lendo a tarefa de inicializacao", ex);
        }
    }

    private async Task AlternarInicioAsync()
    {
        try
        {
            var tarefa = await Windows.ApplicationModel.StartupTask.GetAsync(Projeto.IdTarefaInicio);
            switch (tarefa.State)
            {
                case Windows.ApplicationModel.StartupTaskState.Enabled:
                    tarefa.Disable();
                    Log.Info("[app] iniciar com o Windows: desligado");
                    break;
                case Windows.ApplicationModel.StartupTaskState.DisabledByUser:
                    // o Windows nao deixa o app religar sozinho depois que o usuario desligou nas Configuracoes
                    Avisar("Iniciar com o Windows",
                        "Foi desligado nas Configuracoes do Windows. Ligue em Configuracoes > Aplicativos > Inicializacao > DeXter.",
                        ToolTipIcon.Info);
                    AbrirUri("ms-settings:startupapps");
                    break;
                default:
                    var novo = await tarefa.RequestEnableAsync();
                    Log.Info($"[app] iniciar com o Windows: {novo}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Erro("alterando a tarefa de inicializacao", ex);
        }
        await AtualizarInicioAsync();
    }

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
        _itemAbrir.Enabled = _scrcpy != null && _miracast.TemConexao && !_scrcpy.Aberto;
        _itemReiniciar.Enabled = !_reiniciando;
        _itemTela.Checked = _cfg.DesligarTelaCelular;
        _itemTelaCheia.Checked = _cfg.TelaCheia;
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
        foreach (ToolStripItem i in _menuEscala.DropDownItems)
        {
            if (i is ToolStripMenuItem m && m.Tag is int pct)
            {
                m.Checked = pct == _cfg.EscalaDex;
            }
        }
        _itemModoAuto.Checked = _cfg.Conexao == "auto";
        _itemModoUsb.Checked = _cfg.Conexao == "usb";
        _itemModoWifi.Checked = _cfg.Conexao == "wifi";
    }

    private void Avisar(string titulo, string texto, ToolTipIcon tipo)
    {
        if (_saindo)
        {
            return;
        }
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

    private static bool TemIdentidadeDePacote()
    {
        try
        {
            _ = Windows.ApplicationModel.Package.Current.Id.Name;
            return true;
        }
        catch
        {
            return false;
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

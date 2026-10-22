// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;

namespace DeXter;

/// <summary>
/// Configuracao em dexter.json (pasta de dados do app). Criado com os padroes na primeira execucao.
/// </summary>
internal sealed class Config
{
    /// <summary>
    /// Como o DeX e aberto:
    /// "auto" (padrao) = nativo; se o celular nao aceitar, compatibilidade;
    /// "nativo" = o celular conecta o DeX sem fio nele mesmo (sem janelinha, sem Wi-Fi no PC);
    /// "compatibilidade" = display simulado das Opcoes do desenvolvedor (mostra uma janelinha no celular).
    /// </summary>
    public string ModoDex { get; set; } = "auto";

    /// <summary>Densidade (dpi) do display simulado (modo compatibilidade) com tamanho dos itens em 100%.</summary>
    public int DensidadeSimulado { get; set; } = 200;

    /// <summary>Para onde vai o som: "pc" (scrcpy toca no computador), "celular" (fica no celular/fones), "ambos".</summary>
    public string Som { get; set; } = "pc";

    /// <summary>Caminho do scrcpy.exe. Vazio = usar o embutido no app (ou detectar).</summary>
    public string ScrcpyPath { get; set; } = "";

    /// <summary>Argumentos extras do scrcpy (o --display-id e adicionado pelo app).</summary>
    public string ScrcpyArgs { get; set; } =
        "--window-title=\"DeXter\" --stay-awake --video-bit-rate=16M --max-fps=60";

    /// <summary>Quanto tempo esperar o celular/display do DeX aparecer.</summary>
    public int DescobertaSegundos { get; set; } = 40;

    /// <summary>Quantas vezes reabrir o scrcpy seguidas quando ele fecha com erro.</summary>
    public int TentativasScrcpy { get; set; } = 3;

    /// <summary>Como falar com o celular: "auto" (USB se houver, senao Wi-Fi), "usb" ou "wifi".</summary>
    public string Conexao { get; set; } = "auto";

    /// <summary>Ultimo endereco ADB por Wi-Fi (ip:porta), preenchido pelo menu "Preparar Wi-Fi".</summary>
    public string EnderecoWifi { get; set; } = "";

    /// <summary>Argumentos extras quando o scrcpy roda por Wi-Fi (os ultimos valem: sobrescrevem ScrcpyArgs).</summary>
    public string ScrcpyArgsWifi { get; set; } = "--video-bit-rate=8M";

    /// <summary>Apaga a tela do celular enquanto o DeX esta aberto (scrcpy --turn-screen-off), sem bloquear.</summary>
    public bool DesligarTelaCelular { get; set; } = true;

    /// <summary>Tempo de tela do celular durante a sessao (scrcpy --screen-off-timeout). 0 = nao mexer.
    /// O valor original volta quando o scrcpy fecha.</summary>
    public int TimeoutTelaSegundos { get; set; } = 3600;

    /// <summary>Resolucao da area de trabalho do DeX: "" = padrao do celular, "tela" = igual a tela do PC, ou "LARGURAxALTURA".</summary>
    public string ResolucaoDex { get; set; } = "";

    /// <summary>Tamanho dos itens do DeX em % da densidade original (100 = padrao; maior = itens maiores).</summary>
    public int EscalaDex { get; set; } = 100;

    /// <summary>
    /// Qualidade do video (menu Qualidade): "normal", "alta", "maxima" ou "" = personalizada
    /// (usa so o --video-bit-rate escrito em ScrcpyArgs/ScrcpyArgsWifi).
    /// </summary>
    public string? Qualidade { get; set; }

    /// <summary>Bitrate (Mbps) de cada qualidade: (cabo, Wi-Fi).</summary>
    public static readonly (string id, string rotulo, int cabo, int wifi)[] Qualidades =
    {
        ("normal", "Normal", 16, 8),
        ("alta", "Alta", 24, 12),
        ("maxima", "Maxima", 40, 16),
    };

    /// <summary>Abre a janela do DeX em tela cheia (scrcpy --fullscreen; Alt+F alterna).</summary>
    public bool TelaCheia { get; set; } = false;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Arquivo => Path.Combine(Caminhos.Dados, "dexter.json");

    public static Config Carregar()
    {
        try
        {
            Config? cfg = null;
            if (File.Exists(Arquivo))
            {
                cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(Arquivo), Json);
            }
            cfg ??= new Config();
            if (cfg.Qualidade == null)
            {
                // config sem a opcao: se o bitrate e o padrao, usa "normal"; se o usuario editou, respeita a edicao
                var padrao = cfg.ScrcpyArgs.Contains("--video-bit-rate=16M") && cfg.ScrcpyArgsWifi.Trim() == "--video-bit-rate=8M";
                cfg.Qualidade = padrao ? "normal" : "";
            }
            cfg.Salvar(); // grava de volta para o arquivo ganhar as opcoes novas
            return cfg;
        }
        catch (Exception ex)
        {
            Log.Erro("lendo dexter.json (usando padroes)", ex);
            return new Config();
        }
    }

    public void Salvar()
    {
        try
        {
            File.WriteAllText(Arquivo, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex)
        {
            Log.Erro("salvando dexter.json", ex);
        }
    }

    /// <summary>scrcpy que vem dentro do app.</summary>
    public static string CaminhoEmbutido => Path.Combine(AppContext.BaseDirectory, "scrcpy", "scrcpy.exe");

    /// <summary>Acha o scrcpy.exe: config, variavel DEXTER_SCRCPY, pastas conhecidas, PATH.</summary>
    public string? AcharScrcpy()
    {
        var perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidatos = new List<string>();

        if (!string.IsNullOrWhiteSpace(ScrcpyPath)) candidatos.Add(ScrcpyPath);
        var env = Environment.GetEnvironmentVariable("DEXTER_SCRCPY");
        if (!string.IsNullOrWhiteSpace(env)) candidatos.Add(env);

        // embutido no app (copiado pelo build para a pasta scrcpy\)
        candidatos.Add(CaminhoEmbutido);

        candidatos.Add(@"C:\tools\scrcpy\scrcpy.exe");
        candidatos.Add(Path.Combine(perfil, "Downloads", "scrcpy-win64-v4.1", "scrcpy.exe"));

        // qualquer versao baixada em Downloads (a mais nova primeiro)
        try
        {
            var downloads = Path.Combine(perfil, "Downloads");
            if (Directory.Exists(downloads))
            {
                candidatos.AddRange(Directory.GetDirectories(downloads, "scrcpy-win64-v*")
                    .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                    .Select(d => Path.Combine(d, "scrcpy.exe")));
            }
        }
        catch
        {
            // ignora
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            candidatos.Add(Path.Combine(dir.Trim(), "scrcpy.exe"));
        }

        return candidatos.FirstOrDefault(File.Exists);
    }
}

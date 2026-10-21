// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;

namespace DeXter;

/// <summary>
/// Configuracao em dexter.json (pasta de dados do app). Criado com os padroes na primeira execucao.
/// A tela de configuracoes completa fica para a Fase 3.
/// </summary>
internal sealed class Config
{
    /// <summary>Nome que aparece no celular na lista de TVs/monitores.</summary>
    /// <remarks>Vazio = automatico: "DeXter - NOME-DO-PC".</remarks>
    public string NomeTela { get; set; } = "";

    /// <summary>Nome realmente anunciado ao celular.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string NomeTelaEfetivo
    {
        get
        {
            var nome = string.IsNullOrWhiteSpace(NomeTela) ? $"DeXter - {Environment.MachineName}" : NomeTela.Trim();
            return nome.Length > 32 ? nome[..32] : nome; // limite do nome de dispositivo Wi-Fi Direct
        }
    }

    /// <summary>Caminho do scrcpy.exe. Vazio = usar o embutido no app (ou detectar).</summary>
    public string ScrcpyPath { get; set; } = "";

    /// <summary>Argumentos extras do scrcpy (o --display-id e adicionado pelo app).</summary>
    public string ScrcpyArgs { get; set; } =
        "--window-title=\"DeXter\" --stay-awake --video-bit-rate=16M --max-fps=60";

    /// <summary>Quanto tempo esperar o display do DeX aparecer depois que o stream Miracast chega.</summary>
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
            if (cfg.NomeTela == "DeXter")
            {
                cfg.NomeTela = ""; // antigo padrao fixo -> passa a usar o nome do PC
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

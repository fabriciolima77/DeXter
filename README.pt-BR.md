<p align="center">
  <img src="packaging/Assets/DeXter-512.png" width="160" alt="DeXter">
</p>

<h1 align="center">DeXter</h1>

<p align="center">
  Use o <b>Samsung DeX</b> numa janela do Windows, com o mouse e o teclado do PC.<br>
  <a href="README.md">English</a> · <a href="https://github.com/fabriciolima77/DeXter/releases/latest">Baixar</a>
</p>

---

O app oficial "Samsung DeX para PC" parou de funcionar nas versões recentes da One UI: o serviço do celular que ele usava foi removido. O DeXter traz o DeX de volta para o Windows usando só o adb (cabo USB ou Wi-Fi), sem root, sem instalar nada no celular e sem precisar de Wi-Fi ou Miracast no PC.

> O DeXter é um projeto independente e **não tem afiliação com a Samsung**. "Samsung DeX" é marca da Samsung Electronics.

## Como funciona

1. Pelo adb, o DeXter roda no celular um pequeno servidor (como o scrcpy faz).
2. O servidor faz o "DeX sem fio" da Samsung se conectar **no próprio celular** (127.0.0.1). A Samsung cria a área de trabalho do DeX num display de verdade.
3. O DeXter mostra esse display numa janela do Windows com o [scrcpy](https://github.com/Genymobile/scrcpy) (embutido), com vídeo, mouse, teclado, área de transferência e arrastar arquivos.

Essa técnica (o "loopback" do Wi-Fi Display) vem do projeto [ScrcpyDeX](https://github.com/Aureliano021/ScrcpyDex), de Aureliano Peixoto (Apache 2.0).

Se o celular não aceitar esse modo, o DeXter usa o **modo compatibilidade**: um display simulado das Opções do desenvolvedor, que também abre o DeX, mas mostra uma janelinha flutuante na tela do celular.

Tudo roda na bandeja do sistema: abre o DeX sozinho quando o celular aparece, reconecta se cair e, ao sair, devolve o celular ao normal.

## Requisitos

- Windows 10 ou 11 (64 bits)
- Celular Samsung com **DeX** (Android 11 ou mais novo)
- **Depuração USB** ligada no celular (Configurações → Sobre o telefone → Informações do software → toque 7 vezes em "Número de compilação"; depois Opções do desenvolvedor → Depuração USB)

## Instalação

Não precisa instalar:

1. Baixe o **DeXter-x.y.z-win-x64.zip** na página de [releases](https://github.com/fabriciolima77/DeXter/releases/latest).
2. Extraia numa pasta (por exemplo `C:\DeXter`) e abra o **DeXter.exe**.

O app não é assinado, então na primeira vez o Windows pode mostrar **"O Windows protegeu o computador"**: clique em *Mais informações → Executar assim mesmo*. O hash do zip está no `SHA256SUMS.txt` de cada release.

Para atualizar, feche o DeXter (menu → Sair) e troque a pasta pela da versão nova. As configurações ficam em `%LOCALAPPDATA%\DeXter`.

> Quem usava a versão 0.3.x (instalada como pacote): desinstale antes em Configurações → Aplicativos → DeXter.

## Uso

1. Abra o **DeXter.exe**. O ícone aparece na bandeja, perto do relógio.
2. Conecte o celular no cabo USB, ou prepare o Wi-Fi (veja abaixo). Na primeira vez, aceite a autorização de depuração na tela do celular.
3. O DeX abre sozinho na janela.

### Ícone da bandeja

| Bolinha | Estado |
|---|---|
| cinza | iniciando |
| azul | esperando o celular (cabo ou Wi-Fi) |
| amarela | abrindo o DeX |
| verde | DeX (ou a tela do celular) aberto |
| verde-clara | janela fechada ou DeX encerrado (clique duas vezes para abrir) |
| vermelha | erro (passe o mouse para ver o motivo) |

### Menu

- **Abrir o DeX** / **Abrir a tela do celular (sem DeX)**: alterna entre o DeX e a tela normal do celular. Na tela do celular o teclado do PC funciona como teclado físico (`--keyboard=uhid`).
- **Encerrar o DeX no celular**: fecha a janela e devolve o celular ao normal. Clique duas vezes no ícone para abrir de novo.
- **Apagar a tela do celular durante o uso**, **Resolução do DeX**, **Tamanho dos itens do DeX**, **Qualidade do vídeo**, **Som**, **Abrir em tela cheia**.
- **Conexão com o celular**: USB, Wi-Fi ou automático; pareamento sem cabo.
- **Modo do DeX**: automático, nativo ou compatibilidade.
- **Iniciar com o Windows**, **Abrir log**, **Abrir configuração**.

### Sem cabo (Wi-Fi)

Tudo o que o cabo faz, o adb também faz pela rede: celular e PC na mesma rede (o PC pode estar até no cabo de rede). Menu **Conexão com o celular**:

- **Parear sem cabo**: feito uma vez só. No celular: Opções do desenvolvedor → Depuração sem fio → *Parear dispositivo com código de pareamento*. Digite o IP:porta e o código. Depois disso o DeXter acha o celular sozinho, inclusive depois de reiniciar, se a Depuração sem fio estiver ligada.
- **Preparar Wi-Fi pelo cabo**: um clique com o cabo ligado. Vale até o celular reiniciar.

No modo **Automático** (padrão), o DeXter usa o cabo se estiver ligado e o Wi-Fi se não estiver. Pode tirar o cabo no meio do uso: o DeX continua no celular e a janela volta pelo Wi-Fi.

### Som

Menu **Som**:
- **No PC** (padrão): o som do celular toca no computador.
- **No celular**: o som fica no celular (alto-falante ou fones conectados nele). No modo nativo, escolha também a saída de mídia nas configurações do DeX/Android.
- **Nos dois** (Android 13+).

### Tela do celular

Enquanto a janela está aberta, o DeXter apaga a tela do celular sem bloqueá-lo e impede que ele bloqueie por inatividade. **Não aperte o botão power** do celular durante o uso: bloquear o celular bloqueia o DeX junto (é a segurança do Android). Atalhos na janela: **Alt+O** apaga a tela do celular e **Alt+Shift+O** acende.

### Resolução, qualidade e tela cheia

- **Resolução do DeX**: padrão (1920×1080), **igual à tela do PC** (ideal com tela cheia), presets de 1280×720 a 3440×1440 (ultrawide) ou personalizada. É a resolução real da área de trabalho do DeX, então ela ganha espaço de verdade.
- **Tamanho dos itens do DeX**: de 75% (mais espaço) a 150% (itens maiores).
- **Qualidade do vídeo**: Normal (16 Mbps no cabo / 8 no Wi-Fi), Alta (24 / 12) ou Máxima (40 / 16). Se o mouse começar a atrasar, baixe um nível.
- **Abrir em tela cheia**: dentro da janela, **Alt+F** alterna.

As mudanças reabrem a janela na hora. Ao sair, o celular volta ao normal.

### Modo do DeX

| Modo | Como funciona | Janelinha no celular |
|---|---|---|
| **Automático** (padrão) | Nativo; se o celular não aceitar, compatibilidade | depende |
| **Nativo** | DeX sem fio conectado no próprio celular (loopback) | não |
| **Compatibilidade** | Display simulado das Opções do desenvolvedor | sim (some com a tela apagada) |

### Configuração avançada

Menu → **Abrir configuração** (`dexter.json`, em `%LOCALAPPDATA%\DeXter`): `ModoDex`, `Som`, `DensidadeSimulado` (densidade do modo compatibilidade, padrão 200), argumentos do scrcpy (`ScrcpyArgs`, `ScrcpyArgsWifi`), tempo de tela, tempo de espera e tentativas. Reinicie o DeXter depois de editar.

## Problemas comuns

| Sintoma | O que fazer |
|---|---|
| "O Windows protegeu o computador" | *Mais informações → Executar assim mesmo*. O app não é assinado. |
| "Celular não encontrado pelo adb" | Cabo conectado com Depuração USB ligada, ou Wi-Fi preparado. Na primeira vez, aceite a autorização na tela do celular. |
| Aviso "Usando o modo compatibilidade" | O celular não aceitou o DeX nativo. O motivo está no log (linhas `[nativo]`). O DeX funciona, mas com a janelinha no celular. |
| Ficou uma janelinha do DeX na tela do celular | É o display simulado do modo compatibilidade. O DeXter remove ao sair; se ele foi fechado à força, rode `adb shell settings delete global overlay_display_devices`. |
| O DeX nativo ficou aberto no celular depois de fechar o DeXter à força | Rode `adb shell pkill -f dexter.server.Server`, ou desconecte o DeX pelo painel de notificações do celular. |
| Sem som | Menu → Som. No modo "No celular", confira a saída de mídia do DeX/Android. |
| Vídeo travando por Wi-Fi | Menu → Qualidade do vídeo → Normal, ou diminua `ScrcpyArgsWifi` no `dexter.json`. |

O log fica em Menu → **Abrir log**.

## Compilar a partir do código

Requisitos:
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- JDK 11 ou mais novo (para o servidor do DeX nativo)
- Android SDK com uma plataforma e build-tools: `sdkmanager "platforms;android-35" "build-tools;35.0.0"` (o Android Studio já instala). O script procura em `ANDROID_HOME`, `ANDROID_SDK_ROOT` ou `%LOCALAPPDATA%\Android\Sdk`.

```powershell
# compilar (app + servidor) e abrir
powershell -ExecutionPolicy Bypass -File scripts\dev.ps1

# so o servidor do DeX nativo (server\dexter-server.jar)
powershell -ExecutionPolicy Bypass -File server\build-server.ps1

# gerar o zip portatil em dist\
powershell -ExecutionPolicy Bypass -File scripts\pack.ps1 -Versao 0.5.0
```

Sem JDK/Android SDK o app compila mesmo assim, mas só funciona no modo compatibilidade. Na primeira compilação, o build baixa o pacote oficial do scrcpy (com o adb) para `vendor\`.

### Estrutura

```
src/DeXter/          app (C#/.NET 8, WinForms na bandeja)
  TrayApp.cs         bandeja, modos, reconexao, watchdog
  DexNativo.cs       envia e controla o servidor do DeX nativo no celular
  Scrcpy.cs          descoberta do display + processo do scrcpy
  Adb.cs             USB/Wi-Fi, pareamento, mDNS, display simulado
server/              servidor Java do DeX nativo (roda no celular via app_process)
packaging/           icones e o leia-me do zip
scripts/             dev.ps1, pack.ps1
.github/workflows/   build a cada push; release a cada tag vX.Y.Z
```

### Publicar uma versão (mantenedor)

`git tag v0.5.0 && git push origin v0.5.0`. O GitHub Actions compila o servidor e o app, gera o zip e publica o release.

## Licença e créditos

DeXter © 2026 [fabriciolima77](https://github.com/fabriciolima77), licenciado sob a **GNU GPL v3.0 ou posterior** ([LICENSE](LICENSE)).

- O servidor em `server/` é derivado do [ScrcpyDeX](https://github.com/Aureliano021/ScrcpyDex) © 2026 Aureliano Peixoto e colaboradores, sob a **Apache License 2.0** ([server/LICENSE-ScrcpyDeX.txt](server/LICENSE-ScrcpyDeX.txt)).
- Inclui o [scrcpy](https://github.com/Genymobile/scrcpy) (Apache 2.0) e o adb do Android, sem modificações.

Veja [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

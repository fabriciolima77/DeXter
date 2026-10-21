<p align="center">
  <img src="packaging/Assets/DeXter-512.png" width="160" alt="DeXter">
</p>

<h1 align="center">DeXter</h1>

<p align="center">
  Use o <b>Samsung DeX</b> numa janela do Windows, com o mouse e o teclado do PC.<br>
  <a href="README.md">English</a> · <a href="https://github.com/fabriciolima77/DeXter/releases/latest">Baixar</a>
</p>

---

O app oficial "Samsung DeX para PC" parou de funcionar nas versões recentes da One UI: o serviço do celular que ele usava foi removido. O DeXter traz o DeX de volta para o Windows de outro jeito, usando só recursos públicos do Windows e do Android.

> O DeXter é um projeto independente e **não tem afiliação com a Samsung**. "Samsung DeX" é marca da Samsung Electronics.

## Como funciona

1. O DeXter faz o PC se apresentar ao celular como uma **TV Miracast** com o nome do computador, por exemplo "DeXter - DESKTOP-ABC123".
2. No celular você inicia o **DeX sem fio** nessa "TV". A Samsung cria a área de trabalho do DeX num display próprio.
3. O DeXter descobre esse display e abre o DeX numa janela com o [scrcpy](https://github.com/Genymobile/scrcpy) (embutido), com vídeo, mouse, teclado, área de transferência e arrastar arquivos.

Tudo roda na bandeja do sistema: abre o DeX sozinho, reconecta se cair e fecha tudo direito ao sair.

## Requisitos

- Windows 10 versão 2004 (build 19041) ou mais novo, ou Windows 11
- Placa Wi-Fi compatível com Miracast (a maioria dos notebooks)
- Celular Samsung com **DeX sem fio**
- **Depuração USB** ligada no celular (Configurações → Sobre o telefone → Informações do software → toque 7 vezes em "Número de compilação"; depois Opções do desenvolvedor → Depuração USB)

## Instalação

1. Baixe o **DeXter-x.y.z-x64.zip** na página de [releases](https://github.com/fabriciolima77/DeXter/releases/latest) e extraia.
2. Clique duas vezes em **Instalar.cmd**.

O pacote é assinado com um certificado do próprio projeto (autoassinado). Na primeira instalação, o Windows pede permissão de administrador para confiar nesse certificado. O `DeXter.cer` e os hashes (`SHA256SUMS.txt`) estão em cada release.

Para atualizar, rode o Instalar.cmd da versão nova. Para desinstalar, use Configurações → Aplicativos ou o `Desinstalar.ps1`.

## Uso

1. Feche o app **Conectar** do Windows, se estiver aberto (só um receptor Miracast roda por vez).
2. Abra o **DeXter** (menu Iniciar ou comando `dexter`). O ícone aparece na bandeja.
3. Conecte o celular no cabo USB, ou prepare o Wi-Fi (veja abaixo).
4. No celular: **DeX → conectar a TV/monitor → DeXter - (nome do seu PC)**. A janela do DeX abre sozinha.

### Ícone da bandeja

| Bolinha | Estado |
|---|---|
| cinza | iniciando |
| azul | aguardando o celular |
| amarela | conectando / procurando o DeX |
| verde | DeX aberto |
| verde-clara | DeX conectado, janela fechada (clique duas vezes para abrir) |
| vermelha | erro (passe o mouse para ver o motivo) |

Menu: abrir a janela do DeX, reiniciar o receptor, apagar a tela do celular, resolução e tamanho dos itens do DeX, tela cheia, conexão com o celular (USB/Wi-Fi), iniciar com o Windows, log e configuração.

### Sem cabo (Wi-Fi)

O vídeo do DeX já vai por Wi-Fi. O cabo só é usado para controle. Para dispensá-lo, o celular e o PC precisam estar na mesma rede. Use o menu **Conexão com o celular**:

- **Parear sem cabo**: feito uma vez só. No celular: Opções do desenvolvedor → Depuração sem fio → *Parear dispositivo com código de pareamento*. Digite o IP:porta e o código. Depois disso o DeXter acha o celular sozinho, inclusive depois de reiniciar, se a Depuração sem fio estiver ligada.
- **Preparar Wi-Fi pelo cabo**: um clique com o cabo ligado. Vale até o celular reiniciar.

No modo **Automático** (padrão), o DeXter usa o cabo se estiver ligado e o Wi-Fi se não estiver. Pode tirar o cabo no meio do uso.

### Tela do celular

Enquanto o DeX está aberto, o DeXter apaga a tela do celular sem bloqueá-lo e impede que ele bloqueie por inatividade. **Não aperte o botão power** do celular durante o uso: bloquear o celular bloqueia o DeX junto (é a segurança do Android). Atalhos na janela: **Alt+O** apaga a tela do celular e **Alt+Shift+O** acende.

### Resolução e tela cheia

No menu do ícone:

- **Resolução do DeX**: padrão do celular, **igual à tela do PC** (ideal com tela cheia), presets de 1280×720 a 3440×1440 (ultrawide) ou personalizada. O DeXter muda a resolução do display do DeX no próprio celular (`wm size`), então a área de trabalho ganha mais espaço de verdade, sem só esticar a imagem.
- **Tamanho dos itens do DeX**: de 75% (mais espaço) a 150% (itens maiores).
- **Abrir em tela cheia**: dentro da janela, **Alt+F** alterna.

As mudanças reabrem a janela do DeX na hora. Ao sair do DeXter, o celular volta à resolução original.

### Configuração avançada

Menu → **Abrir configuração** (`dexter.json`): nome da tela (`NomeTela`; vazio = "DeXter - nome do PC"), argumentos do scrcpy (qualidade, FPS, resolução), argumentos extras por Wi-Fi, tempo de tela, tempo de espera e tentativas. Reinicie o DeXter depois de editar.

## Problemas comuns

| Sintoma | O que fazer |
|---|---|
| "Receptor Miracast não iniciou" / AccessDenied | Feche o app Conectar e qualquer outro receptor Miracast; desative "Projetar neste PC" nas Configurações. O log lista os processos suspeitos. |
| "DeXter - (nome do PC)" não aparece no celular | Confira se o Wi-Fi do PC está ligado e se o ícone está azul. Use "Reiniciar receptor". |
| "Celular não encontrado pelo adb" | Cabo conectado com Depuração USB ligada, ou Wi-Fi preparado. Na primeira vez, aceite a autorização na tela do celular. |
| Erro 0x800B010A ao instalar | O certificado do projeto não está confiável. Instale pelo **Instalar.cmd** (não abra o `.msix` direto); ele coloca o `DeXter.cer` em "Pessoas Confiáveis" e em "Autoridades de Certificação Raiz Confiáveis". |
| Vídeo travando por Wi-Fi | Diminua `ScrcpyArgsWifi` no `dexter.json` (ex.: `--video-bit-rate=4M --max-size=1600`). |

O log fica em Menu → **Abrir log**.

## Compilar a partir do código

Requisitos: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), Windows com **Modo de desenvolvedor** ligado. Para gerar o instalador também é preciso o [Windows SDK](https://developer.microsoft.com/windows/downloads/windows-sdk/).

```powershell
# compilar e registrar a versão de desenvolvimento ("DeXter (dev)", comando dexter-dev)
powershell -ExecutionPolicy Bypass -File scripts\dev.ps1

# gerar o instalador (MSIX assinado + zip) em dist\
powershell -ExecutionPolicy Bypass -File scripts\pack.ps1 -Versao 0.3.0
```

Na primeira compilação, o build baixa o pacote oficial do scrcpy (com o adb) para `vendor\`.

O app precisa de **identidade de pacote** para usar a API Miracast do Windows. Por isso ele sempre roda como pacote (MSIX, ou registrado em modo desenvolvedor), nunca como `.exe` solto.

### Estrutura

```
src/DeXter/          app (C#/.NET 8, WinForms na bandeja)
  TrayApp.cs         bandeja, estados, reconexão, watchdog
  MiracastHost.cs    receptor Miracast (Windows.Media.Miracast)
  Scrcpy.cs          descoberta do display do DeX + processo do scrcpy
  Adb.cs             USB/Wi-Fi, pareamento, mDNS
packaging/           manifesto do pacote, ícones, certificado público, instalador
scripts/             dev.ps1, pack.ps1, New-Certificado.ps1
.github/workflows/   build a cada push; release a cada tag vX.Y.Z
```

### Publicar uma versão (mantenedor)

1. Uma vez: `scripts\New-Certificado.ps1`. Commite o `packaging\DeXter.cer` e cadastre os secrets `SIGNING_PFX_BASE64` e `SIGNING_PFX_PASSWORD` no GitHub.
2. A cada versão: `git tag v0.3.0 && git push origin v0.3.0`. O GitHub Actions compila, assina e publica o release.

## Licença e créditos

DeXter © 2026 [fabriciolima77](https://github.com/fabriciolima77), licenciado sob a **GNU GPL v3.0 ou posterior** ([LICENSE](LICENSE)).

Inclui o [scrcpy](https://github.com/Genymobile/scrcpy) (Apache 2.0) e o adb do Android, sem modificações. Veja [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

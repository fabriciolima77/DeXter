# Componentes de terceiros

O DeXter inclui, sem modificações, o pacote oficial do **scrcpy para Windows** (pasta `scrcpy\` ao lado do executável), baixado durante o build de:
https://github.com/Genymobile/scrcpy/releases

Esse pacote contém os componentes abaixo, cada um com sua própria licença:

| Componente | Licença | Fonte |
|---|---|---|
| scrcpy e scrcpy-server | Apache License 2.0 | https://github.com/Genymobile/scrcpy |
| adb, AdbWinApi, AdbWinUsbApi (Android SDK Platform-Tools) | Apache License 2.0 / termos do Android SDK | https://developer.android.com/tools/releases/platform-tools |
| FFmpeg (avcodec, avformat, avutil, swresample) | LGPL 2.1 ou posterior | https://ffmpeg.org |
| SDL3 | zlib | https://www.libsdl.org |
| libusb | LGPL 2.1 ou posterior | https://libusb.info |

As bibliotecas LGPL são distribuídas sem modificação, como DLLs separadas. O DeXter também aceita um scrcpy externo (opção `ScrcpyPath` no `dexter.json`), o que permite usar versões próprias dessas bibliotecas. O código-fonte de cada componente está nos endereços acima.

## Servidor do DeX nativo (ScrcpyDeX)

O servidor em `server/` (que roda no celular e conecta o DeX sem fio no próprio aparelho) é derivado do **ScrcpyDeX**:

- Projeto: https://github.com/Aureliano021/ScrcpyDex
- Copyright 2026 Aureliano Peixoto and ScrcpyDeX Contributors
- Licença: Apache License 2.0 (`server/LICENSE-ScrcpyDeX.txt`, também incluída no zip em `server/`)
- Modificações para o DeXter: pacote `dexter.server`, nome do display "DeXter", saída em linhas `DEXTER_*`, execução destacada (sobrevive à troca cabo ↔ Wi-Fi), leitura RTSP sem timeout e encerramento automático quando a sessão do DeX termina. Cada arquivo indica no cabeçalho que é derivado.

O DeXter não é afiliado à Samsung, ao Google, aos autores do scrcpy nem aos do ScrcpyDeX. "Samsung DeX" é marca da Samsung Electronics.

# Componentes de terceiros

O DeXter inclui, sem modificações, o pacote oficial do **scrcpy para Windows** (pasta `scrcpy\` ao lado do executável), baixado durante o build de:
https://github.com/Genymobile/scrcpy/releases

Esse pacote contém os componentes abaixo, cada um com sua própria licença:

| Componente | Licença | Fonte |
|---|---|---|
| scrcpy e scrcpy-server | Apache License 2.0 | https://github.com/Genymobile/scrcpy |
| adb, AdbWinApi, AdbWinUsbApi (Android SDK Platform-Tools) | Apache License 2.0 / termos do Android SDK | https://developer.android.com/tools/releases/platform-tools |
| FFmpeg (avcodec, avformat, avutil, swresample) | LGPL 2.1 ou posterior | https://ffmpeg.org |
| SDL2 | zlib | https://www.libsdl.org |
| libusb | LGPL 2.1 ou posterior | https://libusb.info |

As bibliotecas LGPL são distribuídas sem modificação, como DLLs separadas. O DeXter também aceita um scrcpy externo (opção `ScrcpyPath` no `dexter.json`), o que permite usar versões próprias dessas bibliotecas. O código-fonte de cada componente está nos endereços acima.

O DeXter não é afiliado à Samsung, ao Google nem aos autores do scrcpy. "Samsung DeX" é marca da Samsung Electronics.

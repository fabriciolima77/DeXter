<p align="center">
  <img src="packaging/Assets/DeXter-512.png" width="160" alt="DeXter">
</p>

<h1 align="center">DeXter</h1>

<p align="center">
  Run <b>Samsung DeX</b> in a window on Windows, using your PC's mouse and keyboard.<br>
  <a href="README.pt-BR.md">Português</a> · <a href="https://github.com/fabriciolima77/DeXter/releases/latest">Download</a>
</p>

---

Samsung's official "DeX for PC" app stopped working on recent One UI versions because the phone-side service it relied on was removed. DeXter brings DeX back to Windows using only adb (USB cable or Wi-Fi): no root, nothing installed on the phone, and no Wi-Fi or Miracast needed on the PC.

> DeXter is an independent project and **is not affiliated with Samsung**. "Samsung DeX" is a trademark of Samsung Electronics.

> The app's interface is currently in Portuguese. Translations are welcome.

## How it works

1. Over adb, DeXter runs a small server on the phone (the same way scrcpy does).
2. The server makes Samsung's "wireless DeX" connect **to the phone itself** (127.0.0.1), so Samsung creates the DeX desktop on a real display.
3. DeXter shows that display in a Windows window with [scrcpy](https://github.com/Genymobile/scrcpy) (bundled): video, mouse, keyboard, clipboard and drag-and-drop.

This Wi-Fi Display "loopback" technique comes from [ScrcpyDeX](https://github.com/Aureliano021/ScrcpyDex) by Aureliano Peixoto (Apache 2.0).

If the phone doesn't accept it, DeXter falls back to a **compatibility mode**: a simulated display from the developer options. DeX opens there too, but a small floating window shows up on the phone screen.

## Requirements

- Windows 10 or 11 (64-bit)
- Samsung phone with **DeX** (Android 11 or newer)
- **USB debugging** enabled on the phone

## Install

No installation: download **DeXter-x.y.z-win-x64.zip** from [Releases](https://github.com/fabriciolima77/DeXter/releases/latest), extract it and run **DeXter.exe**. The app is not signed, so Windows may show "Windows protected your PC": click *More info → Run anyway*. Settings live in `%LOCALAPPDATA%\DeXter`.

## Use

1. Run **DeXter.exe**. A tray icon appears.
2. Connect the phone by USB, or set up Wi-Fi from the tray menu (*Conexão com o celular*). You can pair once with wireless debugging and never use the cable again. You can even unplug the cable mid-session: DeX keeps running and the window comes back over Wi-Fi.
3. DeX opens in the window by itself. When DeXter exits, the phone goes back to normal.

The tray menu also lets you:
- switch to the **plain phone screen** (no DeX) and back;
- turn the phone screen off without locking it (don't press the phone's power button: locking the phone locks DeX too);
- change the **DeX resolution** (match your PC screen, presets up to 3440×1440 ultrawide, or custom), **UI scale**, **video quality**, **sound output** (PC, phone/headphones, or both) and **fullscreen**;
- pick the **DeX mode** (automatic, native, or compatibility).

See the [Portuguese README](README.pt-BR.md) for details and troubleshooting.

## Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), a JDK 11+ and the Android SDK (`platforms;android-35` and `build-tools;35.0.0`) for the native DeX server.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\dev.ps1                   # build (app + server) and run
powershell -ExecutionPolicy Bypass -File scripts\pack.ps1 -Versao 0.5.0    # portable zip in dist\
```

Without a JDK/Android SDK the app still builds, but only the compatibility mode works. Releases are built by GitHub Actions when a `vX.Y.Z` tag is pushed.

## License and credits

DeXter © 2026 [fabriciolima77](https://github.com/fabriciolima77), licensed under the **GNU GPL v3.0 or later** ([LICENSE](LICENSE)).

- The server in `server/` is derived from [ScrcpyDeX](https://github.com/Aureliano021/ScrcpyDex) © 2026 Aureliano Peixoto and contributors, under the **Apache License 2.0** ([server/LICENSE-ScrcpyDeX.txt](server/LICENSE-ScrcpyDeX.txt)).
- Bundles unmodified [scrcpy](https://github.com/Genymobile/scrcpy) (Apache 2.0) and Android adb.

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

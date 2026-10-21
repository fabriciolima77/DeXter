<p align="center">
  <img src="packaging/Assets/DeXter-512.png" width="160" alt="DeXter">
</p>

<h1 align="center">DeXter</h1>

<p align="center">
  Run <b>Samsung DeX</b> in a window on Windows, using your PC's mouse and keyboard.<br>
  <a href="README.pt-BR.md">Português</a> · <a href="https://github.com/fabriciolima77/DeXter/releases/latest">Download</a>
</p>

---

Samsung's official "DeX for PC" app stopped working on recent One UI versions because the phone-side service it relied on was removed. DeXter brings DeX back to Windows a different way, using only public Windows and Android features.

> DeXter is an independent project and **is not affiliated with Samsung**. "Samsung DeX" is a trademark of Samsung Electronics.

> The app's interface is currently in Portuguese. Translations are welcome.

## How it works

1. DeXter makes your PC show up on the phone as a **Miracast TV** named after the computer, e.g. "DeXter - DESKTOP-ABC123".
2. You start **wireless DeX** to that "TV". Samsung creates the DeX desktop on its own display.
3. DeXter finds that display and opens it in a window with [scrcpy](https://github.com/Genymobile/scrcpy) (bundled): video, mouse, keyboard, clipboard and drag-and-drop.

It all runs from the system tray. It opens DeX automatically, reconnects when something drops, and shuts everything down cleanly on exit.

## Requirements

- Windows 10 version 2004 (build 19041) or newer, or Windows 11
- Miracast-capable Wi-Fi (most laptops)
- Samsung phone with **wireless DeX**
- **USB debugging** enabled on the phone

## Install

1. Download **DeXter-x.y.z-x64.zip** from [Releases](https://github.com/fabriciolima77/DeXter/releases/latest) and extract it.
2. Double-click **Instalar.cmd**.

The package is signed with the project's own self-signed certificate. On first install, Windows asks for administrator permission to trust it (the installer adds `DeXter.cer` to *Trusted People* and *Trusted Root*). Don't open the `.msix` directly, or Windows rejects it with error 0x800B010A. `DeXter.cer` and `SHA256SUMS.txt` are attached to every release.

## Use

1. Close the Windows **Connect** app if it is open (only one Miracast receiver can run at a time).
2. Start **DeXter** from the Start menu, or run `dexter`. A tray icon appears.
3. Connect the phone by USB, or set up Wi-Fi from the tray menu (*Conexão com o celular*). You can pair once with wireless debugging and never use the cable again.
4. On the phone: **DeX → connect to TV/monitor → DeXter - (your PC name)**. The DeX window opens by itself.

While DeX is open, DeXter turns the phone screen off without locking it. Don't press the phone's power button: locking the phone locks DeX too. Use **Alt+O** / **Alt+Shift+O** in the DeX window to turn the phone screen off or on.

The tray menu also lets you change the **DeX desktop resolution** (phone default, match your PC screen, presets up to 3440×1440 ultrawide, or custom), the **UI scale**, and open in **fullscreen** (Alt+F toggles). The resolution is changed on the phone's DeX display itself (`wm size`), so you get real extra desktop space. It is restored when DeXter exits.

See the [Portuguese README](README.pt-BR.md) for the tray icon states, Wi-Fi setup details, settings and troubleshooting.

## Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and Windows **Developer Mode**. The [Windows SDK](https://developer.microsoft.com/windows/downloads/windows-sdk/) is also needed to build the installer.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\dev.ps1                   # build + register "DeXter (dev)"
powershell -ExecutionPolicy Bypass -File scripts\pack.ps1 -Versao 0.3.0    # signed MSIX + zip in dist\
```

The first build downloads the official scrcpy release (which includes adb) into `vendor\`. The app needs **package identity** to use the Windows Miracast API, so it always runs as a package, never as a loose `.exe`.

Releases are built by GitHub Actions when a `vX.Y.Z` tag is pushed.

## License and credits

DeXter © 2026 [fabriciolima77](https://github.com/fabriciolima77), licensed under the **GNU GPL v3.0 or later** ([LICENSE](LICENSE)).

Bundles unmodified [scrcpy](https://github.com/Genymobile/scrcpy) (Apache 2.0) and Android adb. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

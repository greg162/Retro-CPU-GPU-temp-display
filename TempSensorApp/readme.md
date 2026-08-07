# TempSensorApp — Build Instructions

The Windows-side half of the project. It reads CPU/GPU temperatures with
LibreHardwareMonitor, shows them in a system tray icon, and writes
`<cpu>,<gpu>\n` to the Pico over serial at 115200 baud every 2 seconds.

## Prerequisites

  * **Windows** — the project targets `net10.0-windows` and uses WinForms, WMI
    and `System.IO.Ports`. It will not build or run on Linux/macOS.
  * **.NET 10 SDK** — https://dotnet.microsoft.com/download/dotnet/10.0

Check what you have installed:

```powershell
dotnet --list-sdks
```

You need a `10.x` entry. If you only see older SDKs, install the .NET 10 SDK
(the *runtime* alone is not enough to build).

## Quick build (development)

From the repo root, in PowerShell:

```powershell
cd TempSensorApp
dotnet restore
dotnet build -c Release
```

Output lands in `bin\Release\net10.0-windows\`. To run it straight from source:

```powershell
dotnet run -c Release
```

This build needs the .NET 10 runtime installed on whatever machine runs it.

## Publishing the standalone .exe

> Releases are built automatically. Pushing a version tag runs
> `.github/workflows/release.yml`, which publishes the exe on a Windows runner
> and attaches a zip to the GitHub Release:
>
> ```powershell
> git tag 1.1.0
> git push origin 1.1.0
> ```
>
> You only need the commands below to produce a build locally. Build output is
> gitignored — don't commit binaries back into the repo.

A single self-contained executable (~75 MB) that runs on a machine with no .NET
installed.

```powershell
cd TempSensorApp
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Output directory:

```
bin\Release\net10.0-windows\win-x64\publish\
```

The files that make up a distributable build:

  * `TempSensorApp.exe` — the app
  * `MonoPosixHelper.dll` and `libMonoPosixHelper.dll` — native helpers pulled in
    by `System.IO.Ports`. Single-file publish leaves native libraries beside the
    exe by default, and the app will not start without them.

`TempSensorApp.pdb` is only needed if you want stack traces with line numbers.

If you'd rather have *everything* in one file with no loose DLLs, add:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The native libs are then extracted to a temp folder at startup instead.

### Smaller build (framework-dependent)

If the target PC already has the .NET 10 desktop runtime, this drops the output
to a few MB:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

## Cleaning

```powershell
dotnet clean -c Release
Remove-Item -Recurse -Force bin, obj
```

## Running

Launch `TempSensorApp.exe` — it has no console window, it appears as a tray
icon. Right-click the icon for status, current readings and Exit.

  * **Run as Administrator.** LibreHardwareMonitor loads a kernel driver to read
    CPU sensors; without elevation CPU temperature comes back empty.
  * The Pico must be plugged in *before* you start the app. It picks the first
    available COM port (`SerialPort.GetPortNames()[0]`), so if you have other
    serial devices attached it may grab the wrong one.
  * `TempSensorApp.exe --kill` (or `-k`) terminates any other running instances
    and exits — handy for restarting cleanly after replugging the Pico.

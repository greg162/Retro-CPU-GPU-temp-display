# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A two-halves project: a Windows tray app reads CPU/GPU temperature and pushes it over
USB serial to a Raspberry Pi Pico, which drives two TM1637 4-digit 7-segment displays.

  * `TempSensorApp/` — the Windows side, C# / WinForms, `net10.0-windows`.
  * `PicoDisplayApp/main.py` — the Pico firmware, MicroPython, deployed by pasting into Thonny.
  * `tools/IconGen/` — dev-only tool that regenerates `TempSensorApp/app.ico`.
  * `case/` — STLs for the printable enclosure. Exported from Onshape *in metres*, so they
    import into most slicers 1000x too small; the readme tells users to scale. Don't "fix"
    them by rescaling without saying so — anyone who already printed a case is working off
    the current files.

`TempSensorApp.slnx` is empty — build the `.csproj` files directly, not the solution.

## Commands

```powershell
# Build / run the app (Windows only — WinForms, WMI, System.IO.Ports)
dotnet build TempSensorApp/TempSensorApp.csproj -c Release
dotnet run --project TempSensorApp -c Release

# Regenerate app.ico and media/tray-icon-preview.png after touching TrayIconRenderer
dotnet run --project tools/IconGen

# Local release build (what CI produces)
dotnet publish TempSensorApp/TempSensorApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Kill other running instances (frees the COM port)
TempSensorApp.exe --kill
```

There are no tests and no linter configured.

Releases are cut by pushing a version tag (`git tag 2.0.0 && git push origin 2.0.0`),
which runs `.github/workflows/release.yml`. Build output is gitignored — never commit binaries.

Two things must be updated *before* the tag is pushed, because the workflow reads both:

  * `CHANGELOG.md` needs a `## [<version>]` section for the tag being built. That section is
    lifted out and used verbatim as the GitHub release notes, so the heading format is load
    bearing — the extraction matches on it. With no match the workflow logs a warning and falls
    back to `--generate-notes`, which builds the notes out of raw commit subjects instead.
  * `<Version>` in `TempSensorApp.csproj` should match the version being released. CI overrides
    it with `-p:Version=<tag>` so a released exe always reports its own tag; the csproj value is
    what local builds and manual runs fall back to.

A `workflow_dispatch` run builds the zip and uploads it as a workflow artifact, but stops before
creating or touching a release — use one to test a build without creating a tag.

## Running it for real

  * **Run as Administrator**, or LibreHardwareMonitor cannot load its kernel driver and CPU
    temperature comes back empty.
  * The Pico must be plugged in *before* launch. `PicoLink.FindPicoPort` just takes
    `SerialPort.GetPortNames()[0]` — with other serial devices attached it will grab the wrong one.

## Architecture

`TempSensorApplicationContext` (`Program.cs`) is only a coordinator: on each timer tick it reads
from `TemperatureMonitor`, hands the result to `TrayPresenter`, and pushes it down `PicoLink`. It
holds no sensor, drawing or serial logic itself, and each of those three owns its own resources
and lifetime. Put new behaviour in whichever of the three it belongs to, not in the context.

**Wire protocol.** `PicoLink.Send` writes `"<cpu>,<gpu>\n"` at 115200 baud every 2 seconds.
Two invariants tie the halves together and are easy to break by accident:

  * A missing sensor is sent as `0.0`, not omitted. The firmware reads `0` as "no reading" and
    shows dashes. Staying silent instead trips the firmware's 10-second timeout and puts `E-20`
    on both displays.
  * The firmware's error codes are `E-10` parse, `E-11` format, `E-20` timeout, `E-99` unknown;
    boot statuses `1`/`2`/`3` flash before data arrives.

**Temperature reading** (`TemperatureServices.cs`, `TemperatureMonitor.cs`). `TemperatureFinder`
holds an ordered list of `ITemperatureProvider`s and returns the first non-null,
greater-than-zero reading for a requested `TemperatureType`. Registration order in
`TemperatureMonitor.CreateDefault()` *is* the fallback order: WMI CPU → LibreHardwareMonitor CPU →
LibreHardwareMonitor GPU. Adding a source means writing a provider and registering it there in the
right position. `TemperatureMonitor` also owns the shared LibreHardwareMonitor `Computer` handle —
providers borrow it, they don't own it.

**Failure handling is deliberately fail-blank.** A sensor exception degrades that tick to dashes
and recovers on the next one (`TemperatureMonitor.Read`); a failed serial write makes `Send`
return false, and the context then calls `StopMonitoring` → `TrayPresenter.ShowStopped`, which
blanks icon, tooltip and menu permanently (nothing restarts the timer). The rule being enforced: a
halted or degraded monitor must never leave a plausible-looking temperature on screen where it's
indistinguishable from a live one. Preserve that when changing error paths.

**`PicoLink` carries no UI.** `Connect()` returns a `PicoLinkResult` describing *why* it failed
(`NoPortFound` / `PortInUse` / `OpenFailed`); turning that into a status line and a `MessageBox` is
`TempSensorApplicationContext.ReportConnectionFailure`'s job. Keep message text out of the
transport.

**The tray icon** (`TrayIconRenderer.cs`) is drawn at runtime as 7-segment digits, reusing the same
segment bit patterns as `DIGITS` in `PicoDisplayApp/main.py` so the tray and the TM1637 hardware
render a digit identically — change one, change the other. Colour bands: green < 60 °C, amber to
79 °C, red at 80 °C+, grey for no reading.

Two things to respect when editing it:

  * Every generated icon owns an unmanaged HICON. `CreateIcon` destroys the handle it gets from
    `GetHicon`, and `TrayPresenter.UpdateIcon` disposes the previous icon and only redraws when
    `Quantize()`'d whole degrees change.
  * `app.ico` is generated from this same code. After changing colours or geometry, rerun
    `dotnet run --project tools/IconGen`. `tools/IconGen` links `TrayIconRenderer.cs` as source
    (not a project reference) so the app never depends on the tool.

**Startup.** `TempSensorApplicationContext`'s constructor can fail before a message loop exists, so
it sets `StartupFailed` rather than calling `Application.Exit()`; `Program.Main` checks the flag,
disposes and returns.

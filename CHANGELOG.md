# Changelog

All notable changes to this project are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

Versions match the git tags that trigger a release build, so `2.0.0` here is the
`2.0.0` tag and the `TempSensorApp-2.0.0.zip` attached to it. The release
workflow reads the section for the tag it is building out of this file and uses
it as the GitHub release notes, so the heading format matters — see
[CLAUDE.md](CLAUDE.md).

## [Unreleased]

## [2.0.0] - 2026-08-10

The app moved from a plain windowed executable to a system tray app, and
releases are now built by CI instead of being committed to the repository.

### Added

  * The app now runs as a system tray icon rather than a window. Right click it
    to see both the CPU and GPU readings, and to quit.
  * The tray icon is drawn at runtime as 7-segment digits showing the CPU
    temperature, and is colour coded: green below 60 °C, amber to 79 °C, red at
    80 °C and above, grey when there is no reading. It reuses the same segment
    patterns as the Pico firmware, so the tray and the TM1637 displays render a
    digit identically.
  * An application icon (`app.ico`), generated from that same renderer by the
    new `tools/IconGen` dev tool.
  * A `--kill` command line flag, which closes other running instances and frees
    the COM port they were holding.
  * Automated release builds. Pushing a version tag produces a self-contained
    single-file zip containing the exe, the two native serial helper DLLs,
    `main.py` for the Pico, the readme and the wiring diagram.
  * Build instructions in `TempSensorApp/readme.md` for compiling the exe
    yourself.
  * STLs for a printable two part case, under `case/`.
  * Readme coverage of two things that used to catch people out: that the app
    has to be run as administrator before it can read the CPU, and how to start
    it at log on with Task Scheduler.

### Changed

  * Retargeted from `net10.0` to `net10.0-windows` and switched to WinForms, as
    required by the tray icon.
  * Split the single `Program.cs` into separate classes for temperature reading
    (`TemperatureMonitor`, `TemperatureServices`), the tray UI (`TrayPresenter`,
    `TrayIconRenderer`) and the serial link to the Pico (`PicoLink`). No
    behaviour change; the code is just easier to work on.
  * Prebuilt binaries are no longer committed to the repository. The `release/`
    directory is gone — download the zip from the Releases page instead.

### Fixed

  * The app no longer crashes when a second instance is started while one is
    already running. It now reports that the COM port is in use and tells you to
    close the other instance or run `TempSensorApp.exe --kill`.
  * A second instance no longer left the displays frozen on stale values by
    silently failing to take the serial port.
  * A missing temperature reading is now sent as `0.0` rather than being
    omitted. The firmware reads `0` as "no reading" and shows dashes; previously
    the app stayed silent, which tripped the firmware's 10 second timeout and
    put `E-20` on both displays.

## [1.0.0] - 2025-11-23

First release. Reads CPU and GPU temperature on Windows and pushes them over USB
serial to a Raspberry Pi Pico driving two TM1637 4-digit displays.

[Unreleased]: https://github.com/greg162/Retro-CPU-GPU-temp-display/compare/2.0.0...HEAD
[2.0.0]: https://github.com/greg162/Retro-CPU-GPU-temp-display/compare/1.0.0...2.0.0
[1.0.0]: https://github.com/greg162/Retro-CPU-GPU-temp-display/releases/tag/1.0.0

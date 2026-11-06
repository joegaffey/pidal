# PIDAL — PID Abstraction Layer

PIDAL is a Windows **PID abstraction layer**. It presents a standard USB HID PID
1.0 force-feedback device to HID clients and drives a physical wheel through
**DirectInput**. It exists for wheels that do not expose a usable standard PID
interface.

```text
HID PID client
        │  standard HID PID 1.0 output/feature reports
        ▼
PIDAL virtual wheel (HIDMaestro, UMDF2)
        │  parsed PID effects
        ▼
PIDAL DirectInput backend (Vortice.DirectInput)
        │  constant force effect
        ▼
physical wheel
```

## Status

Early implementation with a working CLI and WinForms GUI. The repository contains
the architecture, protocol mapping, the DirectInput backend, the HIDMaestro
virtual PID wheel, and the GUI shell. It is Windows-only; the bridge requires
administrator elevation because HIDMaestro creates virtual devices and installs
its UMDF2 driver. GUI and bridge are verified on Windows 11.

## Usage scenarios

- **HID/WebHID client:** PIDAL exposes a standard PID device. Browser apps using
  WebHID (for example [`webhid-wheel`](../webhid-wheel)) are one way to drive it.
  PIDAL does not depend on any particular client.
- **Direct force testing:** the CLI and GUI test the physical DirectInput path
  without a client.

## Implementation note

Chromium's HID parser exposes PID usage arrays in reverse declaration order.
PIDAL compensates for clients that rely on that order, including current
`webhid-wheel` builds. See [SPEC.md §7.1](docs/SPEC.md#71-reversed-usage-array-compatibility).

## Requirements

- Windows 10/11 x64
- .NET 10 SDK
- HIDMaestro SDK (not published on NuGet; clone or vendor the SDK)
- A physical wheel that exposes a DirectInput constant-force effect
- Administrator elevation when starting the bridge (PIDAL relaunches itself;
  `--list` and `--help` run unelevated)

## Build

```powershell
# HIDMaestro is consumed as a project reference.
git clone https://github.com/hifihedgehog/HIDMaestro.git ..\HIDMaestro

dotnet build src\Pidal.App\Pidal.App.csproj -c Release
dotnet publish src\Pidal.App\Pidal.App.csproj -c Release -r win-x64 -o dist\win-x64

# GUI
dotnet build src\Pidal.Gui\Pidal.Gui.csproj -c Release
dotnet publish src\Pidal.Gui\Pidal.Gui.csproj -c Release -r win-x64 -o dist\win-x64
```

If HIDMaestro lives elsewhere:

```powershell
dotnet build src\Pidal.App\Pidal.App.csproj -p:HIDMaestroRoot=C:\src\HIDMaestro
```

If you have a prebuilt `HIDMaestro.Core.dll` from a release ZIP instead of a
source checkout:

```powershell
dotnet build src\Pidal.App\Pidal.App.csproj -p:HIDMaestroCoreDll=C:\src\HIDMaestro.Core.dll
```

The published single-file executables are `dist\win-x64\Pidal.App.exe` (CLI)
and `dist\win-x64\Pidal.Gui.exe` (GUI). `src\Pidal.App\bin` and
`src\Pidal.Gui\bin` are only intermediate build output.

The CLI's `--list` and `--help` run in the current terminal. Starting the bridge
triggers a UAC prompt and relaunches PIDAL elevated. The GUI requires
administrator elevation on launch.

## GUI

Launch `Pidal.Gui.exe` (elevated). The GUI provides:

- physical wheel selection and refresh
- start/stop bridge with live status
- direct force test slider, independent of any HID client
- live steering/throttle/brake/clutch/button/hat display
- gain, invert, watchdog, poll rate, PID usage order, and verbose logging
- log pane and tray icon

The CLI remains available for diagnostics and headless use.

## Run

```powershell
# List attached DirectInput devices and their force-feedback capability
Pidal.App.exe --list

# Start the bridge, choosing a device by name fragment
Pidal.App.exe --device "G920"

# Start with a specific steering axis offset and inverted force
Pidal.App.exe --device "G920" --axis 0 --invert

# Dump axes, FFB actuators and effects (no virtual wheel needed)
Pidal.App.exe --diagnose

# Test the physical DirectInput path directly, bypassing the virtual wheel
Pidal.App.exe --device "G920" --test-force 3000 --test-force-ms 2000
```

Start your HID PID client and select **PIDAL Virtual Wheel**. It appears as a
standard PID device with constant force support.

## Windows smoke test

1. `Pidal.App.exe --list` shows the physical wheel with `FFB yes`.
2. `Pidal.App.exe --device "<wheel name>" --verbose` starts without an
   acquisition error and creates the virtual wheel.
3. In another terminal, `Pidal.App.exe --list` now also shows
   `PIDAL Virtual Wheel` with `VIRTUAL yes`.
4. Open a HID PID client (for example a WebHID app), choose
   **PIDAL Virtual Wheel**, and confirm it reports constant force support.
5. Apply a low force. The physical wheel should move; **Stop** should release it.
6. Start PadForge and repeat. Both virtual devices should remain visible; only
   one process can own the physical wheel.

If the client sees the virtual wheel but force does not reach the physical wheel,
test the DirectInput path first with `--test-force` or the GUI's direct force
test.

## Safety

- Constant force is stopped on process exit, device control stop/disable/reset,
  physical device disconnect, and (by default) when no PID output report has
  arrived for five seconds.
- `--watchdog-ms 0` disables the stale-output watchdog.
- The physical wheel is acquired exclusively. Close PadForge, vendor control
  software, and racing games while testing.
- This is a motor-control path. Start with low force.

## Design docs

- [SPEC.md](docs/SPEC.md) — architecture, contracts, PID report handling, safety.
- [PLAN.md](docs/PLAN.md) — milestones, acceptance criteria, risks.
- [PID-MAPPING.md](docs/PID-MAPPING.md) — report IDs and byte offsets.
- [COEXISTENCE.md](docs/COEXISTENCE.md) — PadForge and shared-driver behavior.

## Repository layout

```text
docs/               architecture, plan, coexistence, PID mapping
src/Pidal.Core/     PID parser, effect state, backend and virtual-wheel contracts
src/Pidal.DirectInput/  Vortice.DirectInput backend
src/Pidal.Virtual/  HIDMaestro virtual PID wheel
src/Pidal.App/      console host, CLI, manifest
src/Pidal.Gui/      WinForms GUI, tray icon, manifest
tests/              PID parser and bridge tests
```

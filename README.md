# PIDAL — PID Abstraction Layer

PIDAL is a Windows **PID abstraction layer**. It presents a standard USB HID PID
1.0 force-feedback wheel to any WebHID-capable application and drives a physical
wheel through **DirectInput**. It exists for wheels that do not expose a usable
standard PID interface.

```text
WebHID-capable app (e.g. webhid-wheel)
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

Early implementation. The repository currently contains the architecture,
protocol mapping, and the first DirectInput backend. It is Windows-only and
requires administrator elevation because HIDMaestro creates virtual devices and
installs its UMDF2 driver. Hardware validation is still in progress.

## Consumers

PIDAL exposes a standard PID device, so any WebHID PID client can use it.
[`webhid-wheel`](../webhid-wheel) is the reference consumer used during
development; PIDAL does not depend on it.

PIDAL does not add a network protocol. The client talks to the virtual wheel
through WebHID exactly as it would to a real PID wheel.

## Requirements

- Windows 10/11 x64
- .NET 10 SDK
- HIDMaestro SDK (not published on NuGet; clone or vendor the SDK)
- A physical wheel that exposes a DirectInput constant-force effect
- Chromium-based browser for WebHID
- Administrator elevation when starting the bridge (PIDAL relaunches itself;
  `--list` and `--help` run unelevated)

## Build

```powershell
# HIDMaestro is consumed as a project reference.
git clone https://github.com/hifihedgehog/HIDMaestro.git ..\HIDMaestro

dotnet build src\Pidal.App\Pidal.App.csproj -c Release
dotnet publish src\Pidal.App\Pidal.App.csproj -c Release -r win-x64 -o dist\win-x64
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

The published single-file executable is `dist\win-x64\Pidal.App.exe`.
`src\Pidal.App\bin` is only the intermediate build output.

`--list` and `--help` run in the current terminal. Starting the bridge triggers
a UAC prompt and relaunches PIDAL elevated, because HIDMaestro creates the virtual
HID device.

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

Open any WebHID PID client in Chrome/Edge, click **Add device**, and select
**PIDAL Virtual Wheel**. It appears as a standard PID device with constant force
support. The `webhid-wheel` demo is a convenient reference client.

## Windows smoke test

1. `Pidal.App.exe --list` shows the physical wheel with `FFB yes`.
2. `Pidal.App.exe --device "<wheel name>" --verbose` starts without an
   acquisition error and creates the virtual wheel.
3. In another terminal, `Pidal.App.exe --list` now also shows
   `PIDAL Virtual Wheel` with `VIRTUAL yes`.
4. In Chrome/Edge, open a WebHID PID client (for example the `webhid-wheel`
   demo), choose **PIDAL Virtual Wheel**, and confirm it reports constant force
   support.
5. Apply a low force. The physical wheel should move; **Stop** should release it.
6. Start PadForge and repeat. Both virtual devices should remain visible; only
   one process can own the physical wheel.

If the browser sees the virtual wheel but force does not reach the physical
wheel, test the DirectInput path first with a known constant-force harness or
PadForge's own force test.

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
tests/              PID parser tests
```

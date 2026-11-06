# PIDAL Implementation Plan

## Milestone 0 — Repository and contracts

- [x] Write spec, plan, coexistence notes, and PID mapping.
- [x] Scaffold `Pidal.Core`, `Pidal.DirectInput`, `Pidal.Virtual`, `Pidal.App`,
      and `Pidal.Core.Tests`.
- [x] Define `IFfbBackend`, `IVirtualWheel`, `WheelInputFrame`, and PID command
      records.
- [x] Implement the PID report parser and effect state.
- [x] Add parser tests.

Acceptance: `dotnet test tests/Pidal.Core.Tests` passes on any OS.

## Milestone 1 — DirectInput constant force

- [x] Enumerate DirectInput devices, exclude virtual devices, and select a
      physical wheel.
- [x] Set exclusive cooperative level and acquire.
- [x] Create a constant-force effect on the steering axis.
- [x] Start/update/stop the effect from PID commands.
- [x] Poll axes, buttons, and hat.
- [x] Report acquisition conflicts and device loss.

Acceptance on Windows:

1. `Pidal.App.exe --list` shows the physical wheel with FFB and excludes
   `PIDAL Virtual Wheel`.
2. A test harness can create the effect and move the wheel with a known
   magnitude.
3. Starting the bridge twice fails cleanly on the second instance with an
   "in use" message.

## Milestone 2 — HIDMaestro virtual PID wheel

- [x] Build the joystick descriptor with `HidDescriptorBuilder`.
- [x] Create the virtual controller with a stable identity key.
- [x] Publish PID Pool and initial PID State.
- [x] Forward output reports into `PidReportParser`.
- [x] Forward physical input as a raw HID input report.
- [x] Dispose only PIDAL's controller.

Acceptance on Windows:

1. `joy.cpl` shows `PIDAL Virtual Wheel`.
2. A WebHID PID client (e.g. `webhid-wheel`) lists it and reports constant force
   support.
3. Moving the physical wheel updates the browser input.
4. Applying force from the client moves the physical wheel.
5. PadForge and PIDAL can run together; each virtual device remains visible.

## Milestone 3 — Safety and robustness

- [x] Stale-output watchdog (default 5 s, configurable).
- [x] Device-control stop/disable/reset/pause handling.
- [x] Force clamp and gain.
- [x] Disconnect handling.
- [ ] Hot-plug re-enumeration (physical wheel unplug/replug).
- [ ] Hidden message window for DirectInput cooperative level.
- [ ] Graceful shutdown on console close/logoff.

## Milestone 4 — Packaging

- [x] Single-file, self-contained publish settings.
- [x] `asInvoker` manifest with bridge-start self-elevation.
- [ ] Signed release binary.
- [ ] Versioned release notes.
- [ ] Verify HIDMaestro driver install/uninstall policy in the release.

## Milestone 5 — Later backends and effects

- [ ] Backend interface for vendor HID writers (Logitech classic/HID++,
      Thrustmaster `0x60`, Fanatec).
- [ ] Periodic/spring/damper effects.
- [ ] Multiple simultaneous effects.
- [ ] Tray UI and diagnostics.
- [ ] TrueForce and haptics.

## Verification matrix

| Test | Environment | Status |
| --- | --- | --- |
| PID parser unit tests | Linux/Windows | Automated |
| Core build | Linux/Windows | Automated |
| DirectInput enumeration | Windows + wheel | Manual |
| Constant force | Windows + wheel | Manual |
| WebHID visibility | Windows + Chrome/Edge | Manual |
| End-to-end FFB | Windows + wheel + browser | Manual |
| PadForge coexistence | Windows + PadForge | Manual |
| Driver install/uninstall | Windows | Manual |

## Known risks

1. **Physical wheel FFB path.** DirectInput constant force is not universal.
   Some wheels need a vendor backend. The backend contract exists for that.
2. **Exclusive acquisition.** PadForge or a racing game may hold the wheel.
   PIDAL reports the conflict rather than fighting for it.
3. **WebHID PID client assumptions.** Some clients (including `webhid-wheel`)
   use EBI 1 when they cannot discover a Block Load Feature report. PIDAL accepts
   that path.
4. **Latency.** HIDMaestro's output reader is event-driven with an 8 ms fallback.
   Input polling defaults to 250 Hz. Measure on the target rig.
5. **Driver coexistence.** HIDMaestro is a shared driver. PIDAL never removes or
   downgrades it; a newer SDK may upgrade it, and running devices keep their
   loaded driver until restart.

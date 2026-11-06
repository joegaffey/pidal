# PIDAL Specification

## 1. Purpose

PIDAL is a **PID abstraction layer**: a Windows process that makes a non-PID
physical wheel look like a standard USB HID PID 1.0 wheel to any WebHID client,
and translates the client's standard PID effect stream into a physical wheel
control path. The first backend is DirectInput.

PIDAL is not a device driver replacement, a game input mapper, or a browser
transport. It is independent of any particular WebHID application.
`webhid-wheel` is the reference consumer used during development.

## 2. Scope

### In scope

- Windows 10/11 x64.
- A virtual HID PID 1.0 joystick/wheel created with HIDMaestro.
- Standard PID 1.0 output and feature reports from WebHID clients.
- Constant-force effects mapped to a physical DirectInput constant-force effect.
- Physical wheel input forwarded as standard HID input reports.
- Coexistence with PadForge and other HIDMaestro consumers.
- Safety stop, watchdog, and explicit device lifecycle.

### Out of scope for the first release

- Periodic, spring, damper, inertia, friction, ramp, custom, or condition effects.
- Multiple simultaneous physical effects (the PID side may allocate more than
  one EBI; PIDAL renders one active constant force at a time).
- Vendor-specific HID writers or SDKs (future backends behind the same contract).
- Linux/macOS.
- In-process tray UI. The first release is a console app.
- TrueForce, haptics, LEDs, displays, or telemetry.

## 3. Architecture

```text
┌─────────────────────────────────────────────────────────────┐
│ WebHID-capable client (e.g. webhid-wheel)                   │
│  - requestDevice() / standard HID PID 1.0 client            │
│  - input mapping / UI / calibration (client-owned)          │
└──────────────────────────────┬──────────────────────────────┘
                               │ WebHID input/output/feature reports
                               ▼
┌─────────────────────────────────────────────────────────────┐
│ PIDAL virtual wheel (HIDMaestro, UMDF2)                     │
│  - standard PID 1.0 report descriptor                       │
│  - PublishPidPool / PublishPidState                         │
│  - OutputReceived -> PidReportParser                        │
│  - SubmitRawReport <- physical input frames                 │
└──────────────────────────────┬──────────────────────────────┘
                               │ IFfbBackend
                               ▼
┌─────────────────────────────────────────────────────────────┐
│ PIDAL DirectInput backend (Vortice.DirectInput)             │
│  - explicit device selection, virtual devices excluded      │
│  - exclusive cooperative level, Acquire/Unacquire           │
│  - constant-force effect, SetParameters, Start/Stop         │
│  - Poll input at a configurable rate                        │
└──────────────────────────────┬──────────────────────────────┘
                               │ DirectInput8
                               ▼
┌─────────────────────────────────────────────────────────────┐
│ physical wheel                                              │
└─────────────────────────────────────────────────────────────┘
```

## 4. Northbound contract: virtual HID PID wheel

### 4.1 Device identity

| Field | Value |
| --- | --- |
| VID | `0xBEEF` |
| PID | `0xF001` |
| Product | `PIDAL Virtual Wheel` |
| Manufacturer | `PIDAL` |
| Type | Joystick (Generic Desktop usage `0x04`) |
| Connection | USB |
| Identity key | `pidal-virtual-wheel` |

The identity is deliberately distinct from PadForge's `0xBEEF:0xF000`
(`PadForge Game Controller`) so both virtual devices can coexist and the client
can distinguish them.

### 4.2 Report descriptor

PIDAL builds the descriptor with HIDMaestro's `HidDescriptorBuilder`:

```csharp
new HidDescriptorBuilder()
    .Joystick()
    .AddAxis(HMAxis.X, 16, -32768, 32767) // steering, signed
    .AddAxis(HMAxis.Y, 16)                // throttle, unsigned
    .AddAxis(HMAxis.Rz, 16)               // brake, unsigned
    .AddAxis(HMAxis.Rx, 16)               // clutch, unsigned
    .AddButtons(32)
    .AddHat(8)
    .AddPidFfbBlock()
    .Build();
```

`AddPidFfbBlock()` injects input Report ID `0x01` and emits the canonical
HIDMaestro PID FFB block. The block declares only one Feature report inside the
application collection (`0x11` Create New Effect); Block Load, Pool, and PID
State are served by the HIDMaestro driver from its shared section. Do not add
additional Feature reports inside the same collection.

### 4.3 Input report layout

`SubmitRawReport` takes **data bytes only**; the HIDMaestro driver prepends
Report ID `0x01`.

| Offset | Size | Field | Encoding |
| --- | --- | --- | --- |
| 0 | 2 | Steering (X) | signed 16-bit little-endian, `-32768..32767` |
| 2 | 2 | Throttle (Y) | unsigned 16-bit, `0..65535` |
| 4 | 2 | Brake (Rz) | unsigned 16-bit |
| 6 | 2 | Clutch (Rx) | unsigned 16-bit |
| 8 | 4 | Buttons 1..32 | bit 0 = button 1 |
| 12 | 1 | Hat | `0..7` = HID position, `8` or greater = centered |

Total input report on the wire: 14 bytes (1-byte report ID + 13 bytes).

Clients that follow the common convention (first Generic Desktop X axis as
steering, first button range as buttons, an 8-position hat) will map this report
directly. The reference `webhid-wheel` client does so and classifies the device
as a wheel from its product name.

### 4.4 PID reports

The virtual descriptor exposes the canonical HIDMaestro PID FFB block:

| Report | Direction | Purpose |
| --- | --- | --- |
| `0x11` | Output | Set Effect |
| `0x12` | Output | Set Envelope (ignored) |
| `0x13` | Output | Set Condition (ignored) |
| `0x14` | Output | Set Periodic (ignored) |
| `0x15` | Output | Set Constant Force |
| `0x16` | Output | Set Ramp Force (ignored) |
| `0x17` | Output | Custom Force Data (ignored) |
| `0x18` | Output | Download Force Sample (ignored) |
| `0x1A` | Output | Effect Operation |
| `0x1B` | Output | Block Free |
| `0x1C` | Output | Device Control |
| `0x1D` | Output | Device Gain |
| `0x1E` | Output | Set Custom Force (ignored) |
| `0x11` | Feature | Create New Effect |
| `0x12` | Feature read | Block Load (driver-served) |
| `0x13` | Feature read | PID Pool (driver-served) |
| `0x14` | Feature read | PID State (driver-served) |

See [PID-MAPPING.md](PID-MAPPING.md) for byte offsets.

### 4.5 Feature state

On start:

```csharp
controller.PublishPidPool(
    ramPoolSize: 0xFFFF,
    simultaneousEffectsMax: 8,
    deviceManagedPool: true,
    sharedParameterBlocks: false);

controller.PublishPidState(
    0,
    PidStateFlags.ActuatorsEnabled | PidStateFlags.ActuatorPower);
```

On effect start/stop, publish the current state with
`PidStateFlags.EffectPlaying`, `ActuatorsEnabled`, and `ActuatorPower` as
appropriate.

### 4.6 Create New Effect

Some PID clients, including the reference `webhid-wheel` client, use Effect Block
Index `1` without sending Create New Effect when they cannot discover a Block Load
Feature report. HIDMaestro deliberately omits the extra Feature reports to avoid a
`pid.dll` crash, so this path must work.

PIDAL accepts Set Effect / Set Constant Force / Effect Operation for EBI `1`
without a prior Create New Effect. If a client sends Create New Effect, the
HIDMaestro driver allocates an EBI and forwards the Feature report; PIDAL reads
the assigned EBI with `GetCurrentPidBlockLoad()` and accepts it.

## 5. Southbound contract: `IFfbBackend`

The core defines a wheel-agnostic backend contract. The DirectInput backend is
the first implementation.

```csharp
public interface IFfbBackend : IDisposable
{
    string Name { get; }
    bool IsOpen { get; }
    event EventHandler<string>? Disconnected;

    void Open();
    void Close();

    void StartConstantForce(int magnitude);
    void UpdateConstantForce(int magnitude);
    void StopConstantForce();

    void SetDeviceGain(int gain);
    void HandleDeviceControl(PidDeviceControl control);

    WheelInputFrame? PollInput();
}
```

`magnitude` is in the PID constant-force range `-10000..10000`. The backend is
responsible for clamping, gain, inversion, and physical axis selection.

## 6. DirectInput backend

### 6.1 Device selection

1. Enumerate `DeviceType.Driving` with `AttachedOnly`.
2. Fall back to `DeviceType.Joystick`.
3. Filter out virtual devices by product/instance name (`PIDAL`, `PadForge`).
4. Prefer devices with `Capabilities.Flags.HasFlag(DeviceFlags.ForceFeedback)`.
5. If `--device` is given, match by name fragment or instance GUID.
6. If more than one candidate remains, print the list and require `--device`.

Never use `devices[0]` without filtering: the virtual wheel and other gamepads
may appear in the same enumeration.

### 6.2 Cooperative level and acquisition

- Create a window handle. The first release is a console app, so
  `GetConsoleWindow()` is used.
- `SetCooperativeLevel(hwnd, CooperativeLevel.Exclusive | CooperativeLevel.Background)`.
- `SetDataFormat<RawJoystickState>()`.
- `Acquire()`.
- If acquisition fails because another process holds the wheel, report the
  conflict and exit; do not steal or reset the other process's device.

### 6.3 Effect

- Find an `EffectInfo` with `Type == EffectType.ConstantForce`.
- Prefer the device's force-feedback actuator object IDs: enumerate
  `GetObjects()`, keep objects whose `ObjectId.Flags` includes
  `DeviceObjectTypeFlags.ForceFeedbackActuator`, and pass those IDs with
  `EffectFlags.ObjectIds`. Steering-axis actuators are ordered first.
- If the device exposes no actuator objects, or object-ID creation fails, fall
  back to `EffectFlags.ObjectOffsets` with the steering axis offset (default X,
  offset 0).
- Create one effect with:
  - `Flags = EffectFlags.Cartesian | ObjectIds` (or `ObjectOffsets`)
  - `Duration = int.MaxValue`
  - `SamplePeriod = 0`
  - `Gain = 10000`
  - `TriggerButton = -1`
  - `TriggerRepeatInterval = int.MaxValue`
  - `Axes = actuatorIds` (or `[steeringAxisOffset]`)
  - `Directions = zeroed array matching Axes`
  - `Parameters = new ConstantForce { Magnitude = 0 }`
- Update magnitude with
  `EffectParameterFlags.TypeSpecificParameters`.
- Start with `Start(-1)`, stop with `Stop()`.
- On close: stop, unload/dispose the effect, unacquire, dispose the device and
  DirectInput object.

### 6.4 Input

- `Poll()` then `GetCurrentJoystickState()` at the configured rate (default
  250 Hz).
- Map `X` to steering, `Y` to throttle, `RotationZ` to brake, `RotationX` to
  clutch.
- Pack the first 32 buttons.
- Convert the first POV hat from hundredths of degrees to HID positions `0..7`;
  centered becomes `8`.
- If `Poll()` fails with device-lost/not-acquired, try one reacquire. If that
  fails, raise `Disconnected` and stop the effect.

## 7. Bridge behavior

`PidBridge` owns the effect state and applies parsed PID commands:

| PID command | Bridge action |
| --- | --- |
| Set Effect (`0x11`) | Record EBI, effect type, gain, direction. Per-effect gain scales magnitude. |
| Set Constant Force (`0x15`) | Record magnitude; if active, update backend. |
| Effect Operation (`0x1A`) Start/StartSolo | If constant force, start backend with current magnitude. |
| Effect Operation Stop | Stop backend. |
| Block Free (`0x1B`) | Remove block; stop if it was active. |
| Device Control (`0x1C`) | Enable/disable actuators, stop all, reset, pause, continue. |
| Device Gain (`0x1D`) | Store and apply device gain. |
| Create New Effect (`0x11` Feature) | Read assigned EBI; record. |
| Other effect reports | Ignored in the first release. |

Only one constant-force effect is rendered at a time. If a second EBI starts,
the bridge switches the backend to that EBI and logs the switch.

### 7.1 Reversed usage-array compatibility

Chromium's HID parser exposes the PID Effect Operation and Device Control usage
arrays in reverse declaration order. `webhid-wheel`'s generic PID client computes
array values from that order, so its Start/Stop and Device Control values arrive
swapped.

PIDAL detects the signature init sequence (`StopAllEffects`, `Reset`,
`Continue`) and, from then on, swaps:

- Effect Operation: Start <-> Stop
- Device Control: Enable <-> Continue, Disable <-> Pause, StopAll <-> Reset

This keeps PIDAL usable with the current reference client. The proper fix is in
the client: sort `field.uids` by usage page/id before computing `usageIndex`.

## 8. Safety

- **Watchdog:** if an effect is playing and no PID output report has arrived for
  `--watchdog-ms` (default 5000), stop the effect. `0` disables it.
- **Device control:** stop on Disable Actuators, Stop All Effects, Reset, and
  Pause.
- **Physical disconnect:** stop and report.
- **Process exit:** stop the effect, unacquire, dispose the virtual controller.
- **Force clamp:** clamp magnitude to the configured maximum and to
  `-10000..10000`.
- **No silent force:** never start an effect without an explicit Effect
  Operation Start (except when a magnitude update arrives for an already-active
  EBI).

## 9. Coexistence with PadForge

See [COEXISTENCE.md](COEXISTENCE.md). In short:

- Distinct VID/PID, product name, and identity key.
- Never call global HIDMaestro cleanup (`RemoveAllVirtualControllers`).
- Do not uninstall or downgrade the HIDMaestro driver.
- Only dispose PIDAL's own controller.
- Physical wheel contention is reported, not forced.

## 10. Non-goals and known gaps

- The reference client currently exposes constant force only; PIDAL matches that.
- The reference client's demo applies a single force on click. With the default
  watchdog, that force stops after five seconds. Applications that need sustained
  force must refresh it (or disable the watchdog).
- PID direction is ignored in the first release; use `--invert` if force pulls
  the wrong way.
- Effect duration is not enforced; the watchdog is the safety mechanism.
- The first release uses a console window handle for DirectInput. A hidden
  message window is a later cleanup.

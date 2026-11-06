# Coexistence with PadForge and other HIDMaestro consumers

PIDAL is an alternative to the PadForge wheel bridge, not a replacement for the
HIDMaestro driver. Both can be installed and running.

## What is shared

- The HIDMaestro UMDF2 driver and its signing certificate.
- The HIDMaestro SDK runtime.
- The machine's HID stack.

HIDMaestro is designed for multiple consumers. Each process gets its own
controller transport index, sections, events, and identity claims.

## What PIDAL does to stay out of the way

| Area | PIDAL behavior |
| --- | --- |
| Virtual device | VID `0xBEEF`, PID `0xF001`, product `PIDAL Virtual Wheel`. PadForge uses `0xBEEF:0xF000`, `PadForge Game Controller`. |
| Identity key | `pidal-virtual-wheel`, unique to PIDAL. |
| Cleanup | Disposes only its own `HMController`. Never calls `RemoveAllVirtualControllers`, `HMUninstall`, or global cleanup. |
| Driver install | Calls HIDMaestro's idempotent `InstallDriver()`. Does not uninstall or force a downgrade. |
| Registry | Does not write OEM-name overrides or unrelated HIDMaestro keys. |
| Physical wheel | Acquires exclusively. If another process holds it, PIDAL reports the conflict and exits. It does not unacquire or reset the other process's device. |
| Exit | Stops the effect, unacquires, disposes its controller, and lets HIDMaestro remove its device. |

## Running both

1. Start PadForge if you need it for another wheel.
2. Start PIDAL for the wheel you want to expose to WebHID.
3. In the browser, pick the correct virtual device:
   - `PIDAL Virtual Wheel` for PIDAL.
   - `PadForge Game Controller` for PadForge.
4. Do not assign the same physical wheel to both at once. DirectInput's
   exclusive acquisition means only one process can drive it.

## Driver version note

HIDMaestro's install policy makes a newer SDK's driver take precedence. If PIDAL
ships a newer HIDMaestro SDK than the installed PadForge, `InstallDriver()` may
upgrade the shared driver. Running PadForge controllers keep the driver they
loaded until their device restarts. This is HIDMaestro's documented behavior, not
a PIDAL-specific action.

## Failure messages

PIDAL should make these cases obvious:

- `Physical wheel is in use by another process` — close PadForge/game/vendor app.
- `No DirectInput force-feedback device found` — install the vendor driver, use
  PC/native mode, or use a different backend.
- `Virtual wheel not visible in WebHID` — confirm PIDAL is still running and the
  HIDMaestro driver is installed.
- `PIDAL Virtual Wheel is not a physical device` — the selector excluded it;
  choose the physical wheel.

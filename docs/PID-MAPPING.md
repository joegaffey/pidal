# PID 1.0 report mapping

All offsets are **relative to `HMOutputPacket.Data`**, which excludes the HID
Report ID. `HMOutputPacket.ReportId` carries the report ID.

The virtual descriptor is the canonical HIDMaestro PID FFB block emitted by
`HidDescriptorBuilder.AddPidFfbBlock()`. Report layouts below are the subset
PIDAL parses in the first release.

## Output reports

### `0x11` Set Effect

| Offset | Size | Field | Notes |
| --- | --- | --- | --- |
| 0 | 1 | Effect Block Index | 1..40 |
| 1 | 1 | Effect Type | 1 = Constant Force |
| 2 | 2 | Duration | ms, little-endian |
| 4 | 2 | Trigger Repeat Interval | ms |
| 6 | 2 | Sample Period | ms |
| 8 | 2 | Start Delay | ms |
| 10 | 1 | Gain | 0..255 |
| 11 | 1 | Trigger Button | 0 = none |
| 12 | 1 | Axes Enable + Direction Enable | bit 0 X, bit 1 Y, bit 2 direction enable |
| 13 | 2 | Direction 1 | 0..32767, hundredths of a degree |
| 15 | 2 | Direction 2 | 0..32767 |
| 17 | 2 | Direction 1 (second collection) | 0..32765 |
| 19 | 2 | Direction 2 (second collection) | 0..32765 |

PIDAL requires at least 13 bytes and reads offsets 0..12. Direction and the
second collection are ignored in the first release.

### `0x15` Set Constant Force

| Offset | Size | Field | Notes |
| --- | --- | --- | --- |
| 0 | 1 | Effect Block Index | |
| 1 | 2 | Magnitude | signed 16-bit, `-10000..10000` |

### `0x1A` Effect Operation

| Offset | Size | Field | Values |
| --- | --- | --- | --- |
| 0 | 1 | Effect Block Index | |
| 1 | 1 | Operation | 1 Start, 2 Start Solo, 3 Stop |
| 2 | 1 | Loop Count | |

### `0x1B` Block Free

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 1 | Effect Block Index |

### `0x1C` Device Control

| Offset | Size | Field | Values |
| --- | --- | --- | --- |
| 0 | 1 | Control | 1 Enable Actuators, 2 Disable Actuators, 3 Stop All Effects, 4 Device Reset, 5 Device Pause, 6 Device Continue |

### `0x1D` Device Gain

| Offset | Size | Field | Values |
| --- | --- | --- | --- |
| 0 | 1 | Gain | 0..255 |

### Ignored output reports

`0x12` Set Envelope, `0x13` Set Condition, `0x14` Set Periodic, `0x16` Set Ramp
Force, `0x17` Custom Force Data, `0x18` Download Force Sample, `0x1E` Set Custom
Force.

## Feature reports

### `0x11` Create New Effect (host write)

| Offset | Size | Field | Notes |
| --- | --- | --- | --- |
| 0 | 1 | Effect Type | |
| 1 | 2 | Byte Count | 10 bits + 6 bits padding, little-endian |

The HIDMaestro driver allocates the EBI synchronously and exposes it via
`HMController.GetCurrentPidBlockLoad()`. The Feature report is also forwarded to
the consumer as `HMOutputSource.HidFeature`.

### `0x12` Block Load (host read)

Served by the HIDMaestro driver from the PID shared section. Wire format:

```text
[reportId, EBI, LoadStatus, RAMPool LSB, RAMPool MSB]
```

### `0x13` PID Pool (host read)

```text
[reportId, RAMPool LSB, RAMPool MSB, MaxSimultaneousEffects, MemoryManagement]
```

### `0x14` PID State (host read)

```text
[reportId, EBI, StateFlags]
```

PIDAL publishes Pool and State through `PublishPidPool` / `PublishPidState`. It
does not publish Block Load; the driver allocates it.

## Reference client behavior (`webhid-wheel`)

The reference `webhid-wheel` generic PID client:

1. Sends Device Reset (`0x1C` = 4), Stop All (`0x1C` = 3), Enable Actuators
   (`0x1C` = 1).
2. Sets Device Gain to maximum (`0x1D`).
3. If it finds a Block Load Feature report, sends Create New Effect and polls
   Block Load for an EBI. HIDMaestro's canonical block does not expose Block
   Load as a Feature, so this step is skipped and EBI `1` is used.
4. Sends Set Effect (`0x11`), Set Constant Force (`0x15`), Effect Operation
   Start (`0x1A`).
5. Subsequent `constantFast()` updates send only Set Constant Force.
6. Stop sends Effect Operation Stop and Set Constant Force with magnitude 0.

PIDAL therefore accepts EBI `1` without Create New Effect and must not require a
Block Load handshake.

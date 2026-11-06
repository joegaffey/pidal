namespace Pidal.Core.Pid;

/// <summary>
/// Mirrors HIDMaestro's HMOutputSource without taking a dependency on the SDK.
/// </summary>
public enum PidReportSource : byte
{
    HidOutput = 0,
    HidFeature = 1,
    XInput = 2,
    HidFeatureRead = 3,
}

namespace Pidal.DirectInput;

public sealed record DirectInputDeviceInfo(
    Guid InstanceGuid,
    string InstanceName,
    string ProductName,
    string Type,
    bool ForceFeedback,
    bool IsVirtual);

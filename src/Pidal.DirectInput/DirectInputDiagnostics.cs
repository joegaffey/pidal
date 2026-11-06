namespace Pidal.DirectInput;

public sealed record DirectInputDeviceDiagnostics(
    Guid InstanceGuid,
    string InstanceName,
    string ProductName,
    string Type,
    bool ForceFeedback,
    bool IsVirtual,
    string? Error,
    IReadOnlyList<DirectInputAxisDiagnostics> Axes,
    IReadOnlyList<DirectInputEffectDiagnostics> Effects);

public sealed record DirectInputAxisDiagnostics(
    string Name,
    string ObjectType,
    ushort UsagePage,
    ushort Usage,
    int Offset,
    int ObjectId,
    string ObjectIdFlags,
    bool ForceFeedbackActuator);

public sealed record DirectInputEffectDiagnostics(
    string Name,
    string Type,
    Guid Guid);

namespace Pidal.DirectInput;

/// <summary>Options for the DirectInput force-feedback backend.</summary>
public sealed record DirectInputOptions
{
    /// <summary>Case-insensitive fragment of the DirectInput product or instance name.</summary>
    public string? DeviceName { get; init; }

    /// <summary>DirectInput instance GUID, when the caller already knows it.</summary>
    public Guid? DeviceGuid { get; init; }

    /// <summary>
    /// DirectInput data-format offset of the steering axis. Null means discover
    /// the first Generic Desktop X axis, falling back to the first axis.
    /// </summary>
    public int? AxisOffset { get; init; }

    public bool InvertForce { get; init; }

    public int MaxForce { get; init; } = 10000;

    /// <summary>DirectInput effect gain, 0..10000.</summary>
    public int Gain { get; init; } = 10000;

    public bool Exclusive { get; init; } = true;
}

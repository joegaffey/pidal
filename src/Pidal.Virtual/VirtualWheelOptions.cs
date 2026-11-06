namespace Pidal.Virtual;

public sealed record VirtualWheelOptions
{
    public ushort VendorId { get; init; } = 0xBEEF;

    public ushort ProductId { get; init; } = 0xF001;

    public string ProductName { get; init; } = "PIDAL Virtual Wheel";

    public string Manufacturer { get; init; } = "PIDAL";

    public string IdentityKey { get; init; } = "pidal-virtual-wheel";

    public ushort RamPoolSize { get; init; } = 0xFFFF;

    public byte MaxSimultaneousEffects { get; init; } = 8;
}

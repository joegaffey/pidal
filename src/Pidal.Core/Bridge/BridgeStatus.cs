namespace Pidal.Core.Bridge;

public sealed record BridgeStatus(
    bool Running,
    bool BackendConnected,
    string BackendName,
    string VirtualWheelName,
    int ActiveEffectBlock,
    bool EffectPlaying,
    bool ActuatorsEnabled,
    bool Paused,
    bool ReverseUsageOrder);

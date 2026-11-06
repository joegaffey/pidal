using Pidal.Core.Pid;

namespace Pidal.Core.Abstractions;

/// <summary>
/// Southbound force-feedback backend. The DirectInput implementation is the
/// first backend; vendor HID writers can implement the same contract later.
/// </summary>
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

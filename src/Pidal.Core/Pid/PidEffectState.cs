namespace Pidal.Core.Pid;

/// <summary>
/// Tracks PID effect blocks by Effect Block Index. The bridge owns the lock.
/// </summary>
public sealed class PidEffectState
{
    private readonly Dictionary<byte, PidEffectBlock> _blocks = new();

    public byte ActiveIndex { get; private set; }

    public PidEffectBlock? Active =>
        ActiveIndex != 0 && _blocks.TryGetValue(ActiveIndex, out var block) ? block : null;

    public IReadOnlyCollection<PidEffectBlock> Blocks => _blocks.Values;

    public PidEffectBlock GetOrCreate(byte index)
    {
        if (!_blocks.TryGetValue(index, out var block))
        {
            block = new PidEffectBlock(index);
            _blocks[index] = block;
        }

        return block;
    }

    public bool TryGet(byte index, out PidEffectBlock block)
    {
        return _blocks.TryGetValue(index, out block!);
    }

    public PidEffectBlock? Remove(byte index)
    {
        if (!_blocks.Remove(index, out var block))
        {
            return null;
        }

        if (ActiveIndex == index)
        {
            ActiveIndex = 0;
        }

        return block;
    }

    public void SetActive(byte index)
    {
        ActiveIndex = index;
    }

    public void Clear()
    {
        _blocks.Clear();
        ActiveIndex = 0;
    }
}

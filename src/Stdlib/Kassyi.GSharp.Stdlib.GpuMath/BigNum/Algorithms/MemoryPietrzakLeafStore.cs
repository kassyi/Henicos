namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>In-memory <see cref="IPietrzakLeafStore"/> used when <see cref="FermatPrpOptions.ProofLeafFile"/> is unset.</summary>
internal sealed class MemoryPietrzakLeafStore : IPietrzakLeafStore
{
    private readonly uint[][] _slots;
    private readonly bool[] _present;

    public MemoryPietrzakLeafStore(PietrzakSchedule schedule)
    {
        TotalLeafSlots = schedule.TotalLeafSlots;
        ResidueWords = schedule.ResidueWords;
        _slots = new uint[schedule.TotalStorageSlots][];
        _present = new bool[TotalLeafSlots];
    }

    public int TotalLeafSlots { get; }

    public int ResidueWords { get; }

    public bool HasLeafSlot(int leafSlot) => _present[leafSlot];

    public void ResetLeaves() => Array.Clear(_present);

    public void WriteSlot(int slot, ReadOnlySpan<uint> words)
    {
        if (words.Length != ResidueWords)
        {
            throw new ArgumentException($"Expected {ResidueWords} words, got {words.Length}.", nameof(words));
        }

        var target = _slots[slot];
        if (target == null)
        {
            target = new uint[ResidueWords];
            _slots[slot] = target;
        }

        words.CopyTo(target);
        if (slot < TotalLeafSlots)
        {
            _present[slot] = true;
        }
    }

    public void ReadSlot(int slot, Span<uint> destination)
    {
        var source = _slots[slot]
            ?? throw new InvalidOperationException($"Pietrzak slot {slot} has not been written.");
        source.AsSpan().CopyTo(destination);
    }

    public void Flush()
    {
        // In-memory slots require no flush.
    }

    public void Dispose()
    {
        // Managed buffers only.
    }
}


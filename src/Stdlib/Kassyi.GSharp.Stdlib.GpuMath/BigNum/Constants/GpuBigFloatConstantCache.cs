namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Constants;

/// <summary>
/// Keeps the device copies of the transcendental constants (ln 2) of one <see cref="GpuWorkspace"/>, uploaded once
/// and handed out as borrowed views.
/// </summary>
/// <remarks>
/// <para>
/// ln 2 is held as one fixed-point integer <c>floor(ln 2 · 2^F)</c> in <c>F / 32</c> words (ln 2 &lt; 1, so there is no
/// integer word). A request for fewer fraction bits is a view of the top words, which equals the floor at that
/// precision: dropping whole low words of a floor is the floor of the shorter value.
/// </para>
/// <para>
/// A request beyond the uploaded precision uploads at least twice as many bits and releases the previous buffer in
/// stream order. A view is valid until the next request that grows the constant, or until the workspace is
/// disposed; callers use it within one operation.
/// </para>
/// <para><b>Thread Safety:</b> requests are serialized by a lock.</para>
/// </remarks>
internal sealed class GpuBigFloatConstantCache(GpuWorkspace workspace) : IDisposable
{
    private readonly Lock _lock = new();
    private GpuBigUInt? _ln2;
    private int _ln2FractionBits;

    /// <summary>
    /// Returns a borrowed view of <c>floor(ln 2 · 2^F)</c> with <c>F = 32·ceil(fractionBits / 32)</c>.
    /// </summary>
    /// <param name="fractionBits">The minimum number of fraction bits; at least 1.</param>
    /// <returns>A non-owning view of <c>F / 32</c> words; do not dispose it.</returns>
    /// <exception cref="ObjectDisposedException">The cache has been disposed.</exception>
    internal GpuBigUInt GetLn2(int fractionBits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fractionBits, 1);
        var words = (fractionBits + 31) / 32;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_ln2FractionBits < 0, this);
            if (_ln2 is not { } current || 32 * words > _ln2FractionBits)
            {
                var uploadWords = (int)Math.Min(int.MaxValue / 32, Math.Max(words, 2L * _ln2FractionBits / 32));
                var value = GpuBigFloatConstants.GetLn2Fixed(32 * uploadWords);
                var uploaded = GpuBigUInt.FromBigInteger(workspace, value, uploadWords);
                if (_ln2 is { } previous)
                {
                    previous.Dispose();
                }

                _ln2 = uploaded;
                _ln2FractionBits = 32 * uploadWords;
                current = uploaded;
            }

            var handle = current.Handle;
            var offset = _ln2FractionBits / 32 - words;
            return new GpuBigIntMemoryHandle(handle.Buffer, handle.WordOffset + offset, words)
                .AsBorrowedBigUInt(workspace);
        }
    }

    /// <summary>
    /// Releases the device copies of the constants.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_ln2 is { } ln2)
            {
                ln2.Dispose();
            }

            _ln2 = null;
            _ln2FractionBits = -1;
        }
    }
}

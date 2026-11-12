namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// A signed value inside the evaluation of an expression: its magnitude and the source of its sign. The public
// GpuBigInt keeps a host sign; a device sign is read once, at the root.
internal readonly struct SignedValue([TakesOwnership] GpuBigUInt magnitude, [TakesOwnership] SignSource sign)
    : IDisposable
{
    public GpuBigUInt Magnitude { get; } = magnitude;

    public SignSource Sign { get; } = sign;

    public static SignedValue FromHost(GpuBigInt value) => new(value.Value, SignSource.Host(value.IsNegative));

    // The value with its host sign; a device sign must have been read before
    public GpuBigInt ToHost() =>
        Sign.IsDevice
            ? throw new InvalidOperationException("The sign of the value is on the device and has not been read.")
            : new GpuBigInt(Magnitude, Sign.HostNegative);

    // Nested, so that a magnitude whose release throws does not keep the sign word
    public void Dispose()
    {
        try
        {
            Magnitude.Dispose();
        }
        finally
        {
            Sign.Dispose();
        }
    }
}

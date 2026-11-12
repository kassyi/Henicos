namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// A shared dummy enumerator struct to satisfy the C# 12 <see cref="CollectionBuilderAttribute"/> element type requirement (CS9188).
/// </summary>
/// <remarks>
/// <para><b>Execution Target:</b> Host compile-time contract support for collection expressions. Never allocated or iterated at runtime.</para>
/// </remarks>
[ExcludeFromCodeCoverage]
public struct DummyUIntEnumerator
{
    /// <summary>
    /// Gets a default element value of 0.
    /// </summary>
    public uint Current => 0;

    /// <summary>
    /// Always returns <see langword="false"/> to indicate an empty sequence.
    /// </summary>
    /// <returns><see langword="false"/> unconditionally.</returns>
    public bool MoveNext() => false;
}

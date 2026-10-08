namespace Kassyi.ZeroAlloc.Linq;

/// <summary>
/// Represents a stack-allocated, zero-allocation sequence wrapping a struct-based enumerator.
/// </summary>
/// <typeparam name="TEnumerator">The underlying enumerator type implementing <see cref="IStackEnumerator{T}"/>.</typeparam>
/// <typeparam name="T">The type of elements in the sequence.</typeparam>
/// <remarks>
/// <para><b>Allocation:</b> This is a <see langword="ref struct"/> and cannot be boxed or stored on the managed heap, guaranteeing zero GC pressure.</para>
/// <para><b>Usage:</b> Intended for high-performance zero-allocation transformations such as Roslyn Source Generators and GPU kernel preparation.</para>
/// </remarks>
public readonly ref struct StackEnumerable<TEnumerator, T>
    where TEnumerator : struct, IStackEnumerator<T>, allows ref struct
{
    /// <summary>
    /// The underlying enumerator that drives this sequence.
    /// </summary>
    public readonly TEnumerator Enumerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="StackEnumerable{TEnumerator, T}"/> struct wrapping the specified enumerator.
    /// </summary>
    /// <param name="enumerator">The struct enumerator to wrap.</param>
    public StackEnumerable(TEnumerator enumerator)
    {
        Enumerator = enumerator;
    }
}

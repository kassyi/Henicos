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

    /// <summary>
    /// Filters the sequence down to the elements of type <typeparamref name="TResult"/>.
    /// </summary>
    /// <typeparam name="TResult">The type to filter the elements on.</typeparam>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, T}"/> over the elements of type <typeparamref name="TResult"/>, in order.</returns>
    /// <remarks>
    /// <para><b>Usage:</b> An instance method rather than an extension, so that a call names only
    /// <typeparamref name="TResult"/> (<c>nodes.AsStackEnumerable().OfType&lt;FieldDeclarationSyntax&gt;()</c>); an
    /// extension would need its enumerator and source types spelled out as well. A <see langword="null"/> element is
    /// never of <typeparamref name="TResult"/>, so <c>Select(x =&gt; x as T).OfType&lt;T&gt;()</c> drops the misses.</para>
    /// <para><b>Execution Target:</b> Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.</para>
    /// </remarks>
    public StackEnumerable<OfTypeEnumerator<TEnumerator, T, TResult>, TResult> OfType<TResult>() =>
        new(new OfTypeEnumerator<TEnumerator, T, TResult>(Enumerator));
}

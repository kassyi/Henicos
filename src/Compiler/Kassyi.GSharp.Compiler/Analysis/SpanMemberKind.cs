namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The kind of <c>Span&lt;T&gt;</c>, <c>ReadOnlySpan&lt;T&gt;</c>, <c>MemoryExtensions</c>, or <c>System.Array</c>
/// method call classified by <see cref="SpanWriteClassifier"/> (#731).
/// </summary>
internal enum SpanMemberKind
{
    /// <summary>Not a method of <c>Span&lt;T&gt;</c>, <c>ReadOnlySpan&lt;T&gt;</c>, <c>MemoryExtensions</c>, or <c>System.Array</c>.</summary>
    None,

    /// <summary>A method on one of the span or array types that G# does not map in device code (reported as GSH0003).</summary>
    Unsupported,

    /// <summary><c>Span&lt;T&gt;.Fill(T)</c>.</summary>
    Fill,

    /// <summary><c>Span&lt;T&gt;.Clear()</c>.</summary>
    Clear,

    /// <summary><c>Span&lt;T&gt;.CopyTo(Span&lt;T&gt;)</c>, <c>ReadOnlySpan&lt;T&gt;.CopyTo(Span&lt;T&gt;)</c>, or <c>MemoryExtensions.CopyTo&lt;T&gt;(T[], Span&lt;T&gt;)</c>.</summary>
    CopyTo,

    /// <summary><c>Span&lt;T&gt;.TryCopyTo(Span&lt;T&gt;)</c> or <c>ReadOnlySpan&lt;T&gt;.TryCopyTo(Span&lt;T&gt;)</c>.</summary>
    TryCopyTo,

    /// <summary><c>MemoryExtensions.Reverse&lt;T&gt;(Span&lt;T&gt;)</c> or <c>MemoryExtensions.Reverse&lt;T&gt;(T[])</c>.</summary>
    Reverse,

    /// <summary><c>Span&lt;T&gt;.Slice(int)</c> or <c>ReadOnlySpan&lt;T&gt;.Slice(int)</c>.</summary>
    Slice1,

    /// <summary><c>Span&lt;T&gt;.Slice(int, int)</c> or <c>ReadOnlySpan&lt;T&gt;.Slice(int, int)</c>.</summary>
    Slice2,

    /// <summary><c>MemoryExtensions.AsSpan&lt;T&gt;(T[])</c>.</summary>
    AsSpan0,

    /// <summary><c>MemoryExtensions.AsSpan&lt;T&gt;(T[], int)</c>.</summary>
    AsSpan1,

    /// <summary><c>MemoryExtensions.AsSpan&lt;T&gt;(T[], int, int)</c>.</summary>
    AsSpan2,

    /// <summary><c>Array.Fill&lt;T&gt;(T[], T)</c>.</summary>
    ArrayFill2,

    /// <summary><c>Array.Fill&lt;T&gt;(T[], T, int, int)</c>.</summary>
    ArrayFill4,

    /// <summary><c>Array.Clear(Array)</c>.</summary>
    ArrayClear1,

    /// <summary><c>Array.Clear(Array, int, int)</c>.</summary>
    ArrayClear3,

    /// <summary><c>Array.Reverse(Array)</c> or <c>Array.Reverse&lt;T&gt;(T[])</c>.</summary>
    ArrayReverse1,

    /// <summary><c>Array.Reverse(Array, int, int)</c> or <c>Array.Reverse&lt;T&gt;(T[], int, int)</c>.</summary>
    ArrayReverse3,

    /// <summary><c>Array.Copy(Array, Array, int)</c>.</summary>
    ArrayCopy3,

    /// <summary><c>Array.Copy(Array, int, Array, int, int)</c>.</summary>
    ArrayCopy5,

    /// <summary><c>MemoryExtensions.Sort</c>, <c>Array.Sort&lt;T&gt;(T[])</c>, or <c>Array.Sort&lt;TKey, TValue&gt;(TKey[], TValue[])</c>.</summary>
    Sort,
}

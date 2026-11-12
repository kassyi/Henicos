namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The kind of <c>Span&lt;T&gt;</c>, <c>ReadOnlySpan&lt;T&gt;</c>, or <c>System.Array</c> property classified by
/// <see cref="SpanWriteClassifier"/> (#731).
/// </summary>
internal enum SpanPropertyKind
{
    /// <summary>Not a property of <c>Span&lt;T&gt;</c>, <c>ReadOnlySpan&lt;T&gt;</c>, or <c>System.Array</c>.</summary>
    None,

    /// <summary>A property on one of the span or array types that G# does not map in device code (reported as GSH0003).</summary>
    Unsupported,

    /// <summary><c>Span&lt;T&gt;.Length</c>, <c>ReadOnlySpan&lt;T&gt;.Length</c>, or <c>Array.Length</c>.</summary>
    Length,

    /// <summary><c>Span&lt;T&gt;.IsEmpty</c> or <c>ReadOnlySpan&lt;T&gt;.IsEmpty</c>.</summary>
    IsEmpty,

    /// <summary><c>Span&lt;T&gt;.Empty</c> or <c>ReadOnlySpan&lt;T&gt;.Empty</c>.</summary>
    Empty,
}

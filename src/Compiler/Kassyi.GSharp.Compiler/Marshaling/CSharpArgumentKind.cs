namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>How a launcher's C# execution passes an argument to the kernel (<see cref="CSharpArgument"/>).</summary>
internal enum CSharpArgumentKind
{
    /// <summary>A copy of the launcher's parameter, which the lambda captures.</summary>
    Value,

    /// <summary>The same copy, passed with <c>in</c> to an <c>in</c> or <c>ref readonly</c> kernel parameter.</summary>
    In,

    /// <summary>
    /// A <c>ref</c> argument, pinned for the run and passed through its pointer, so that the kernel writes the caller's
    /// variable and two parameters that receive one variable stay one variable (#1061).
    /// </summary>
    Ref,

    /// <summary>The same for an <c>out</c> argument, which the launcher has set to <c>default</c>.</summary>
    Out,

    /// <summary>
    /// An <c>in</c> argument that the kernel receives by address, pinned for the run and passed through its pointer
    /// like <see cref="Ref"/>, so that a write through a <c>ref</c> parameter of the same variable shows through it.
    /// </summary>
    InReference,

    /// <summary>A <c>Span&lt;T&gt;</c>, pinned for the run and rebuilt in the lambda from pointer and length.</summary>
    Span,

    /// <summary>A <c>ReadOnlySpan&lt;T&gt;</c>, pinned and rebuilt as <see cref="Span"/> is.</summary>
    ReadOnlySpan
}

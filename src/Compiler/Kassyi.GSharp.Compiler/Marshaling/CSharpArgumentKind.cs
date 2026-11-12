namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>How a launcher's C# execution passes an argument to the kernel (<see cref="CSharpArgument"/>).</summary>
internal enum CSharpArgumentKind
{
    /// <summary>A copy of the launcher's parameter, which the lambda captures.</summary>
    Value,

    /// <summary>The same copy, passed with <c>in</c> to an <c>in</c> or <c>ref readonly</c> kernel parameter.</summary>
    In,

    /// <summary>
    /// A copy of a <c>ref</c> argument that the lambda captures and passes by <c>ref</c>, copied back after the run, as
    /// the GPU launchers copy the value to the device and back.
    /// </summary>
    Ref,

    /// <summary>The same for an <c>out</c> argument, which starts as <c>default</c>.</summary>
    Out,

    /// <summary>A <c>Span&lt;T&gt;</c>, pinned for the run and rebuilt in the lambda from pointer and length.</summary>
    Span,

    /// <summary>A <c>ReadOnlySpan&lt;T&gt;</c>, pinned and rebuilt as <see cref="Span"/> is.</summary>
    ReadOnlySpan
}

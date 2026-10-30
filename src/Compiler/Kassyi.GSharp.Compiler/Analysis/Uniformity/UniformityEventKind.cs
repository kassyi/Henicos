namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>What a <see cref="UniformityEvent"/> does.</summary>
internal enum UniformityEventKind
{
    /// <summary>A parameter receives its variance.</summary>
    Define,

    /// <summary>An operation writes locals, parameters or a flow capture.</summary>
    Write,

    /// <summary>An operation is evaluated; only read while recording the variance of each expression.</summary>
    Observe,

    /// <summary>A call that reaches a barrier; only read while recording the variance of its arguments.</summary>
    Site
}

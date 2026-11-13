namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>What a call of a function may write, besides its own locals and by-value parameters.</summary>
internal enum DeviceFunctionEffect
{
    /// <summary>Writes nothing that the caller can see.</summary>
    None,

    /// <summary>Writes only through its <c>ref</c> / <c>out</c> parameters (and, for a struct member, its receiver).</summary>
    RefParameters,

    /// <summary>May write any memory: array elements, pointers, shared memory, static fields, or calls that do.</summary>
    Memory
}

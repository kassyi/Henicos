namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Why only some threads of a block reach a node of a kernel or device function.
/// </summary>
internal enum PartialReach
{
    /// <summary>Every thread of the block reaches the node.</summary>
    None,

    /// <summary>The node is in the influence region of a thread-variant branch.</summary>
    Branch,

    /// <summary>The node runs only after an earlier thread-variant exit (return, break or jump).</summary>
    Exit
}

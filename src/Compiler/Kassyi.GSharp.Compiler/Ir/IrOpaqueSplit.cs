namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>Whether the syntax of an opaque node holds a call that splits the state machine of the CPU paths.</summary>
/// <remarks>
/// The calls inside an opaque node are not part of the tree, so the classify pass records the answer on the node: a
/// statement that holds a split point is then either fully in the IR or under an opaque node marked
/// <see cref="HoldsSplit"/>, never invisible.
/// </remarks>
internal enum IrOpaqueSplit
{
    /// <summary>The classify pass has not looked at the node.</summary>
    Unclassified,

    /// <summary>No invocation in the node splits.</summary>
    None,

    /// <summary>An invocation in the node splits.</summary>
    HoldsSplit
}

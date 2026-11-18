namespace Kassyi.GSharp.Compiler.Ir.Linear;

/// <summary>The kind of a label in the linear form of a method body.</summary>
internal enum IrLabelKind
{
    /// <summary>The label after a flattened <see langword="if"/> statement.</summary>
    BranchEnd,

    /// <summary>The label at the <see langword="else"/> branch of a flattened <see langword="if"/> statement.</summary>
    BranchElse,

    /// <summary>The label at the condition of a flattened <see langword="while"/> loop.</summary>
    WhileStart,

    /// <summary>The label before the back-edge of a flattened <see langword="while"/> loop.</summary>
    WhileContinue,

    /// <summary>The label after a flattened <see langword="while"/> loop.</summary>
    WhileEnd,

    /// <summary>The reconvergence continue label of a loop (reserved for warp reconvergence).</summary>
    ReconvergeContinue,

    /// <summary>A label written in the user's code (reserved for user labels).</summary>
    User
}

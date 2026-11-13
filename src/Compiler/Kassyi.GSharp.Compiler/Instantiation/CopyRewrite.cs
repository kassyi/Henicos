namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>One node that the copy writes differently from the declaration.</summary>
/// <param name="NodeIndex">The position of the node in <see cref="CopyNodeIndex.List"/> of the declaration.</param>
/// <param name="Kind">How the node is written.</param>
/// <param name="Type">
/// The type argument (<see cref="CopyRewriteKind.TypeArgument"/>), the type of a constant, or the type an operator is
/// converted back to.
/// </param>
/// <param name="Method">The extension method of <see cref="CopyRewriteKind.StaticExtensionCall"/>, in its static
/// form.</param>
/// <param name="TypeArguments">The type arguments a pinned call writes, already substituted.</param>
/// <param name="Constant">
/// The value of <see cref="CopyRewriteKind.IdentityConstant"/> or <see cref="CopyRewriteKind.NameOfConstant"/>, or the
/// shift mask of <see cref="CopyRewriteKind.NarrowedShift"/>.
/// </param>
/// <param name="Checked">
/// Whether a narrowed operator is the checked one of generic math (<c>op_CheckedAddition</c>), which throws on
/// overflow; the others never throw.
/// </param>
internal sealed record CopyRewrite(
    int NodeIndex,
    CopyRewriteKind Kind,
    ITypeSymbol? Type = null,
    IMethodSymbol? Method = null,
    ImmutableArray<ITypeSymbol> TypeArguments = default,
    object? Constant = null,
    bool Checked = false);

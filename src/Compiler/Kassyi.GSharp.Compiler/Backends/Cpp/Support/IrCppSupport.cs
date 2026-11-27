using Kassyi.GSharp.Compiler.Ir;

namespace Kassyi.GSharp.Compiler.Backends.Cpp.Support;

/// <summary>
/// The table of the forms the C++ writer spells: one group of rows per family of node kinds, asked in order. A node
/// no group answers is not spelled, with the shape of <see cref="IrSupportShapes.Describe"/>.
/// </summary>
internal static class IrCppSupport
{
    // A new family of nodes adds its group here
    private static readonly SupportGroup[] s_groups =
    [
        IrCppSupportStatements.TryOf,
        IrCppSupportLeaves.TryOf
    ];

    /// <summary>
    /// Whether the C++ writer spells <paramref name="node"/> itself on the route of <paramref name="profile"/>, not
    /// what is below it. <paramref name="parent"/> is the node that holds it (null for a root or when the caller has
    /// none).
    /// </summary>
    public static IrSupport Of(IrNode node, IrNode? parent, CppBodyProfile profile) =>
        Of(node, parent, profile, s_groups);

    /// <summary>
    /// Every node of the body of <paramref name="method"/> the writer cannot spell on the route of
    /// <paramref name="profile"/>, in pre-order; empty when it spells all. Remembers the parent while it walks and
    /// passes it to <see cref="Of(IrNode, IrNode?, CppBodyProfile)"/>.
    /// </summary>
    public static ImmutableArray<IrSupportKey> Check(IrMethod method, CppBodyProfile profile) =>
        Check(method, profile, s_groups);

    /// <summary>The same walk with <paramref name="groups"/> in place of the rows of the writer.</summary>
    internal static ImmutableArray<IrSupportKey> Check(IrMethod method, CppBodyProfile profile, SupportGroup[] groups)
    {
        var unsupported = ImmutableArray.CreateBuilder<IrSupportKey>();
        Walk(method.Body, null, profile, groups, unsupported);
        return unsupported.ToImmutable();
    }

    private static void Walk(IrNode node, IrNode? parent, CppBodyProfile profile, SupportGroup[] groups,
        ImmutableArray<IrSupportKey>.Builder unsupported)
    {
        var support = Of(node, parent, profile, groups);
        if (!support.IsSupported)
        {
            unsupported.Add(IrSupportKey.Of(profile.Route, node, support));
        }

        foreach (var child in IrNodeChildren.Of(node))
        {
            Walk(child, node, profile, groups, unsupported);
        }
    }

    private static IrSupport Of(IrNode node, IrNode? parent, CppBodyProfile profile, SupportGroup[] groups)
    {
        foreach (var group in groups)
        {
            if (group(node, parent, profile, out var support))
            {
                return support;
            }
        }

        return IrSupport.Unspelled(IrSupportShapes.Describe(node));
    }
}

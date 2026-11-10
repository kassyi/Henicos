namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Fully qualified type and member names, and syntax builders, of the managed runtime context emitted into host
/// wrappers and lowered state machines.
/// </summary>
internal static class ManagedNames
{
    private const string TypeName = "ManagedGpuContext";

    /// <summary>
    /// The fully qualified name of the managed runtime context,
    /// <c>global::Kassyi.GSharp.Core.ManagedGpuContext</c>.
    /// </summary>
    public const string Context = $"global::{TypeConstants.CoreNamespace}.{TypeName}";

    /// <summary>
    /// The thread-static flag through which a managed state machine requests a grid sync from the managed launcher
    /// (<see cref="ManagedFallbackEmitter"/>).
    /// </summary>
    public const string GridSyncRequested = $"{Context}.{GridSyncRequestedName}";

    /// <summary>
    /// The thread-static flag through which a managed state machine tells the managed launcher that the thread waits
    /// at a block barrier (<see cref="ManagedFallbackEmitter"/>).
    /// </summary>
    public const string BlockBarrierRequested = $"{Context}.{BlockBarrierRequestedName}";

    public const string ThreadIdxX = $"{Context}.{ThreadIdxXName}";
    public const string ThreadIdxY = $"{Context}.{ThreadIdxYName}";
    public const string ThreadIdxZ = $"{Context}.{ThreadIdxZName}";
    public const string BlockIdxX = $"{Context}.{BlockIdxXName}";
    public const string BlockIdxY = $"{Context}.{BlockIdxYName}";
    public const string BlockIdxZ = $"{Context}.{BlockIdxZName}";

    internal const string GridSyncRequestedName = "GridSyncRequested";
    internal const string BlockBarrierRequestedName = "BlockBarrierRequested";
    internal const string ThreadIdxXName = "ThreadIdxX";
    internal const string ThreadIdxYName = "ThreadIdxY";
    internal const string ThreadIdxZName = "ThreadIdxZ";
    internal const string BlockIdxXName = "BlockIdxX";
    internal const string BlockIdxYName = "BlockIdxY";
    internal const string BlockIdxZName = "BlockIdxZ";

    // global::Kassyi.GSharp.Core.ManagedGpuContext
    private static readonly NameSyntax s_contextName = BuildContextName();

    /// <summary>
    /// Returns the syntax node for <c>global::Kassyi.GSharp.Core.ManagedGpuContext</c>.
    /// </summary>
    public static NameSyntax ContextName => s_contextName;

    // global::Kassyi.GSharp.Core.ManagedGpuContext.<memberName>
    public static MemberAccessExpressionSyntax CreateMemberAccess(string memberName) =>
        CreateMemberAccess(SyntaxFactory.IdentifierName(memberName));

    // global::Kassyi.GSharp.Core.ManagedGpuContext.<member>
    public static MemberAccessExpressionSyntax CreateMemberAccess(SimpleNameSyntax member) =>
        SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, s_contextName, member);

    // global::Kassyi.GSharp.Core.ManagedGpuContext
    private static NameSyntax BuildContextName()
    {
        NameSyntax? current = null;
        var start = 0;
        while (true)
        {
            var dot = TypeConstants.CoreNamespace.IndexOf('.', start);
            var part = dot < 0
                ? TypeConstants.CoreNamespace[start..]
                : TypeConstants.CoreNamespace[start..dot];
            var identifier = SyntaxFactory.IdentifierName(part);
            current = current is null
                ? SyntaxFactory.AliasQualifiedName(
                    SyntaxFactory.IdentifierName(SyntaxFactory.Token(SyntaxKind.GlobalKeyword)),
                    identifier
                )
                : SyntaxFactory.QualifiedName(current, identifier);
            if (dot < 0)
            {
                return SyntaxFactory.QualifiedName(current, SyntaxFactory.IdentifierName(TypeName));
            }

            start = dot + 1;
        }
    }
}

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Fully qualified type and member names, and syntax builders, of the managed runtime context emitted into host
/// wrappers and lowered state machines.
/// </summary>
/// <remarks>
/// Emitters write a member of the context as <c>{Context}.{member}</c>; the member names below are the ones that more
/// than one file writes.
/// </remarks>
internal static class ManagedNames
{
    /// <summary>
    /// The fully qualified name of the managed runtime context,
    /// <c>global::Kassyi.GSharp.Core.ManagedGpuContext</c>.
    /// </summary>
    public const string Context = $"global::{TypeConstants.CoreNamespace}.ManagedGpuContext";

    /// <summary>
    /// The thread-static flag through which a managed state machine requests a grid sync from the managed launcher
    /// (<see cref="ManagedFallbackEmitter"/>).
    /// </summary>
    public const string GridSyncRequestedName = "GridSyncRequested";

    /// <summary>
    /// The thread-static flag through which a managed state machine tells the managed launcher that the thread waits
    /// at a block barrier (<see cref="ManagedFallbackEmitter"/>).
    /// </summary>
    public const string BlockBarrierRequestedName = "BlockBarrierRequested";

    /// <summary>The X component of the thread index of the running managed thread.</summary>
    public const string ThreadIdxXName = "ThreadIdxX";

    /// <summary>The Y component of the thread index of the running managed thread.</summary>
    public const string ThreadIdxYName = "ThreadIdxY";

    /// <summary>The Z component of the thread index of the running managed thread.</summary>
    public const string ThreadIdxZName = "ThreadIdxZ";

    /// <summary>The X component of the block index of the running managed thread.</summary>
    public const string BlockIdxXName = "BlockIdxX";

    /// <summary>The Y component of the block index of the running managed thread.</summary>
    public const string BlockIdxYName = "BlockIdxY";

    /// <summary>The Z component of the block index of the running managed thread.</summary>
    public const string BlockIdxZName = "BlockIdxZ";

    // global::Kassyi.GSharp.Core.ManagedGpuContext, parsed once from the constant
    private static readonly NameSyntax s_contextName = SyntaxFactory.ParseName(Context);

    // global::Kassyi.GSharp.Core.ManagedGpuContext.<memberName>
    public static MemberAccessExpressionSyntax CreateMemberAccess(string memberName) =>
        CreateMemberAccess(SyntaxFactory.IdentifierName(memberName));

    // global::Kassyi.GSharp.Core.ManagedGpuContext.<member>
    public static MemberAccessExpressionSyntax CreateMemberAccess(SimpleNameSyntax member) =>
        SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, s_contextName, member);
}

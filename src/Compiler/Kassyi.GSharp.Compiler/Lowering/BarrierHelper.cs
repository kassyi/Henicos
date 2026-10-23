namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Provides helper methods to detect phase barrier and warp shuffle invocations in Roslyn syntax trees.</summary>
internal static class BarrierHelper
{
    /// <summary>
    /// Determines whether the specified invocation represents a thread barrier, warp-level synchronization, or a call to a
    /// device function that reaches one (marked by <see cref="LoweringAnnotations.SuspendingCallAnnotationKind"/>).
    /// </summary>
    public static bool IsPhaseBarrier(InvocationExpressionSyntax inv)
    {
        if (IsSuspendingCall(inv))
        {
            return true;
        }

        var name = "";
        var className = "";
        switch (inv.Expression)
        {
            case MemberAccessExpressionSyntax ma:
                name = ma.Name.Identifier.ValueText;
                className = (ma.Expression as IdentifierNameSyntax)?.Identifier.ValueText ?? "";
                break;
            case IdentifierNameSyntax id:
                name = id.Identifier.ValueText;
                break;
        }

        if ((name == "SyncThreads" || name == TypeConstants.GpuMacros.SyncGrid) && className is TypeConstants.GpuType or "")
        {
            return true;
        }

        return (className == TypeConstants.WarpType && name.StartsWith("Shuffle")) ||
               (className == TypeConstants.GpuType && name.StartsWith(TypeConstants.WarpType));
    }

    /// <summary>Determines whether the specified invocation is a block barrier (<c>Gpu.SyncThreads()</c> or <c>Gpu.SyncGrid()</c>).</summary>
    public static bool IsBlockBarrier(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: TypeConstants.GpuType } } ma =>
            IsBlockBarrierName(ma.Name.Identifier.ValueText),
        IdentifierNameSyntax id => IsBlockBarrierName(id.Identifier.ValueText),
        _ => false
    };

    private static bool IsBlockBarrierName(string name) => name is "SyncThreads" or TypeConstants.GpuMacros.SyncGrid;

    /// <summary>Determines whether the specified invocation calls a device function that may suspend the calling thread.</summary>
    public static bool IsSuspendingCall(InvocationExpressionSyntax inv) =>
        inv.HasAnnotations(LoweringAnnotations.SuspendingCallAnnotationKind);

    /// <summary>Determines whether the specified invocation calls a device function that may suspend and returns a value.</summary>
    public static bool IsValueReturningSuspendingCall(InvocationExpressionSyntax inv)
    {
        foreach (var annotation in inv.GetAnnotations(LoweringAnnotations.SuspendingCallAnnotationKind))
        {
            if (annotation.Data == LoweringAnnotations.ValueReturningCallAnnotationData)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines whether the specified invocation represents a warp shuffle operation.</summary>
    public static bool IsWarpShuffle(InvocationExpressionSyntax inv)
    {
        var name = "";
        var className = "";
        switch (inv.Expression)
        {
            case MemberAccessExpressionSyntax ma:
                name = ma.Name.Identifier.ValueText;
                className = (ma.Expression as IdentifierNameSyntax)?.Identifier.ValueText ?? "";
                break;
            case IdentifierNameSyntax id:
                name = id.Identifier.ValueText;
                break;
        }

        return (className == TypeConstants.WarpType && name.StartsWith("Shuffle")) ||
               (className == TypeConstants.GpuType && name.StartsWith(TypeConstants.WarpType));
    }

    /// <summary>Extracts the phase barrier invocation expression from a statement if present.</summary>
    public static InvocationExpressionSyntax? GetPhaseBarrierInvocation(StatementSyntax stmt)
    {
        switch (stmt)
        {
            case ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax { Right: InvocationExpressionSyntax rightInv }
            } when IsPhaseBarrier(rightInv):
                return rightInv;
            case ExpressionStatementSyntax { Expression: InvocationExpressionSyntax inv } when IsPhaseBarrier(inv):
                return inv;
            case LocalDeclarationStatementSyntax localDecl:
                {
                    var initializer = localDecl.Declaration.Variables.FirstOrDefault()?.Initializer;
                    if (initializer?.Value is InvocationExpressionSyntax inv && IsPhaseBarrier(inv))
                    {
                        return inv;
                    }

                    break;
                }
        }

        return null;
    }
}


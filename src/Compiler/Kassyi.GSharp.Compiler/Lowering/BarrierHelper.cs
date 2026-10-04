namespace Kassyi.GSharp.Compiler.Lowering;

internal static class BarrierHelper
{
    public static bool IsPhaseBarrier(InvocationExpressionSyntax inv)
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

        if (name == "SyncThreads" && className is TypeConstants.GpuType or "")
        {
            return true;
        }

        return (className == TypeConstants.WarpType && name.StartsWith("Shuffle")) || 
               (className == TypeConstants.GpuType && name.StartsWith(TypeConstants.WarpType));
    }

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

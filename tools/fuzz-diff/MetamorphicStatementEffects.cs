using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Read and write sets of kernel statements, and whether two adjacent statements may be swapped.
/// </summary>
internal static class MetamorphicStatementEffects
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="first"/> and <paramref name="second"/> contain no barrier
    /// or warp call, have describable effects, and neither writes a local or parameter the other reads or writes.
    /// </summary>
    internal static bool AreIndependent(
        StatementSyntax first,
        StatementSyntax second,
        EffectScope scope,
        CompilationUnitSyntax root)
    {
        if (MetamorphicBarrierDetector.ContainsBarrierOrWarpCall(first, scope.Model, root) ||
            MetamorphicBarrierDetector.ContainsBarrierOrWarpCall(second, scope.Model, root))
        {
            return false;
        }

        if (!TryGetEffects(first, scope, out var r1, out var w1) ||
            !TryGetEffects(second, scope, out var r2, out var w2))
        {
            return false;
        }

        return !w1.Overlaps(r2) &&
               !w1.Overlaps(w2) &&
               !w2.Overlaps(r1) &&
               !w2.Overlaps(w1);
    }

    /// <summary>
    /// Collects the locals and parameters <paramref name="stmt"/> reads and writes. Returns <see langword="false"/>
    /// for the prologue, the checksum write, statements other than declarations and expression statements, and
    /// effects the sets cannot describe (see <see cref="ExpressionEffectCollector"/>).
    /// </summary>
    internal static bool TryGetEffects(
        StatementSyntax stmt,
        EffectScope scope,
        out HashSet<ISymbol> reads,
        out HashSet<ISymbol> writes)
    {
        var collector = new ExpressionEffectCollector(scope);
        reads = collector.Reads;
        writes = collector.Writes;

        if (FuzzSyntaxHelpers.IsGidDeclaration(stmt) ||
            FuzzSyntaxHelpers.IsGidBoundsGuard(stmt) ||
            FuzzSyntaxHelpers.IsHashInitDeclaration(stmt) ||
            FuzzSyntaxHelpers.IsFinalChecksumWrite(stmt))
        {
            return false;
        }

        return stmt switch
        {
            LocalDeclarationStatementSyntax localDecl => TryCollectDeclaration(localDecl, scope, collector),
            ExpressionStatementSyntax exprStmt => TryCollectExpression(exprStmt.Expression, collector),
            _ => false,
        };
    }

    private static bool TryCollectDeclaration(
        LocalDeclarationStatementSyntax localDecl,
        EffectScope scope,
        ExpressionEffectCollector collector)
    {
        if (localDecl.Declaration.Type is RefTypeSyntax ||
            FuzzSyntaxHelpers.IsStackAllocSpanDeclaration(localDecl))
        {
            return false;
        }

        foreach (var v in localDecl.Declaration.Variables)
        {
            if (scope.Model.GetDeclaredSymbol(v) is not ILocalSymbol { IsRef: false } localSymbol ||
                localSymbol.Type.TypeKind == TypeKind.Error)
            {
                return false;
            }

            collector.Writes.Add(localSymbol);
            if (v.Initializer is not null && !TryCollectExpression(v.Initializer.Value, collector))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryCollectExpression(ExpressionSyntax expr, ExpressionEffectCollector collector)
    {
        collector.Visit(expr);
        return !collector.IsRejected;
    }
}

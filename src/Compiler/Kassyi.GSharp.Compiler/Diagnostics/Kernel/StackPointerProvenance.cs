using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Resolves the provenance of pointer locals to detect those pointing to stack memory (<c>stackalloc</c>, address-of,
/// or derived pointer arithmetic) for cross-barrier validation (GSH0041, #1004 F11).
/// </summary>
internal static class StackPointerProvenance
{
    /// <summary>
    /// Collects all pointer locals that may point to stack memory into <see cref="KernelBarrierScan.TrackedLocals"/>.
    /// </summary>
    public static void CollectStackPointerLocals(KernelBarrierScan scan)
    {
        if (scan.PointerLocals.Count == 0)
        {
            return;
        }

        var pointerAssignments = CollectPointerAssignments(scan);
        var stackPointers = PropagateStackPointers(pointerAssignments, scan.SemanticModel);
        AddTrackedStackPointers(scan, stackPointers);
    }

    /// <summary>Collects all initializers and simple assignments of pointer locals in the method.</summary>
    private static ImmutableArray<(ILocalSymbol Target, ExpressionSyntax Value)> CollectPointerAssignments(KernelBarrierScan scan)
    {
        var assignments = ImmutableArray.CreateBuilder<(ILocalSymbol, ExpressionSyntax)>();
        foreach (var (declarator, symbol) in scan.PointerLocals)
        {
            if (declarator.Initializer is { } initializer)
            {
                assignments.Add((symbol, initializer.Value));
            }
        }

        foreach (var node in scan.Method.DescendantNodes())
        {
            if (node is AssignmentExpressionSyntax assignment &&
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                scan.SemanticModel.GetSymbol(assignment.Left) is ILocalSymbol { Type: IPointerTypeSymbol } target)
            {
                assignments.Add((target, assignment.Right));
            }
        }

        return assignments.ToImmutable();
    }

    /// <summary>Propagates stack-pointer provenance through assignments until reaching a fixed point.</summary>
    private static HashSet<ILocalSymbol> PropagateStackPointers(
        ImmutableArray<(ILocalSymbol Target, ExpressionSyntax Value)> assignments,
        SemanticModel model)
    {
        var stackPointers = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        bool added;
        do
        {
            added = false;
            foreach (var (target, value) in assignments)
            {
                if (!stackPointers.Contains(target) && IsStackPointerSource(value, model, stackPointers))
                {
                    stackPointers.Add(target);
                    added = true;
                }
            }
        } while (added);

        return stackPointers;
    }

    /// <summary>Adds the discovered stack pointer locals to the barrier scan's tracked locals.</summary>
    private static void AddTrackedStackPointers(KernelBarrierScan scan, HashSet<ILocalSymbol> stackPointers)
    {
        foreach (var (declarator, symbol) in scan.PointerLocals)
        {
            if (stackPointers.Contains(symbol))
            {
                scan.TrackedLocals.Add(
                    new TrackedLocalInfo(
                        declarator,
                        symbol,
                        isRefLikeOrRef: false,
                        isLocalPointer: true,
                        isIteratorLocal: false
                    )
                );
            }
        }
    }

    /// <summary>
    /// Whether expression evaluates to a stack pointer: directly via stackalloc or &amp;, or derived from a local in
    /// stackPointers through pointer arithmetic, increments, chained assignments, switch expressions, casts, or coalescing.
    /// </summary>
    private static bool IsStackPointerSource(
        ExpressionSyntax expression,
        SemanticModel model,
        HashSet<ILocalSymbol> stackPointers) =>
        expression switch
        {
            StackAllocArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax => true,
            PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.AddressOfExpression) => true,
            PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PreIncrementExpression, SyntaxKind.PreDecrementExpression) =>
                IsStackPointerSource(prefix.Operand, model, stackPointers),
            PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.PostIncrementExpression, SyntaxKind.PostDecrementExpression, SyntaxKind.SuppressNullableWarningExpression) =>
                IsStackPointerSource(postfix.Operand, model, stackPointers),
            AssignmentExpressionSyntax assignment =>
                IsStackPointerSource(assignment.Right, model, stackPointers),
            SwitchExpressionSyntax switchExpr =>
                switchExpr.Arms
                    .AsStackEnumerable()
                    .Any((model, stackPointers), static (arm, state) =>
                        IsStackPointerSource(arm.Expression, state.model, state.stackPointers)),
            IdentifierNameSyntax identifier =>
                model.GetSymbol(identifier) is ILocalSymbol local && stackPointers.Contains(local),
            ParenthesizedExpressionSyntax parenthesized =>
                IsStackPointerSource(parenthesized.Expression, model, stackPointers),
            CastExpressionSyntax cast =>
                IsStackPointerSource(cast.Expression, model, stackPointers),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression, SyntaxKind.SubtractExpression, SyntaxKind.CoalesceExpression) =>
                IsStackPointerSource(binary.Left, model, stackPointers) ||
                IsStackPointerSource(binary.Right, model, stackPointers),
            ConditionalExpressionSyntax conditional =>
                IsStackPointerSource(conditional.WhenTrue, model, stackPointers) ||
                IsStackPointerSource(conditional.WhenFalse, model, stackPointers),
            _ => false
        };
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Resolves the provenance of pointer locals to detect those pointing to stack memory (<c>stackalloc</c>, address-of,
/// or derived pointer arithmetic) for cross-barrier validation (GSH0041, #1004).
/// </summary>
internal static class StackPointerProvenance
{
    /// <summary>
    /// Collects all pointer locals that may point to stack memory into <see cref="KernelBarrierScan.TrackedLocals"/>.
    /// </summary>
    public static void CollectStackPointerLocals(KernelBarrierScan scan) =>
        CollectStackPointerLocals(scan.Method, scan.SemanticModel, scan.PointerLocals, scan.TrackedLocals);

    /// <summary>
    /// Collects all pointer locals that may point to stack memory into <paramref name="trackedLocals"/>.
    /// </summary>
    public static void CollectStackPointerLocals(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        IReadOnlyList<(VariableDeclaratorSyntax Declarator, ILocalSymbol Symbol)> pointerLocals,
        ImmutableArray<TrackedLocalInfo>.Builder trackedLocals)
    {
        if (pointerLocals.Count == 0)
        {
            return;
        }

        var pointerAssignments = CollectPointerAssignments(method, semanticModel, pointerLocals);
        var stackPointers = PropagateStackPointers(pointerAssignments, semanticModel);
        AddTrackedStackPointers(pointerLocals, trackedLocals, stackPointers);
    }

    /// <summary>Collects all initializers and simple assignments of pointer locals in the method.</summary>
    private static ImmutableArray<(ILocalSymbol Target, ExpressionSyntax Value)> CollectPointerAssignments(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        IReadOnlyList<(VariableDeclaratorSyntax Declarator, ILocalSymbol Symbol)> pointerLocals)
    {
        var assignments = ImmutableArray.CreateBuilder<(ILocalSymbol, ExpressionSyntax)>();
        foreach (var (declarator, symbol) in pointerLocals)
        {
            if (declarator.Initializer is { } initializer)
            {
                assignments.Add((symbol, initializer.Value));
            }
        }

        foreach (var node in method.DescendantNodes())
        {
            if (node is AssignmentExpressionSyntax assignment &&
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                semanticModel.GetSymbol(assignment.Left) is ILocalSymbol { Type: IPointerTypeSymbol } target)
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

    /// <summary>Adds the discovered stack pointer locals to the tracked locals.</summary>
    private static void AddTrackedStackPointers(
        IReadOnlyList<(VariableDeclaratorSyntax Declarator, ILocalSymbol Symbol)> pointerLocals,
        ImmutableArray<TrackedLocalInfo>.Builder trackedLocals,
        HashSet<ILocalSymbol> stackPointers)
    {
        foreach (var (declarator, symbol) in pointerLocals)
        {
            if (stackPointers.Contains(symbol))
            {
                trackedLocals.Add(
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

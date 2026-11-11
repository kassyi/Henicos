using static Kassyi.GSharp.Compiler.OperatorMacroContext;
using static Kassyi.GSharp.Compiler.OperatorMacroResolver;

namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Whether the target and the context variables of a resolved operator macro assignment
/// (<see cref="OperatorMacroResolver.TryResolve"/>) are assigned before the assignment, as the expansion needs them to
/// be.
/// </summary>
internal static class OperatorMacroAssignment
{
    /// <summary>
    /// Why the target of <paramref name="assignment"/> (the user's tree, an operator macro <paramref name="macro"/>
    /// with a target) cannot receive the result, or <see langword="null"/> when it can: a local or <c>out</c>
    /// parameter, or a field of a struct one, that is not definitely assigned before the assignment, when the target
    /// method takes the result as a value, <c>ref</c> or <c>in</c> parameter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// [WHY] The expansion passes the target to the target method, which writes the result into the storage the
    /// target holds; it does not assign the target. An unassigned target has no storage: the C++ code reads an
    /// uninitialized value, and the managed expansion does not compile (CS0165, CS0170, CS0269). A target method that
    /// takes the result as <c>out</c> assigns it, so any target can receive it.
    /// </para>
    /// <para>
    /// Only what is certain is reported (semantics charter §3): a variable captured from outside the enclosing lambda
    /// or local function is assigned wherever the caller assigned it, which this body's data flow does not see.
    /// </para>
    /// </remarks>
    public static string? GetUnassignedTargetFailure(AssignmentExpressionSyntax assignment, OperatorMacro macro,
        SemanticModel semanticModel)
    {
        if (macro.TargetMethod is not { } target ||
            target.Parameters[macro.HasContext ? 1 : 0].RefKind == RefKind.Out ||
            assignment.Parent is not ExpressionStatementSyntax statement ||
            GetTargetVariable(assignment.Left, semanticModel) is not { } symbol ||
            !IsUnassignedBefore(statement, symbol, semanticModel))
        {
            return null;
        }

        return OperatorMacroMessages.UnassignedTarget(assignment.Left, macro.TargetName, symbol.Name);
    }

    /// <summary>
    /// Why the context variable of <paramref name="assignment"/> (the user's tree), or of an operator macro nested in
    /// it, cannot be passed to its target, or <see langword="null"/> when it can: a local or <c>out</c> parameter that
    /// is not definitely assigned before the assignment.
    /// </summary>
    /// <remarks>
    /// [WHY] The expansion passes the variable itself (by reference, or as a copy of its value), which the target may
    /// read: the C++ code reads an uninitialized value, and the managed expansion does not compile (CS0165). Only what
    /// is certain is reported, as for the target (<see cref="GetUnassignedTargetFailure"/>).
    /// </remarks>
    public static string? GetUnassignedContextFailure(AssignmentExpressionSyntax assignment, OperatorMacro macro,
        SemanticModel semanticModel)
    {
        if (assignment.Parent is not ExpressionStatementSyntax statement)
        {
            return null;
        }

        if (macro is { HasContext: true, TargetMethod: { } target } &&
            TryFindContextVariable(assignment, target, semanticModel, out var variable, out _) &&
            variable is ILocalSymbol { IsRef: false } or IParameterSymbol { RefKind: RefKind.Out } &&
            IsUnassignedBefore(statement, variable, semanticModel))
        {
            return OperatorMacroMessages.UnassignedContext(variable.Name, macro.TargetName);
        }

        if (macro.Nested.IsDefaultOrEmpty)
        {
            return null;
        }

        foreach (var nested in macro.Nested)
        {
            if (GetUnassignedContextFailure(assignment, nested.Inner, semanticModel) is { } failure)
            {
                return failure;
            }
        }

        return null;
    }

    // Whether <symbol> (a local or out parameter of the method that holds <statement>) is certainly not assigned when
    // <statement> starts. A local declared with a value, and a variable captured from outside the enclosing lambda or
    // local function, are not
    private static bool IsUnassignedBefore(ExpressionStatementSyntax statement, ISymbol symbol,
        SemanticModel semanticModel)
    {
        if (IsAssignedWhereDeclared(symbol) ||
            !symbol.ContainingSymbol.IsEqualTo(semanticModel.GetEnclosingSymbol(statement.SpanStart)))
        {
            return false;
        }

        var flow = semanticModel.AnalyzeDataFlow(statement);
        return flow is { Succeeded: true } &&
               !flow.DefinitelyAssignedOnEntry
                   .AsStackEnumerable()
                   .Any(symbol, static (assigned, s) => assigned.IsEqualTo(s));
    }

    // The local or out parameter whose storage <target> is: the variable itself, or the variable that holds the struct
    // whose field it is (h.Inner.Value); null for anything else (an array element, a field of a class, a ref local)
    private static ISymbol? GetTargetVariable(ExpressionSyntax target, SemanticModel semanticModel)
    {
        while (true)
        {
            switch (semanticModel.GetSymbol(target))
            {
                case ILocalSymbol { IsRef: false } local:
                    return local;
                case IParameterSymbol { RefKind: RefKind.Out } parameter:
                    return parameter;
                case IFieldSymbol { IsStatic: false, ContainingType.IsValueType: true }
                    when target is MemberAccessExpressionSyntax { Expression: var receiver }:
                    target = receiver;
                    continue;
                default:
                    return null;
            }
        }
    }

    // A local declared with an initializer, or by a foreach statement, is assigned wherever it is in scope; the data
    // flow need not run for it
    private static bool IsAssignedWhereDeclared(ISymbol symbol) =>
        symbol is ILocalSymbol { DeclaringSyntaxReferences: [var reference] } &&
        reference.GetSyntax() is VariableDeclaratorSyntax { Initializer: not null } or ForEachStatementSyntax;
}

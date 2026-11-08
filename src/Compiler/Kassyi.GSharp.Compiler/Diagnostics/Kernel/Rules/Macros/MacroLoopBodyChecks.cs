namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.Macros;

/// <summary>
/// Checks the lambda body of the loop macros (<c>Gpu.ForUnroll</c>, <c>Gpu.GridStrideLoop</c>): it takes the loop
/// variable, it does not return, and it neither writes the loop variable nor takes its address.
/// </summary>
internal static class MacroLoopBodyChecks
{
    /// <summary>
    /// Reports a loop body without a loop variable, a return in it, and a write or reference to the variable.
    /// </summary>
    public static void CheckLoopBody(
        InvocationExpressionSyntax invocation,
        GpuMacroCall call,
        string macroName,
        ref ValidationContext context)
    {
        if (call.GetBody(invocation.ArgumentList.Arguments) is not { } body)
        {
            return;
        }

        if (GpuCodeConstraints.GetMacroLoopParameter(body, call.HasContext) is not { } loopParameter)
        {
            context.ReportDiagnostic(GSharpDiagnostics.MacroLambdaRequiresParameter, invocation, macroName);
            return;
        }

        CheckLoopBodyReturn(body, macroName, ref context);
        CheckLoopVariableUse(body, loopParameter, macroName, ref context);
    }

    // [WHY] In C# a return leaves the lambda, which is the end of one iteration. The transpiler writes the body into
    // the surrounding code, where it would end the kernel, so the two would silently differ
    private static void CheckLoopBodyReturn(
        LambdaExpressionSyntax body,
        string macroName,
        ref ValidationContext context)
    {
        // A return in a lambda or local function of the body leaves that function, not the loop body
        var statements = body.Body.DescendantNodesAndSelf(
            static node => node is not (
                LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax));
        foreach (var node in statements)
        {
            if (node is ReturnStatementSyntax)
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.UnsupportedSyntax,
                    node,
                    $"return in a Gpu.{macroName} body");
            }
        }
    }

    // [WHY] In C# the loop variable is a parameter, so assigning it or taking its address is legal. The transpiler
    // replaces it by the iteration index, which is a value and has no address
    private static void CheckLoopVariableUse(
        LambdaExpressionSyntax body,
        ParameterSyntax loopParameter,
        string macroName,
        ref ValidationContext context)
    {
        var semanticModel = context.SemanticModel;
        var loopSymbol = semanticModel.GetDeclaredSymbol<IParameterSymbol>(loopParameter)?.OriginalDefinition;
        if (loopSymbol == null)
        {
            return;
        }

        var name = loopParameter.Identifier.ValueText;
        foreach (var node in body.Body.DescendantNodesAndSelf())
        {
            var isWrite = IsWriteOfLoopVariable(node, semanticModel, loopSymbol);
            var isReference = IsReferenceToLoopVariable(node, semanticModel, loopSymbol);
            if (isWrite || isReference)
            {
                var kind = isWrite ? "assignment to" : "reference to";
                context.ReportDiagnostic(
                    GSharpDiagnostics.UnsupportedSyntax,
                    node,
                    $"{kind} the Gpu.{macroName} loop variable '{name}'");
            }
        }
    }

    // An assignment, an increment or decrement, or a ref / out argument of the loop variable
    private static bool IsWriteOfLoopVariable(SyntaxNode node, SemanticModel semanticModel, ISymbol loopSymbol) =>
        node switch
        {
            AssignmentExpressionSyntax assignment =>
                IsLoopVariable(assignment.Left, semanticModel, loopSymbol),
            PrefixUnaryExpressionSyntax unary
                when unary.IsKind(SyntaxKind.PreIncrementExpression, SyntaxKind.PreDecrementExpression) =>
                IsLoopVariable(unary.Operand, semanticModel, loopSymbol),
            PostfixUnaryExpressionSyntax unary
                when unary.IsKind(SyntaxKind.PostIncrementExpression, SyntaxKind.PostDecrementExpression) =>
                IsLoopVariable(unary.Operand, semanticModel, loopSymbol),
            ArgumentSyntax argument
                when argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword, SyntaxKind.OutKeyword) =>
                IsLoopVariable(argument.Expression, semanticModel, loopSymbol),
            _ => false
        };

    // The address of the loop variable, or the variable passed as an in argument
    private static bool IsReferenceToLoopVariable(SyntaxNode node, SemanticModel semanticModel, ISymbol loopSymbol) =>
        node switch
        {
            PrefixUnaryExpressionSyntax unary when unary.IsKind(SyntaxKind.AddressOfExpression) =>
                IsLoopVariable(unary.Operand, semanticModel, loopSymbol),
            ArgumentSyntax argument when argument.RefKindKeyword.IsKind(SyntaxKind.InKeyword) =>
                IsLoopVariable(argument.Expression, semanticModel, loopSymbol),
            _ => false
        };

    // The loop variable, possibly in parentheses or among the targets of a deconstruction
    private static bool IsLoopVariable(ExpressionSyntax expression, SemanticModel semanticModel, ISymbol loopSymbol) =>
        expression switch
        {
            IdentifierNameSyntax name =>
                semanticModel.GetSymbol(name)?.OriginalDefinition.IsEqualTo(loopSymbol) == true,
            ParenthesizedExpressionSyntax parenthesized =>
                IsLoopVariable(parenthesized.Expression, semanticModel, loopSymbol),
            TupleExpressionSyntax tuple => tuple.Arguments
                .AsStackEnumerable()
                .Any(
                    (semanticModel, loopSymbol),
                    static (argument, state) =>
                        IsLoopVariable(argument.Expression, state.semanticModel, state.loopSymbol)
                ),
            _ => false
        };
}

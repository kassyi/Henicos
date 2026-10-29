using Microsoft.CodeAnalysis.Operations;
using Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;
using static Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules.GpuOperatorMacroResolver;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Expands an operator macro assignment (<c>c = a + b;</c> with a <c>[GpuOperatorMacro]</c> operator) into the C# call
/// of its target for the managed path, as <see cref="GpuOperatorMacroTranspiler"/> does in C++ (#711).
/// </summary>
/// <remarks>
/// <para>
/// The call is the one the resolver chose (<see cref="GpuOperatorMacroResolver"/>, #478), whatever C# would pick for
/// the arguments as written: a value argument whose type is not the parameter's is cast to it, which applies the
/// implicit conversion C# would apply and leaves the chosen overload the only one whose parameters all match exactly.
/// The context is the variable the resolver found (#477), spelled as the body being rewritten spells it: a local that a
/// state machine keeps in its context is a field there.
/// </para>
/// <para>
/// A C# call evaluates its arguments from left to right, which is the order of the operator (result, then operands).
/// An inner operator macro is computed into a temporary before the outer call, so the operands before it that
/// interfere with it are evaluated into temporaries first, in the C# order, as the C++ emitter does
/// (<see cref="EvaluationOrder.LastSpilledOperand"/>).
/// </para>
/// </remarks>
internal static class ManagedOperatorMacroExpander
{
    /// <summary>
    /// The statement that <paramref name="assignment"/> (an operator macro of
    /// <see cref="ManagedRewriteClassifier.TryGetOperatorMacro"/>) becomes, or <see langword="null"/> when its context
    /// variable cannot be found (GSH0003), and the statement stays as written.
    /// </summary>
    /// <param name="assignment">The assignment, a node of the tree that <paramref name="visitor"/> rewrites.</param>
    /// <param name="macro">The operator macro on its right side.</param>
    /// <param name="semanticModel">The model of the user's tree.</param>
    /// <param name="visitor">The rewriter of the body, which rewrites the operands and the context reference.</param>
    /// <param name="spellCallee">Writes the callee of a call of a device function on the managed path.</param>
    public static StatementSyntax? Expand(AssignmentExpressionSyntax assignment, OperatorMacro macro,
        SemanticModel semanticModel, CSharpSyntaxVisitor<SyntaxNode?> visitor,
        Func<IMethodSymbol, ExpressionSyntax> spellCallee)
    {
        var expansion = new Expansion(assignment, semanticModel, visitor, spellCallee);
        if (expansion.CreateCall(assignment.Left, null, macro) is not { } call)
        {
            return null;
        }

        var statement = SyntaxFactory.ExpressionStatement(call);
        if (expansion.Statements.Count == 0)
        {
            return statement;
        }

        // { <temporaries and inner calls> <call>; }
        expansion.Statements.Add(statement);
        return SyntaxFactory.Block(expansion.Statements.ToImmutable());
    }

    private sealed class Expansion(AssignmentExpressionSyntax assignment, SemanticModel semanticModel,
        CSharpSyntaxVisitor<SyntaxNode?> visitor, Func<IMethodSymbol, ExpressionSyntax> spellCallee)
    {
        private int _nextTemp;

        public ImmutableArray<StatementSyntax>.Builder Statements { get; } = ImmutableArray.CreateBuilder<StatementSyntax>();

        // <target>(<context>, <result>, <operands>), after the statements that compute its inner operator macros and
        // the operands before them; the result is the temporary <resultTemp> when <result> is null
        public InvocationExpressionSyntax? CreateCall(ExpressionSyntax? result, string? resultTemp, OperatorMacro macro)
        {
            if (!TryPrepareCall(result, macro, assignment, semanticModel, out var call, out _))
            {
                return null;
            }

            // Only an inner operator macro moves a computation in front of the call
            var last = -1;
            ExpressionEffects[]? effects = null;
            if (!macro.Nested.IsDefaultOrEmpty)
            {
                effects = GetOperandEffects(call, macro, assignment, semanticModel);
                last = EvaluationOrder.LastSpilledOperand(effects);
            }

            var arguments = ImmutableArray.CreateBuilder<ArgumentSyntax>(call.Operands.Length + 1);
            if (call.ContextVariable is { } contextVariable)
            {
                arguments.Add(CreateContextArgument(contextVariable, call.Target.Parameters[0]));
            }

            for (var i = 0; i < call.Operands.Length; i++)
            {
                if (call.Parameters[i] is not { } parameter)
                {
                    continue;
                }

                if (call.Operands[i] is not { } operand)
                {
                    // The temporary of the inner operator macro that this call computes, passed to its ref or out result
                    arguments.Add(CreateArgument(Temp(resultTemp), parameter, macro.ResultType, isVariable: true));
                    continue;
                }

                if (macro.TryGetNested(operand, out var inner) && inner.ResultType is { } innerType)
                {
                    var temp = NextTemp();
                    // <inner result type> <temp> = default;
                    Statements.Add(DeclareLocal(innerType, temp, SyntaxFactory.LiteralExpression(
                        SyntaxKind.DefaultLiteralExpression)));
                    if (CreateCall(null, temp, inner) is not { } innerCall)
                    {
                        return null;
                    }

                    Statements.Add(SyntaxFactory.ExpressionStatement(innerCall));
                    arguments.Add(CreateArgument(Temp(temp), parameter, innerType, isVariable: true));
                    continue;
                }

                var visited = (visitor.Visit(operand) as ExpressionSyntax ?? operand).WithoutTrivia();
                var type = semanticModel.GetTypeSymbol(operand);
                if (i <= last && effects is not null && !effects[i].IsEmpty)
                {
                    arguments.Add(Spill(visited, type, parameter));
                    continue;
                }

                arguments.Add(CreateArgument(visited, parameter, type, IsVariable(operand)));
            }

            return SyntaxFactory.InvocationExpression(
                spellCallee(call.Target),
                SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments.ToImmutable()))
            );
        }

        // The operand evaluated into a temporary now, in the C# order:
        // ref <type> <temp> = ref <operand>;    for a ref or out result, which the call writes through
        // <type> <temp> = <operand>;             for a value (an 'in' parameter takes the value too, as the operator did)
        private ArgumentSyntax Spill(ExpressionSyntax operand, ITypeSymbol? type, IParameterSymbol parameter)
        {
            var temp = NextTemp();
            if (parameter.RefKind is RefKind.Ref or RefKind.Out && type is not null)
            {
                Statements.Add(DeclareLocal(type, temp, SyntaxFactory.RefExpression(operand), isRef: true));
            }
            else
            {
                Statements.Add(DeclareLocal(type, temp, operand));
            }

            return CreateArgument(Temp(temp), parameter, type, isVariable: true);
        }

        // ref <argument> / out <argument> / in <argument>, or the value cast to the parameter type when its type is not
        // that type
        private static ArgumentSyntax CreateArgument(ExpressionSyntax expression, IParameterSymbol parameter,
            ITypeSymbol? type, bool isVariable)
        {
            switch (parameter.RefKind)
            {
                case RefKind.Ref:
                    return WithModifier(expression, SyntaxKind.RefKeyword);
                case RefKind.Out:
                    return WithModifier(expression, SyntaxKind.OutKeyword);
                case RefKind.In or RefKind.RefReadOnlyParameter when isVariable && type.IsEqualTo(parameter.Type):
                    return WithModifier(expression, SyntaxKind.InKeyword);
            }

            if (type is null || type.IsEqualTo(parameter.Type))
            {
                return SyntaxFactory.Argument(expression);
            }

            // (<parameter type>)<expression>, with the expression in parentheses unless it is a primary expression
            return SyntaxFactory.Argument(
                SyntaxFactory.CastExpression(
                    ParseType(parameter.Type),
                    IsPrimary(expression) ? expression : SyntaxFactory.ParenthesizedExpression(expression)
                )
            );
        }

        private static bool IsPrimary(ExpressionSyntax expression) =>
            expression is SimpleNameSyntax or QualifiedNameSyntax or AliasQualifiedNameSyntax
                or MemberAccessExpressionSyntax or ElementAccessExpressionSyntax or InvocationExpressionSyntax
                or ParenthesizedExpressionSyntax or ThisExpressionSyntax ||
            expression.IsKind(SyntaxKind.NumericLiteralExpression);

        // The context variable, as the body spells it: a local that a state machine keeps in its context is read through
        // the context there (GpuOperatorMacroResolver.FindLoweredReference finds a reference of the lowered body);
        // otherwise, in the user's own tree, its name
        private ArgumentSyntax CreateContextArgument(ISymbol variable, IParameterSymbol parameter)
        {
            ExpressionSyntax reference = FindLoweredReference(assignment, variable, semanticModel) is { } lowered
                ? (visitor.Visit(lowered) as ExpressionSyntax ?? lowered).WithoutTrivia()
                : SyntaxFactory.IdentifierName(SyntaxFactory.Identifier(variable.Name.ToCSharpIdentifier()));
            return parameter.RefKind switch
            {
                RefKind.Ref => WithModifier(reference, SyntaxKind.RefKeyword),
                RefKind.In or RefKind.RefReadOnlyParameter => WithModifier(reference, SyntaxKind.InKeyword),
                _ => SyntaxFactory.Argument(reference)
            };
        }

        private bool IsVariable(ExpressionSyntax operand) =>
            semanticModel.GetSafeOperation(operand) is ILocalReferenceOperation or IParameterReferenceOperation
                or IFieldReferenceOperation or IArrayElementReferenceOperation;

        private string NextTemp() => GSharpInternalNames.ManagedVersion.MacroTempPrefix +
                                     _nextTemp++.ToString(CultureInfo.InvariantCulture);

        private static IdentifierNameSyntax Temp(string? name) =>
            SyntaxFactory.IdentifierName(name ?? throw new InvalidOperationException(
                "An operator macro without a result operand is not an inner operator macro."));

        private static ArgumentSyntax WithModifier(ExpressionSyntax expression, SyntaxKind modifier) =>
            SyntaxFactory.Argument(expression).WithRefKindKeyword(SyntaxFactory.Token(modifier));

        // [ref] <type> <name> = <value>;   (var when the type is not known)
        private static LocalDeclarationStatementSyntax DeclareLocal(ITypeSymbol? type, string name,
            ExpressionSyntax value, bool isRef = false)
        {
            TypeSyntax typeSyntax = type is null ? SyntaxFactory.IdentifierName("var") : ParseType(type);
            if (isRef)
            {
                typeSyntax = SyntaxFactory.RefType(typeSyntax);
            }

            var declarator = SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(name))
                .WithInitializer(SyntaxFactory.EqualsValueClause(value));
            return SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(typeSyntax, SyntaxFactory.SingletonSeparatedList(declarator))
            );
        }

        private static TypeSyntax ParseType(ITypeSymbol type)
        {
            using var writer = new SourceWriter();
            writer.AppendCsharpTypeName(type);
            return SyntaxFactory.ParseTypeName(writer.ToString());
        }
    }
}

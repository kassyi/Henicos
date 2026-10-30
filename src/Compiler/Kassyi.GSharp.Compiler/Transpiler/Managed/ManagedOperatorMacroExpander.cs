using Microsoft.CodeAnalysis.Operations;
using Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;
using static Kassyi.GSharp.Compiler.OperatorMacroContext;
using static Kassyi.GSharp.Compiler.OperatorMacroResolver;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Expands an operator macro assignment (<c>c = a + b;</c> with a <c>[GpuOperatorMacro]</c> operator) into the C# call
/// of its target for the managed path, as <see cref="GpuOperatorMacroTranspiler"/> does in C++ (#711).
/// </summary>
/// <remarks>
/// <para>
/// The call is the one the resolver chose (<see cref="OperatorMacroResolver"/>, #478), whatever C# would pick for
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
/// <para>
/// An operand passed by reference is passed as the storage it names, as the C++ emitter passes its address: a variable,
/// an element or a call that returns by reference, a field of one, and an auto-property, which the device stores as a
/// field (its backing field, through an <c>[UnsafeAccessor]</c> stub of <see cref="ManagedAccessorSet"/>). An operand
/// without storage (a field or an auto-property of a value) passes to an <c>in</c> parameter as a value, for which C#
/// makes a temporary.
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
    /// <param name="accessors">The stubs of the body, which reach the backing field of an auto-property.</param>
    public static StatementSyntax? Expand(AssignmentExpressionSyntax assignment, OperatorMacro macro,
        SemanticModel semanticModel, CSharpSyntaxVisitor<SyntaxNode?> visitor,
        Func<IMethodSymbol, ExpressionSyntax> spellCallee, ManagedAccessorSet accessors)
    {
        var expansion = new Expansion(assignment, semanticModel, visitor, spellCallee, accessors);
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
        CSharpSyntaxVisitor<SyntaxNode?> visitor, Func<IMethodSymbol, ExpressionSyntax> spellCallee,
        ManagedAccessorSet accessors)
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

                var spill = i <= last && effects is not null && !effects[i].IsEmpty;
                if (CreateOperand(call.Operands[i], parameter, macro, resultTemp, spill) is not { } argument)
                {
                    return null;
                }

                arguments.Add(argument);
            }

            return SyntaxFactory.InvocationExpression(
                spellCallee(call.Target),
                SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments.ToImmutable()))
            );
        }

        // The argument of one operand, as GpuOperatorMacroTranspiler.DeclareOperandTemps and AppendOperand write it:
        // the temporary <resultTemp> of the inner operator macro that this call computes (the operand is null), the
        // temporary of an inner operator macro computed first, the operand evaluated into a temporary now (<spill>), or
        // the operand in place
        private ArgumentSyntax? CreateOperand(ExpressionSyntax? operand, IParameterSymbol parameter, OperatorMacro macro,
            string? resultTemp, bool spill)
        {
            if (operand is null)
            {
                // The temporary of the inner operator macro that this call computes, passed to its ref or out result
                return CreateArgument(Temp(resultTemp), parameter, isVariable: true, type: null);
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
                return CreateArgument(Temp(temp), parameter, isVariable: true, type: null);
            }

            var type = semanticModel.GetTypeSymbol(operand);
            var reference = IsPassedByReference(parameter) && (parameter.RefKind is RefKind.Ref or RefKind.Out ||
                                                                 type.IsEqualTo(parameter.Type))
                ? TryCreateReference(operand)
                : null;
            var expression = reference ?? Visit(operand);
            return spill
                ? Spill(expression, type, parameter, isVariable: reference is not null)
                : CreateArgument(expression, parameter, isVariable: reference is not null, type);
        }

        // The operand evaluated into a temporary now, in the C# order, as the C++ emitter does (EvaluationOrder.DeclareTemp:
        // auto&& for a parameter passed by reference):
        // ref <type> <temp> = ref <operand>;            a ref or out result, which the call writes through
        // ref readonly <type> <temp> = ref <operand>;   storage passed to an 'in' or 'ref readonly' parameter: the call
        //                                               reads the storage itself, as in C++
        // <type> <temp> = <operand>;                    any other value
        private ArgumentSyntax Spill(ExpressionSyntax operand, ITypeSymbol? type, IParameterSymbol parameter,
            bool isVariable)
        {
            var temp = NextTemp();
            var reference = type is null ? RefKind.None : parameter.RefKind switch
            {
                RefKind.Ref or RefKind.Out => RefKind.Ref,
                RefKind.In or RefKind.RefReadOnlyParameter when isVariable => RefKind.RefReadOnly,
                _ => RefKind.None
            };
            Statements.Add(DeclareLocal(type, temp,
                reference == RefKind.None ? operand : SyntaxFactory.RefExpression(operand), reference));
            return CreateArgument(Temp(temp), parameter, isVariable: reference != RefKind.None, type);
        }

        // ref <argument> / out <argument>, in <argument> for storage, or the value cast to the parameter type when its type
        // is not that type
        private static ArgumentSyntax CreateArgument(ExpressionSyntax expression, IParameterSymbol parameter,
            bool isVariable, ITypeSymbol? type)
        {
            if (parameter.RefKind is RefKind.Ref or RefKind.Out || isVariable && IsPassedByReference(parameter))
            {
                return WithModifier(expression, parameter.RefKind);
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
        // the context there (OperatorMacroContext.FindLoweredReference finds a reference of the lowered body); otherwise,
        // in the user's own tree, its name
        private ArgumentSyntax CreateContextArgument(ISymbol variable, IParameterSymbol parameter)
        {
            ExpressionSyntax reference = FindLoweredReference(assignment, variable, semanticModel) is { } lowered
                ? Visit(lowered)
                : SyntaxFactory.IdentifierName(SyntaxFactory.Identifier(variable.Name.ToCSharpIdentifier()));
            return WithModifier(reference, parameter.RefKind);
        }

        // The storage that <operand> names, spelled for a by-reference argument, or null when it names none (a value):
        // a local, a parameter, an array element, a call or property that returns by reference, a field of a class or
        // of storage, and an auto-property of storage (its backing field, which the device uses for it)
        private ExpressionSyntax? TryCreateReference(ExpressionSyntax operand)
        {
            switch (semanticModel.GetSafeOperation(operand))
            {
                case ILocalReferenceOperation or IParameterReferenceOperation or IArrayElementReferenceOperation
                    or IInvocationOperation { TargetMethod.RefKind: not RefKind.None }
                    or IPropertyReferenceOperation { Property.RefKind: not RefKind.None }:
                case IFieldReferenceOperation { Instance: null or { Type.IsValueType: false } }:
                    return Visit(operand);
                case IFieldReferenceOperation { Field: var field }
                    when operand is MemberAccessExpressionSyntax { Expression: var receiver }:
                    // <receiver storage>.<field>
                    return TryCreateReference(receiver) is { } receiverReference
                        ? SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            receiverReference,
                            SyntaxFactory.IdentifierName(field.Name.ToCSharpIdentifier())
                        )
                        : null;
                case IPropertyReferenceOperation { Property: var property, Instance.Type.IsValueType: true }
                    when StructAbiMembers.IsAutoProperty(property) &&
                         operand is MemberAccessExpressionSyntax { Expression: var receiver }:
                    // <accessor>(in <receiver storage>)
                    return TryCreateReference(receiver) is { } structReference
                        ? accessors.SpellBackingField(property, structReference)
                        : null;
                default:
                    return null;
            }
        }

        private ExpressionSyntax Visit(ExpressionSyntax node) =>
            (visitor.Visit(node) as ExpressionSyntax ?? node).WithoutTrivia();

        private string NextTemp() => GSharpInternalNames.ManagedVersion.MacroTempPrefix +
                                     _nextTemp++.ToString(CultureInfo.InvariantCulture);

        private static IdentifierNameSyntax Temp(string? name) =>
            SyntaxFactory.IdentifierName(name ?? throw new InvalidOperationException(
                "An operator macro without a result operand is not an inner operator macro."));

        // <modifier> <expression>: ref, out or in by the parameter, nothing for a value parameter
        private static ArgumentSyntax WithModifier(ExpressionSyntax expression, RefKind refKind) =>
            ManagedRefKinds.ArgumentKeyword(refKind) is var keyword && keyword != SyntaxKind.None
                ? SyntaxFactory.Argument(expression).WithRefKindKeyword(SyntaxFactory.Token(keyword))
                : SyntaxFactory.Argument(expression);

        // [ref [readonly]] <type> <name> = <value>;   (var when the type is not known)
        private static LocalDeclarationStatementSyntax DeclareLocal(ITypeSymbol? type, string name,
            ExpressionSyntax value, RefKind reference = RefKind.None)
        {
            TypeSyntax typeSyntax = type is null ? SyntaxFactory.IdentifierName("var") : ParseType(type);
            typeSyntax = reference switch
            {
                // ref <type>
                RefKind.Ref => SyntaxFactory.RefType(typeSyntax),
                // ref readonly <type>
                RefKind.RefReadOnly => SyntaxFactory.RefType(
                    SyntaxFactory.Token(SyntaxKind.RefKeyword),
                    SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword),
                    typeSyntax
                ),
                _ => typeSyntax
            };

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

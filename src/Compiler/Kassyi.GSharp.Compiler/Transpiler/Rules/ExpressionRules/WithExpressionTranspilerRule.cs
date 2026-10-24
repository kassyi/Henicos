namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles <c>value with { X = a, Y = b }</c> on a struct into an immediately invoked C++ lambda that copies the value once,
/// assigns the members in the written order and returns the copy.
/// </summary>
/// <remarks>
/// <para>
/// [WHY] A lambda keeps the expression usable everywhere an expression is (<c>return</c>, an argument, a conditional
/// branch), as the object initializer does; a temporary declared before the statement could not follow a conditional
/// or short-circuit evaluation order. The operand is evaluated once, as the copy initializer, and the right-hand sides
/// still read the original value (C# 9 non-destructive mutation).
/// </para>
/// <para>
/// A field or auto-property is assigned directly on the copy, and a computed property with a <c>set</c> or <c>init</c>
/// body is assigned through the setter member function emitted by <c>StructTranspiler</c>
/// (<see cref="ComputedPropertyGetterCall"/>). A <c>with</c> on a class would allocate and is reported (GSH0003).
/// </para>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class WithExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<WithExpressionSyntax>
{
    /// <inheritdoc />
    public override void Transpile(WithExpressionSyntax node, ref TranspileContext context)
    {
        var type = context.Query.SemanticModel.GetTypeSymbol(node);

        // An unresolved type is the C# compiler's error
        if (type is null or IErrorTypeSymbol)
        {
            return;
        }

        if (!type.IsValueType)
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, GpuCodeConstraints.WithOnReferenceType);
            context.Visitor.AppendUnsupportedPlaceholder(node);
            return;
        }

        var supported = true;
        foreach (var expression in node.Initializer.Expressions)
        {
            if (expression is AssignmentExpressionSyntax { Left: { } left } &&
                GpuCodeConstraints.GetWithMemberProblem(context.Query.SemanticModel.GetSymbol(left)) is { } problem)
            {
                context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, left, problem);
                supported = false;
            }
        }

        if (!supported)
        {
            context.Visitor.AppendUnsupportedPlaceholder(node);
            return;
        }

        // ([&]() { <type> __gsharp_tmp = <value>; __gsharp_tmp.<member> = <rhs>; ... return __gsharp_tmp; }())
        using (context.Emitter.Writer.ImmediatelyInvokedLambda())
        {
            context.Emitter.Dialect.AppendType(ref context, type);
            context.Emitter.Writer.Append($" {GSharpInternalNames.Lowering.AggregateTemp} = ");
            context.Visitor.Visit(node.Expression);
            context.Emitter.Writer.AppendLine(";");

            foreach (var expression in node.Initializer.Expressions)
            {
                if (expression is not AssignmentExpressionSyntax assignment)
                {
                    continue;
                }

                context.Emitter.Writer.Append($"{GSharpInternalNames.Lowering.AggregateTemp}.");
                ComputedPropertyGetterCall.TranspileAssignment(assignment, " = ", ref context);
                context.Emitter.Writer.AppendLine(";");
            }

            context.Emitter.Writer.AppendLine($"return {GSharpInternalNames.Lowering.AggregateTemp};");
        }
    }
}

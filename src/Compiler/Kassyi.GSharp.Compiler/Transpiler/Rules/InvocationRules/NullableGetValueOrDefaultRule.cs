namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;

/// <summary>
/// Writes <c>Nullable&lt;T&gt;.GetValueOrDefault()</c> and <c>GetValueOrDefault(T)</c>, which the
/// <c>System_Nullable&lt;T&gt;</c> of the Prelude does not declare, from its <c>HasValue</c> and <c>Value</c> fields
/// (#1143): <c>(opt.HasValue?opt.Value:int{})</c>, <c>(opt.HasValue?opt.Value:-1)</c>.
/// </summary>
/// <remarks>
/// C# evaluates the receiver (a variable is passed by reference), then the argument, and the method reads the fields
/// last. The receiver is written twice and the argument only when there is no value, so this short form is used only
/// when evaluating them writes nothing and cannot fail; otherwise both are evaluated into temporaries in that order:
/// <c>([&amp;]() { auto&amp;&amp; __gsharp_seq_0 = &lt;opt&gt;; int __gsharp_seq_1 = &lt;fallback&gt;;
/// return __gsharp_seq_0.HasValue?__gsharp_seq_0.Value:__gsharp_seq_1; }())</c>.
/// </remarks>
internal sealed class NullableGetValueOrDefaultRule : IInvocationTranspilerRule
{
    private const string MethodName = "GetValueOrDefault";

    // The receiver and the fallback argument (null without one), the type of the value, and whether they are evaluated
    // into temporaries first
    private readonly record struct Plan(
        ExpressionSyntax Receiver,
        ArgumentSyntax? Fallback,
        ITypeSymbol ValueType,
        bool IsSequenced
    );

    /// <inheritdoc />
    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        if (symbol is not
            {
                Name: MethodName,
                ContainingType:
                {
                    OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
                    TypeArguments: [var valueType]
                }
            } ||
            node.Expression is not MemberAccessExpressionSyntax { Expression: var receiver })
        {
            return false;
        }

        var arguments = node.ArgumentList.Arguments;
        var fallback = arguments.Count == 1 ? arguments[0] : null;
        var model = context.Query.SemanticModel;
        var isSequenced = !CanRepeatOrSkip(receiver, model) || (fallback != null &&
            (!CanRepeatOrSkip(fallback.Expression, model) ||
             ExpressionEffects.Of(receiver, model).InterferesWith(ExpressionEffects.Of(fallback.Expression, model))));
        Emit(new Plan(receiver, fallback, valueType, isSequenced), ref context);
        return true;
    }

    // An expression that may be evaluated twice, or not at all, without a difference: it writes nothing and cannot fail
    private static bool CanRepeatOrSkip(ExpressionSyntax expression, SemanticModel model) =>
        ExpressionEffects.Of(expression, model).Writes.IsEmpty && !ExpressionTraps.MayTrap(expression, model);

    private static void Emit(in Plan plan, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        if (!plan.IsSequenced)
        {
            using (writer.Parentheses())
            {
                AppendSelection(plan, receiver: -1, fallback: -1, ref context);
            }

            return;
        }

        using var sequence = EvaluationOrder.BeginSequence(writer);
        var receiver = EvaluationOrder.DeclareTemp(ref context, plan.Receiver, reference: true);
        var fallback = plan.Fallback is { } argument
            ? EvaluationOrder.DeclareTemp(ref context, argument.Expression, reference: false, plan.ValueType)
            : -1;
        sequence.Return();
        AppendSelection(plan, receiver, fallback, ref context);
    }

    // <opt>.HasValue?<opt>.Value:<fallback or T{}>, with the temporaries that are not -1
    private static void AppendSelection(in Plan plan, int receiver, int fallback, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        AppendReceiver(plan.Receiver, receiver, ref context);
        writer.Append(".HasValue?");
        AppendReceiver(plan.Receiver, receiver, ref context);
        writer.Append(".Value:");
        if (fallback >= 0)
        {
            EvaluationOrder.AppendTempName(writer, fallback);
        }
        else if (plan.Fallback is { } argument)
        {
            ArithmeticConversions.AppendArgument(argument, plan.ValueType, ref context);
        }
        else
        {
            context.Emitter.Dialect.AppendType(ref context, plan.ValueType);
            writer.Append("{}");
        }
    }

    // The receiver's temporary, or the receiver, parenthesized unless it is a primary expression
    private static void AppendReceiver(ExpressionSyntax receiver, int temp, ref TranspileContext context)
    {
        if (temp >= 0)
        {
            EvaluationOrder.AppendTempName(context.Emitter.Writer, temp);
            return;
        }

        using (receiver is IdentifierNameSyntax or MemberAccessExpressionSyntax or ElementAccessExpressionSyntax or
                   InvocationExpressionSyntax or ParenthesizedExpressionSyntax or ThisExpressionSyntax
                   ? default
                   : context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(receiver);
        }
    }
}

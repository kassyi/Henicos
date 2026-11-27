namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles the null-conditional access <c>a?.b</c>, <c>a?[i]</c>, <c>a?.M()</c> and <c>a?.b = v</c> (which C#
/// parses as an access whose <c>WhenNotNull</c> is the assignment): the receiver is evaluated once into a temporary,
/// and the access, with the right side of an assignment, is written for it only when it is not null.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class ConditionalAccessExpressionTranspilerRule
    : SyntaxNodeTranspilerRuleBase<ConditionalAccessExpressionSyntax>
{
    // The names of the receiver of the access, read by the identifiers that stand for it
    private const string ReceiverName = "__gsharp_cond_receiver";

    private const string ReceiverValueName = "__gsharp_cond_value";

    /// <inheritdoc />
    public override void Transpile(ConditionalAccessExpressionSyntax node, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        var plan = ConditionalAccess.Plan(node, model);
        if (!IsSupported(node, plan, ref context))
        {
            return;
        }

        var receiver = CreateReceiver(node, model.Compilation);
        var access = ConditionalAccess.BindReceiver(
            node.WhenNotNull,
            GetBindingReceiver(node, plan, receiver, model.Compilation),
            model.Compilation);
        Emit(node, plan, receiver, access, ref context);
    }

    // The plan has the GSH0003 text exactly when the receiver cannot be written
    private static bool IsSupported(ConditionalAccessExpressionSyntax node, ConditionalAccessPlan plan,
        ref TranspileContext context)
    {
        if (plan.Problem is not { } problem)
        {
            return true;
        }

        context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, problem);
        context.Visitor.AppendUnsupportedPlaceholder(node);
        return false;
    }

    // The identifier that stands for the receiver of the access: its type is the receiver's
    private static IdentifierNameSyntax CreateReceiver(ConditionalAccessExpressionSyntax node,
        Compilation compilation) =>
        SyntaxFactory.IdentifierName(ReceiverName).WithSpilledTypeSpanFrom(node.Expression, compilation);

    // A Nullable<S> receiver is read through its Value; a receiver that is never null (a stack array) is read as
    // written
    private static ExpressionSyntax GetBindingReceiver(ConditionalAccessExpressionSyntax node,
        ConditionalAccessPlan plan, IdentifierNameSyntax receiver, Compilation compilation) =>
        plan.Receiver switch
        {
            ConditionalReceiverKind.Nullable => SyntaxFactory.IdentifierName(ReceiverValueName),
            ConditionalReceiverKind.NeverNull => ConditionalAccess.NeverNullReceiver(node, compilation),
            _ => receiver
        };

    // ([&]() { auto tmp = <receiver>; return <receiver is not null> ? <Result>(<access>) : <Result>(); })(), or for a
    // result that is not used: ([&]() { auto tmp = <receiver>; if (<receiver is not null>) { <access>; } })()
    private static void Emit(ConditionalAccessExpressionSyntax node, ConditionalAccessPlan plan,
        IdentifierNameSyntax receiver, ExpressionSyntax access, ref TranspileContext context)
    {
        if (plan.Receiver == ConditionalReceiverKind.NeverNull)
        {
            EmitNeverNull(plan, access, ref context);
            return;
        }

        using var sequence = EvaluationOrder.BeginSequence(context.Emitter.Writer);
        var temp = EvaluationOrder.DeclareTemp(ref context, node.Expression, reference: false);
        using var names = ReceiverNames.Bind(
            context.Emitter.LocalVariableOverrides,
            GSharpInternalNames.Lowering.SequenceTempPrefix + temp);
        if (plan.Form == ConditionalResultForm.Statement)
        {
            WriteStatement(access, receiver, ref context);
            return;
        }

        sequence.Return();
        EmitValue(plan, receiver, access, ref context);
    }

    // <receiver is not null> ? <Result>(<access>) : <Result>(), the access lifted when it has a value type
    private static void EmitValue(ConditionalAccessPlan plan, ExpressionSyntax receiver, ExpressionSyntax access,
        ref TranspileContext context)
    {
        NullRepresentationEmitter.AppendNullTest(receiver, isNull: false, ref context);
        context.Emitter.Writer.Append(" ? ");
        if (plan.Form == ConditionalResultForm.Lifted)
        {
            AppendResultType(plan, ref context);
        }

        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(access);
        }

        context.Emitter.Writer.Append(" : ");
        AppendResultType(plan, ref context);
        context.Emitter.Writer.Append("()");
    }

    // The receiver is a stack array, which is never null: the access itself, lifted when it has a value type
    private static void EmitNeverNull(ConditionalAccessPlan plan, ExpressionSyntax access, ref TranspileContext context)
    {
        if (plan.Form == ConditionalResultForm.Lifted)
        {
            AppendResultType(plan, ref context);
        }

        using (plan.Form == ConditionalResultForm.Statement ? default : context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(access);
        }
    }

    private static void WriteStatement(ExpressionSyntax access, ExpressionSyntax receiver, ref TranspileContext context)
    {
        context.Emitter.Writer.Append("if");
        using (context.Emitter.Writer.Parentheses())
        {
            NullRepresentationEmitter.AppendNullTest(receiver, isNull: false, ref context);
        }

        using (context.Emitter.Writer.Scope())
        {
            context.Visitor.Visit(access);
            context.Emitter.Writer.AppendLine(";");
        }
    }

    private static void AppendResultType(ConditionalAccessPlan plan, ref TranspileContext context)
    {
        if (plan.ResultType is not { } type)
        {
            throw new InvalidOperationException("A null-conditional access with a value has no result type.");
        }

        context.Emitter.Dialect.AppendType(ref context, type);
    }

    // The names that stand for the receiver (the temporary) and its value (the temporary's Value) while the access is
    // written; disposing unbinds both
    private readonly struct ReceiverNames(OverrideScopeStack overrides, int cookie) : IDisposable
    {
        public static ReceiverNames Bind(OverrideScopeStack overrides, string tempName)
        {
            var cookie = overrides.Push(ReceiverName, new OverrideValue(tempName));
            overrides.Push(ReceiverValueName, new OverrideValue(tempName + ".Value"));
            return new ReceiverNames(overrides, cookie);
        }

        public void Dispose() => overrides.Pop(cookie);
    }
}

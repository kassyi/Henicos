using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Writes the assignments of a property with accessor functions that read it or use its value (#908): a compound
/// assignment (<c>s.P += v</c>), an increment or decrement (<c>s.P++</c>, <c>--s.P</c>), and a simple assignment
/// whose value is used (<c>x = s.P = v</c>, <c>F(s.P = v)</c>).
/// </summary>
/// <remarks>
/// <para>
/// C# finds the receiver once, calls the getter, evaluates the right side, applies the operator and calls the setter
/// with the result (C# spec §12.21.4, §12.8.16); the value of the expression is the value passed to the setter, or the
/// value read for a postfix step. The receiver is bound to a reference when finding it reads or writes something
/// (<c>t[k++].P</c>), so that it is found once, and the operands are evaluated in that order into temporaries:
/// </para>
/// <code>
/// ([&amp;]() { auto&amp;&amp; __gsharp_seq_0 = &lt;receiver&gt;; auto __gsharp_seq_1 = __gsharp_seq_0.P();
///   auto __gsharp_seq_2 = &lt;value&gt;; auto __gsharp_seq_3 = (&lt;T&gt;)(&lt;operation&gt;);
///   __gsharp_seq_0.P(__gsharp_seq_3); return __gsharp_seq_3; }())
/// </code>
/// <para>
/// The operation is the one an ordinary compound assignment or step writes (<see cref="CompoundOperator"/>). The getter
/// and setter are the member functions of <c>StructPropertyEmitter</c>, under the one C++ name of the property; the
/// target is written without the getter call (<see cref="PropertyAccessorCall.AppendCallIfGetter"/>) and the call
/// is added here.
/// </para>
/// <para>
/// A property without a setter whose accessors use the <c>field</c> keyword can be stepped in a constructor of its
/// struct (<c>R += 1</c>): C# reads it through the getter and writes the backing field, so the store is
/// <c>__gsharp_self.__gsharp_bf_R = &lt;new value&gt;</c> instead of the setter call.
/// </para>
/// </remarks>
internal static class PropertyAccessorAssignment
{
    /// <summary>The GSH0003 text for a compound assignment or step of a property with accessor functions with an
    /// operator that is
    /// user-defined or lifted.</summary>
    public const string UserDefinedOrLiftedOperator =
        "compound assignment or increment of a computed property with a user-defined or lifted operator";

    private enum Form
    {
        Assignment,
        Compound,
        PrefixStep,
        PostfixStep
    }

    // What the form evaluates, in the C# order: the receiver to bind (null when it is found without evaluating
    // anything), the getter call (not for an assignment), the value (not for a step), the new value, the setter call,
    // or the store into the backing field of StoredProperty (a property without a setter, in a constructor)
    private readonly record struct Plan(
        Form Form,
        ExpressionSyntax Target,
        ExpressionSyntax? BoundReceiver,
        ITypeSymbol? PropertyType,
        CompoundOperator Operator,
        ExpressionSyntax? Value,
        bool IsValueUsed,
        IPropertySymbol? StoredProperty
    );

    /// <summary>
    /// Writes <paramref name="node"/>, a compound assignment of a property with accessor functions, or a simple
    /// assignment of one whose value is used.
    /// </summary>
    public static void TranspileAssignment(AssignmentExpressionSyntax node, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        Plan plan;
        if (node.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            plan = CreatePlan(Form.Assignment, node, node.Left, node.Right, default, ref context);
        }
        else
        {
            var operation = model.GetSafeOperation(node) as ICompoundAssignmentOperation;
            if (operation is { OperatorMethod: not null } or { IsLifted: true } ||
                CompoundOperator.ForAssignment(node, operation) is not { } compoundOperator)
            {
                Fail(node, ref context);
                return;
            }

            plan = CreatePlan(Form.Compound, node, node.Left, node.Right, compoundOperator, ref context);
        }

        Emit(plan, ref context);
    }

    /// <summary>
    /// Writes <paramref name="node"/>, an increment or decrement of the property with accessor functions
    /// <paramref name="operand"/>.
    /// </summary>
    public static void TranspileStep(ExpressionSyntax node, ExpressionSyntax operand, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        if (model.GetSafeOperation(node) is not IIncrementOrDecrementOperation step ||
            step.OperatorMethod != null || step.IsLifted)
        {
            Fail(node, ref context);
            return;
        }

        var form = step.IsPostfix ? Form.PostfixStep : Form.PrefixStep;
        var stepOperator = CompoundOperator.ForStep(step, node, ref context);
        Emit(CreatePlan(form, node, operand, null, stepOperator, ref context), ref context);
    }

    private static Plan CreatePlan(Form form, ExpressionSyntax node, ExpressionSyntax target, ExpressionSyntax? value,
        CompoundOperator compoundOperator, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        var storedProperty = PropertyAccessorCall.WritesBackingField(target, ref context)
            ? PropertyAccessorCall.GetTargetProperty(target, model)
            : null;
        target = IntegerSemanticsEmitter.Unparenthesize(target);
        var receiver = target is MemberAccessExpressionSyntax { Expression: var expression } &&
                       !ExpressionEffects.OfLocation(expression, model).IsEmpty
            ? expression
            : null;
        return new Plan(form, target, receiver, model.GetTypeSymbol(target), compoundOperator, value,
            IntegerSemanticsEmitter.IsValueUsed(node, model), storedProperty);
    }

    private static void Emit(in Plan plan, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        var spilled = context.Emitter.SpilledExpressions;
        using var sequence = EvaluationOrder.BeginSequence(writer);
        if (plan.BoundReceiver is { } receiver)
        {
            spilled[receiver] = EvaluationOrder.DeclareTemp(ref context, receiver, reference: true);
        }

        try
        {
            var current = -1;
            if (plan.Form != Form.Assignment)
            {
                current = EvaluationOrder.BeginDeclaration(ref context);
                AppendGetterCall(plan, ref context);
                writer.AppendLine(";");
            }

            // The value of an assignment is converted to the type of the property when it is evaluated (§12.21.2)
            var value = plan.Value is { } valueSyntax
                ? EvaluationOrder.DeclareTemp(ref context, valueSyntax, reference: false,
                    plan.Form == Form.Assignment || plan.Operator.ConvertsValue ? plan.PropertyType : null)
                : -1;
            var updated = value;
            if (plan.Form != Form.Assignment)
            {
                updated = EvaluationOrder.BeginDeclaration(ref context);
                using (writer.Parentheses())
                {
                    AppendPropertyType(plan.PropertyType, current, ref context);
                }

                plan.Operator.AppendOperation(writer, current, plan.Form == Form.Compound ? value : null);
                writer.AppendLine(";");
            }

            AppendStore(plan, updated, ref context);

            if (plan.IsValueUsed)
            {
                writer.AppendLine(";");
                sequence.Return();
                EvaluationOrder.AppendTempName(writer, plan.Form == Form.PostfixStep ? current : updated);
            }
        }
        finally
        {
            if (plan.BoundReceiver is { } bound)
            {
                spilled.Remove(bound);
            }
        }
    }

    // The C++ type of the property, or decltype of the value read for a type the dialect cannot spell (a type parameter)
    private static void AppendPropertyType(ITypeSymbol? type, int current, ref TranspileContext context)
    {
        if (type != null && TypeMapping.HasDirectCppType(type))
        {
            context.Emitter.Dialect.AppendType(ref context, type);
            return;
        }

        context.Emitter.Writer.Append("decltype");
        using (context.Emitter.Writer.Parentheses())
        {
            EvaluationOrder.AppendTempName(context.Emitter.Writer, current);
        }
    }

    // <target>() or, for a property whose backing field the constructor writes, <receiver>.<getter>(): the target
    // itself names the backing field there (PropertyAccessorCall.WritesBackingField)
    private static void AppendGetterCall(in Plan plan, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        if (plan.StoredProperty is not { } property)
        {
            context.Visitor.Visit(plan.Target);
            writer.Append("()");
            return;
        }

        if (plan.Target is MemberAccessExpressionSyntax { Expression: var receiver })
        {
            context.Visitor.Visit(receiver);
            writer.Append('.');
        }
        else if (context.Emitter.StructConstructorType != null)
        {
            writer.Append(GSharpInternalNames.Lowering.ConstructorSelf).Append('.');
        }

        writer.AppendCppName(CppNameResolver.Resolve(property, property.Name, context.Emitter.Dialect)).Append("()");
    }

    // <target>(<new value>), or <target>=<new value> for a property whose backing field the constructor writes
    private static void AppendStore(in Plan plan, int updated, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        context.Visitor.Visit(plan.Target);
        if (plan.StoredProperty != null)
        {
            writer.Append('=');
            EvaluationOrder.AppendTempName(writer, updated);
            return;
        }

        using (writer.Parentheses())
        {
            EvaluationOrder.AppendTempName(writer, updated);
        }
    }

    private static void Fail(ExpressionSyntax node, ref TranspileContext context)
    {
        context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, UserDefinedOrLiftedOperator);
        context.Visitor.AppendUnsupportedPlaceholder(node);
    }
}

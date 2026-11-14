using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles C# 8+ switch expressions (<c>expr switch { ... }</c>) into immediately invoked C++ lambdas containing a
/// <c>switch</c> statement, or a chain of comparisons for a <see langword="float"/>, <see langword="double"/> or
/// <c>Nullable&lt;T&gt;</c> input.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// <para>
/// C# throws <c>SwitchExpressionException</c> when no arm matches. Without a discard arm the lambda ends with a trap
/// that reports <c>SwitchExpression</c>, which the host throws as that exception, with the place of the switch
/// expression; the trap does not return, so no path of the lambda runs off its end (<see cref="AppendNoMatchTrap"/>).
/// </para>
/// <para>
/// C# converts each arm to the type of the switch expression (its natural type, the best common type of the arms, or
/// the target type; C# spec §12.11). The lambda deduces its return type from the <c>return</c> statements, which C++
/// requires to agree, so an arm of another type is written converted (<c>return 1LL;</c>, #710;
/// <see cref="GetArmConversion"/>).
/// </para>
/// <para>
/// A C++ <c>switch</c> takes only an integral or enumeration value, so a switch expression over a
/// <see langword="float"/>, a <see langword="double"/> or a <c>Nullable&lt;T&gt;</c> evaluates its input once into a
/// temporary and compares it with each constant in order (#1287). The C++ <c>==</c> is the C# equality of a constant
/// pattern, except for NaN: a NaN constant pattern matches a NaN input (C# spec §11.2.3, <c>double.Equals</c>), so it
/// is written as <c>t!=t</c>. <c>0.0</c> and <c>-0.0</c> match each other in both languages.
/// </para>
/// </remarks>
public sealed class SwitchExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<SwitchExpressionSyntax>
{
    // The message of SwitchExpressionException, so the native, CUDA and managed paths report the same text
    private const string NoMatchMessage = "Non-exhaustive switch expression failed to match its input.";

    /// <summary>
    /// Gets the singleton instance of the <see cref="SwitchExpressionTranspilerRule"/>.
    /// </summary>
    public SwitchExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(SwitchExpressionSyntax node, ref TranspileContext context)
    {
        // An unsupported arm is still written (as a default arm), so that the failures inside the input and the arms are
        // recorded too
        ReportUnsupportedArms(node, ref context);
        var plan = Plan(node, ref context);
        using (context.Emitter.Writer.ImmediatelyInvokedLambda())
        {
            if (plan.InputType != null)
            {
                AppendComparisonChain(node, in plan, ref context);
            }
            else
            {
                AppendSwitch(node, in plan, ref context);
            }

            if (plan.NoMatch is { } trap)
            {
                AppendNoMatchTrap(trap, ref context);
            }
        }
    }

    // Records a when clause or a pattern that is not a constant or a discard (GSH0003)
    private static void ReportUnsupportedArms(SwitchExpressionSyntax node, ref TranspileContext context)
    {
        foreach (var arm in node.Arms)
        {
            if (arm.WhenClause != null)
            {
                context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax,
                    arm.WhenClause,
                    GpuCodeConstraints.SwitchArmWhenClauseNotSupported);
            }
            else if (!GpuCodeConstraints.IsSupportedSwitchArmPattern(arm.Pattern))
            {
                context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax,
                    arm.Pattern,
                    GpuCodeConstraints.SwitchArmPatternNotSupported);
            }
        }
    }

    /// <summary>Decides the form of <paramref name="node"/> before anything is written.</summary>
    internal static SwitchExpressionPlan Plan(SwitchExpressionSyntax node, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        var operation = model.GetSafeOperation(node) as ISwitchExpressionOperation;
        if (operation != null && operation.Arms.Length != node.Arms.Count)
        {
            operation = null;
        }

        var governingType = operation?.Value.Type ?? model.GetTypeSymbol(node.GoverningExpression);
        var underlying = governingType is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: [var argument]
        }
            ? argument
            : null;
        var valueType = underlying ?? governingType;
        var comparesValues = underlying != null ||
                             valueType?.SpecialType is SpecialType.System_Single or SpecialType.System_Double;
        return new SwitchExpressionPlan(
            comparesValues ? governingType : null,
            comparesValues ? valueType : null,
            underlying != null,
            operation,
            PlanNoMatchTrap(node, ref context)
        );
    }

    /// <summary>
    /// How the arm at <paramref name="index"/> is written so that every <c>return</c> of the lambda has the type of the
    /// switch expression.
    /// </summary>
    internal static ArmConversion GetArmConversion(in SwitchExpressionPlan plan, int index)
    {
        if (plan.Operation is not { Type: { } type } operation)
        {
            return ArmConversion.AsWritten;
        }

        var value = operation.Arms[index].Value;
        var converted = value is IConversionOperation { IsImplicit: true, OperatorMethod: null } conversion
            ? conversion.Operand
            : value;

        // C++ deduces no return type from {} or nullptr
        if (converted is IDefaultValueOperation ||
            converted is { Type: null, ConstantValue: { HasValue: true, Value: null } })
        {
            return value.ConstantValue.HasValue && type.SpecialType.IsPrimitiveValue()
                ? ArmConversion.Constant
                : ArmConversion.DefaultValue;
        }

        if (ReferenceEquals(converted, value) || converted.Type is not { } source || source.IsEqualTo(type))
        {
            return ArmConversion.AsWritten;
        }

        if (type.SpecialType.IsPrimitiveValue())
        {
            return source.SpecialType.IsPrimitiveValue() ? ArmConversion.Primitive : ArmConversion.AsWritten;
        }

        return type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            ? ArmConversion.Construct
            : ArmConversion.AsWritten;
    }

    // switch (<input>) { case <value>: return <arm>; ... default: return <arm>; }
    private static void AppendSwitch(SwitchExpressionSyntax node, in SwitchExpressionPlan plan,
        ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        writer.Append("switch");
        using (writer.Parentheses())
        {
            context.Visitor.Visit(node.GoverningExpression);
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            for (var i = 0; i < node.Arms.Count; i++)
            {
                var arm = node.Arms[i];
                if (arm.Pattern is ConstantPatternSyntax constantPattern)
                {
                    writer.Append("case ");
                    context.Visitor.Visit(constantPattern.Expression);
                    writer.Append(":");
                }
                else
                {
                    writer.Append("default:");
                }

                writer.Append(" ");
                AppendReturn(arm, i, in plan, ref context);
            }
        }
    }

    // <input type> __gsharp_seq_<n> = <input>;
    // if (<test of the constant>) return <arm>;
    // return <arm>;    (the discard arm)
    private static void AppendComparisonChain(SwitchExpressionSyntax node, in SwitchExpressionPlan plan,
        ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        var input = EvaluationOrder.DeclareTemp(ref context, node.GoverningExpression, reference: false,
            plan.InputType);
        for (var i = 0; i < node.Arms.Count; i++)
        {
            var arm = node.Arms[i];
            if (arm.Pattern is ConstantPatternSyntax constantPattern)
            {
                writer.Append("if");
                using (writer.Parentheses())
                {
                    AppendComparison(constantPattern, in plan, input, ref context);
                }

                writer.Append(" ");
            }

            AppendReturn(arm, i, in plan, ref context);
        }
    }

    // t==<constant converted to the input type>, t!=t for NaN; for a Nullable<T> input t.HasValue&&t.Value==<constant>,
    // and !t.HasValue for null
    private static void AppendComparison(ConstantPatternSyntax pattern, in SwitchExpressionPlan plan, int input,
        ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        var model = context.Query.SemanticModel;
        var constant = model.GetSafeOperation(pattern) is IConstantPatternOperation
        {
            Value.ConstantValue: { HasValue: true } converted
        }
            ? converted
            : model.GetSafeConstantValue(pattern.Expression);
        if (!constant.HasValue || plan.ValueType is not { } valueType)
        {
            // Roslyn binds a constant pattern only to a constant; a pattern it cannot bind is the C# compiler's error
            AppendValue(writer, input, plan.IsNullableInput);
            writer.Append("==");
            context.Visitor.Visit(pattern.Expression);
            return;
        }

        if (constant.Value is not { } value)
        {
            writer.Append('!');
            EvaluationOrder.AppendTempName(writer, input);
            writer.Append(".HasValue");
            return;
        }

        if (plan.IsNullableInput)
        {
            EvaluationOrder.AppendTempName(writer, input);
            writer.Append(".HasValue&&");
        }

        AppendValue(writer, input, plan.IsNullableInput);
        if (IsNaN(value))
        {
            writer.Append("!=");
            AppendValue(writer, input, plan.IsNullableInput);
            return;
        }

        writer.Append("==");
        writer.Append(ConstexprLiteral.FormatCppConstant(valueType, value));
    }

    // __gsharp_seq_<n>, or __gsharp_seq_<n>.Value for a Nullable<T> input
    private static void AppendValue(SourceWriter writer, int input, bool isNullableInput)
    {
        EvaluationOrder.AppendTempName(writer, input);
        writer.AppendIf(isNullableInput, ".Value");
    }

    private static bool IsNaN(object constant) => constant switch
    {
        double number => double.IsNaN(number),
        float single => float.IsNaN(single),
        _ => false
    };

    // return <the arm, written as GetArmConversion decided>;
    private static void AppendReturn(SwitchExpressionArmSyntax arm, int index, in SwitchExpressionPlan plan,
        ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        writer.Append("return ");
        switch (GetArmConversion(in plan, index))
        {
            // GetArmConversion returns these only for a bound operation with a type
            case ArmConversion.Primitive when plan.Operation is { Type: { } type } operation:
                ArithmeticConversions.AppendImplicitlyConverted(operation.Arms[index].Value, arm.Expression, type,
                    ref context);
                break;
            case ArmConversion.Constant when plan.Operation is { Type: { } type } operation:
                writer.Append(ConstexprLiteral.FormatCppConstant(type, operation.Arms[index].Value.ConstantValue.Value));
                break;
            case ArmConversion.DefaultValue when plan.Operation is { Type: { } type }:
                context.Emitter.Dialect.AppendType(ref context, type);
                writer.Append("{}");
                break;
            case ArmConversion.Construct when plan.Operation is { Type: { } type }:
                context.Emitter.Dialect.AppendType(ref context, type);
                using (writer.Parentheses())
                {
                    context.Visitor.Visit(arm.Expression);
                }

                break;
            default:
                context.Visitor.Visit(arm.Expression);
                break;
        }

        writer.AppendLine(";");
    }

    // The trap after the switch: none when a discard arm matches every input; otherwise a site of the kernel's error
    // site table, or (in a device function, which has no table) a message that names the place
    private static NoMatchTrap? PlanNoMatchTrap(SwitchExpressionSyntax node, ref TranspileContext context)
    {
        var hasDiscardArm = node.Arms
            .AsStackEnumerable()
            .Any(static arm => arm.Pattern is DiscardPatternSyntax);
        if (hasDiscardArm)
        {
            return null;
        }

        var model = context.Query.SemanticModel;
        if (context.Emitter.ErrorSites is { } table)
        {
            var memberName = context.Emitter.CurrentMemberName ?? "";
            return new NoMatchTrap(table.Register(node, model, memberName, NoMatchMessage, "SwitchExpression"), "");
        }

        var place = SourcePlace.Format(model, node);
        return new NoMatchTrap(0u, $"Non-exhaustive switch expression failed to match its input at {place}");
    }

    // ([&]() { switch (<input>) { case <value>: return <arm>; } <trap> })(), the trap being
    // __gsharp_trap_abort(GSharpErrorCode::SwitchExpression, <site>u); or
    // __gsharp_trap_abort(GSharpErrorCode::SwitchExpression, "<message>");
    private static void AppendNoMatchTrap(NoMatchTrap trap, ref TranspileContext context)
    {
        UnwindingTrap.Append(ref context, GSharpInternalNames.Prelude.TrapAbort, "SwitchExpression", trap.SiteId,
            trap.Message);
        context.Emitter.Writer.AppendLine(";");
    }

    /// <summary>The form of a switch expression, decided before it is written.</summary>
    /// <param name="InputType">
    /// The type of the input when it is compared with each constant in order (a <see langword="float"/>,
    /// <see langword="double"/> or <c>Nullable&lt;T&gt;</c> input, which a C++ <c>switch</c> cannot take);
    /// <see langword="null"/> for a C++ <c>switch</c>.
    /// </param>
    /// <param name="ValueType">
    /// The type the constants are converted to (the input type, or <c>T</c> of <c>Nullable&lt;T&gt;</c>), when the input
    /// is compared.
    /// </param>
    /// <param name="IsNullableInput">Whether the input is a <c>Nullable&lt;T&gt;</c>, whose <c>HasValue</c> is tested first.</param>
    /// <param name="Operation">
    /// The bound switch expression, whose arms carry the conversions C# applies to them (<see cref="GetArmConversion"/>);
    /// <see langword="null"/> when it is not bound.
    /// </param>
    /// <param name="NoMatch">The trap after the arms, or <see langword="null"/> when a discard arm matches every input.</param>
    internal readonly record struct SwitchExpressionPlan(
        ITypeSymbol? InputType,
        ITypeSymbol? ValueType,
        bool IsNullableInput,
        ISwitchExpressionOperation? Operation,
        NoMatchTrap? NoMatch);

    /// <summary>How an arm is written so that its <c>return</c> has the type of the switch expression.</summary>
    internal enum ArmConversion
    {
        /// <summary>As written: it has that type, or its conversion is written by its own rule (a user-defined one).</summary>
        AsWritten,

        /// <summary>Converted from another primitive type: <c>(long long)x</c>, or the converted constant.</summary>
        Primitive,

        /// <summary>A <c>default</c> literal of a primitive type, as its constant: <c>0</c>.</summary>
        Constant,

        /// <summary>A <c>null</c> or <c>default</c> literal of another type: <c>System_Nullable&lt;int&gt;{}</c>.</summary>
        DefaultValue,

        /// <summary>A value wrapped in the <c>Nullable&lt;T&gt;</c> it converts to: <c>System_Nullable&lt;int&gt;(x)</c>.</summary>
        Construct
    }

    /// <summary>Either a registered error site (<paramref name="SiteId"/>) or, when there is no table, a message with the place.</summary>
    internal readonly record struct NoMatchTrap(uint SiteId, string Message);
}

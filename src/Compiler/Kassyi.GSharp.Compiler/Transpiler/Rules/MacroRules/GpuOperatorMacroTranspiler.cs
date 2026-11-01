using static Kassyi.GSharp.Compiler.OperatorMacroContext;
using static Kassyi.GSharp.Compiler.OperatorMacroResolver;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

/// <summary>
/// Intercepts and transpiles expressions decorated with <c>[GpuOperatorMacro]</c> into specialized device function calls.
/// </summary>
internal static class GpuOperatorMacroTranspiler
{
    /// <summary>
    /// Attempts to intercept and transpile an assignment expression decorated with a GPU operator macro.
    /// </summary>
    public static bool TryTranspile(AssignmentExpressionSyntax assignment, ref TranspileContext context)
    {
        if (!TryResolve(assignment, context.Query.SemanticModel, out var macro))
        {
            return false;
        }

        if (macro.TargetMethod == null)
        {
            if (macro.Failure is { } targetFailure)
            {
                context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, assignment, targetFailure);
            }

            return false;
        }

        if (!macro.Nested.IsDefaultOrEmpty)
        {
            // An operator macro among the operands is computed into a temporary first: a block of statements
            using (context.Emitter.Writer.Scope())
            {
                return AppendExpansion(assignment.Left, -1, macro, assignment, ref context);
            }
        }

        if (!TryPrepareCall(assignment.Left, macro, assignment, ref context, out var call))
        {
            return false;
        }

        var effects = GetOperandEffects(call, macro, assignment, context.Query.SemanticModel);
        var last = EvaluationOrder.LastSpilledOperand(effects);

        // C# evaluates the target and then the operands from left to right; when they interfere, they are evaluated
        // into temporaries in that order first (EvaluationOrder)
        int[]? temps = null;
        using (var sequence = last >= 0 ? EvaluationOrder.BeginSequence(context.Emitter.Writer) : default)
        {
            if (last >= 0)
            {
                // Without an operator macro among the operands, declaring cannot fail
                _ = DeclareOperandTemps(call, macro, effects, last, assignment, ref context, out temps);
                sequence.Return();
            }

            AppendCall(call, temps, -1, assignment, ref context);
        }

        context.Emitter.Writer.AppendLine(";");
        return true;
    }

    // { auto __gsharp_seq_0 = <operand>; <T> __gsharp_seq_1{}; <inner target>(&__gsharp_seq_1, <inner operands>); <target>(<result>, __gsharp_seq_0, __gsharp_seq_1); }
    // The call of <macro> with <result> (or the temporary <resultTemp>) as the result, after its operands: each operator
    // macro among them is computed into a temporary of its own, and the operands before the last one that interferes
    // with a later one (an inner call included) are evaluated into temporaries, in the C# order (EvaluationOrder)
    private static bool AppendExpansion(ExpressionSyntax? result, int resultTemp, OperatorMacro macro,
        AssignmentExpressionSyntax assignment, ref TranspileContext context)
    {
        if (!TryPrepareCall(result, macro, assignment, ref context, out var call))
        {
            return false;
        }

        var effects = GetOperandEffects(call, macro, assignment, context.Query.SemanticModel);
        if (!DeclareOperandTemps(call, macro, effects, EvaluationOrder.LastSpilledOperand(effects), assignment,
                ref context, out var temps))
        {
            return false;
        }

        AppendCall(call, temps, resultTemp, assignment, ref context);
        context.Emitter.Writer.AppendLine(";");
        return true;
    }

    // The call of <macro> (OperatorMacroResolver.TryPrepareCall); a context variable that cannot be found fails the
    // transpilation with the message the analyzer reports as well
    private static bool TryPrepareCall(ExpressionSyntax? result, OperatorMacro macro,
        AssignmentExpressionSyntax assignment, ref TranspileContext context, out MacroCall call)
    {
        if (OperatorMacroResolver.TryPrepareCall(result, macro, assignment, context.Query.SemanticModel, out call,
                out var failure))
        {
            return true;
        }

        if (failure != null)
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, assignment, failure);
        }

        return false;
    }

    // In the order of the operands: an operator macro among them as <T> __gsharp_seq_<n>{}; and its own expansion, which
    // computes it into that temporary; an operand up to <last> that has an effect as auto&& __gsharp_seq_<n> = <operand
    // passed by reference>; or auto __gsharp_seq_<n> = <operand>; (-1 in <temps>: emitted in place)
    private static bool DeclareOperandTemps(in MacroCall call, OperatorMacro macro, ExpressionEffects[] effects, int last,
        AssignmentExpressionSyntax assignment, ref TranspileContext context, out int[] temps)
    {
        var writer = context.Emitter.Writer;
        temps = new int[call.Operands.Length];
        for (var i = 0; i < temps.Length; i++)
        {
            temps[i] = -1;
            if (call.Operands[i] is not { } operand || call.Parameters[i] is not { } parameter)
            {
                continue;
            }

            if (macro.TryGetNested(operand, out var inner) && inner.ResultType is { } innerType)
            {
                temps[i] = context.Emitter.SequenceTempCounter++;
                context.Emitter.Dialect.AppendType(ref context, innerType);
                writer.Append(' ');
                EvaluationOrder.AppendTempName(writer, temps[i]);
                writer.AppendLine("{};");
                if (!AppendExpansion(null, temps[i], inner, assignment, ref context))
                {
                    return false;
                }
            }
            else if (i <= last && !effects[i].IsEmpty)
            {
                temps[i] = EvaluationOrder.DeclareTemp(ref context, operand, IsPassedByReference(parameter));
            }
        }

        return true;
    }

    // <target>(<context>, <result>, <operands>): each operand in place, its temporary, or for the result the temporary
    // <resultTemp> of an inner operator macro
    private static void AppendCall(in MacroCall call, int[]? temps, int resultTemp,
        AssignmentExpressionSyntax assignment, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;

        // [WHY] The same resolver as the declaration, so the call names the renamed (and overload-numbered) function.
        CppNameResolver.AppendSymbolName(call.Target, ref context);
        using (writer.Parentheses())
        {
            if (call.ContextVariable != null)
            {
                AppendContextArgument(call.ContextVariable, call.Target.Parameters[0], assignment, ref context);
            }

            for (var i = 0; i < call.Operands.Length; i++)
            {
                if (call.Parameters[i] is not { } parameter)
                {
                    continue;
                }

                AppendSeparator(parameter.Ordinal, ref context);
                if (call.Operands[i] is { } operand)
                {
                    AppendOperand(operand, temps?[i] ?? -1, parameter, ref context);
                    continue;
                }

                // The temporary of an inner operator macro, passed to its ref or out result parameter
                writer.Append('&');
                EvaluationOrder.AppendTempName(writer, resultTemp);
            }
        }
    }

    // <operand> or &<operand> (or its temporary), after a comma unless it is the first argument; nothing when the
    // operator has no such operand
    private static void AppendOperand(ExpressionSyntax operand, int temp, IParameterSymbol parameter,
        ref TranspileContext context)
    {
        if (temp >= 0)
        {
            context.Emitter.Writer.AppendIf(
                IsPassedByReference(parameter) && RefSyntaxHelper.NeedsAddressOf(operand, context.Query.SemanticModel),
                "&");
            EvaluationOrder.AppendTempName(context.Emitter.Writer, temp);
            return;
        }

        if (IsPassedByReference(parameter))
        {
            RefSyntaxHelper.TranspileAsByRefArgument(ref context, operand, allowAutoProperty: true);
            return;
        }

        context.Visitor.Visit(operand);
    }

    // The context variable as RoslynToCudaVisitor.VisitIdentifierName and RefSyntaxHelper emit a reference to it
    private static void AppendContextArgument(ISymbol variable, IParameterSymbol parameter,
        AssignmentExpressionSyntax assignment, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        var writer = context.Emitter.Writer;

        // [WHY] Lowering moves a local that lives across a barrier into the state machine frame and rewrites every
        // reference to it; the call is not one of those references, so it copies one of them.
        if (FindLoweredReference(assignment, variable, model) is { } reference)
        {
            if (IsPassedByReference(parameter) && RefSyntaxHelper.NeedsAddressOf(reference, model))
            {
                writer.Append('&');
            }

            context.Visitor.Visit(reference);
            return;
        }

        if (IsPassedByReference(parameter))
        {
            writer.Append('&');
        }

        if (context.Emitter.TryGetOverride(variable.Name, out var overrideValue))
        {
            overrideValue.WriteTo(context.Visitor);
            return;
        }

        if (variable is IParameterSymbol { RefKind: RefKind.Ref or RefKind.Out } or ILocalSymbol { IsRef: true } ||
            (variable is IParameterSymbol { RefKind: RefKind.In or RefKind.RefReadOnlyParameter, ContainingSymbol: IMethodSymbol paramMethod } &&
             !paramMethod.HasAttribute(KnownAttributes.GpuKernel)))
        {
            using (writer.Parentheses())
            {
                writer.Append('*');
                CppNameResolver.AppendSymbolName(variable, ref context);
            }

            return;
        }

        CppNameResolver.AppendSymbolName(variable, ref context);
    }

    private static void AppendSeparator(int parameterIndex, ref TranspileContext context)
    {
        if (parameterIndex > 0)
        {
            context.Emitter.Writer.Append(',');
        }
    }
}

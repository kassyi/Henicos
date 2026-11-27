using static Kassyi.GSharp.Compiler.OperatorMacroCallPlanner;
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

        if (!macro.Nested.IsDefaultOrEmpty || macro.Test != null)
        {
            // An operator macro among the operands is computed into a temporary first, and a && or || tests its left
            // operand first: a block of statements
            using (context.Emitter.Writer.Scope())
            {
                return AppendExpansion(assignment.Left, MacroResultSlot.None, macro, assignment, ref context);
            }
        }

        if (!TryPrepareCall(assignment.Left, macro, assignment, ref context, out var call))
        {
            return false;
        }

        if (FailIfSuspendingTarget(macro, ref context))
        {
            return false;
        }

        var model = context.Query.SemanticModel;
        var effects = GetOperandEffects(call, macro, assignment, model);
        var last = EvaluationOrder.LastSpilledOperand(effects);
        var arguments = PlanArguments(call, macro, effects, last, model);

        // C# evaluates the target and then the operands from left to right; when they interfere, they are evaluated
        // into temporaries in that order first (EvaluationOrder)
        int[]? temps = null;
        using (var sequence = last >= 0 ? EvaluationOrder.BeginSequence(context.Emitter.Writer) : default)
        {
            if (last >= 0)
            {
                // Without an operator macro among the operands, declaring cannot fail
                _ = DeclareOperandTemps(arguments, MacroCall.OperandRoles.Length, assignment, ref context, out temps);
                sequence.Return();
            }

            AppendCall(call, arguments, temps, MacroResultSlot.None, assignment, ref context);
        }

        context.Emitter.Writer.AppendLine(";");
        return true;
    }

    // { auto __gsharp_seq_0 = <operand>; <T> __gsharp_seq_1{}; <inner target>(&__gsharp_seq_1, <inner operands>); <target>(<result>, __gsharp_seq_0, __gsharp_seq_1); }
    // The call of <macro> with <result> (or <resultSlot>, the temporary or the declared variable that receives the result)
    // as the result, after its operands: each operator macro among them is computed into a temporary of its own, and
    // the operands before the last one that interferes with a later one (an inner call included) are evaluated into
    // temporaries, in the C# order (EvaluationOrder)
    private static bool AppendExpansion(ExpressionSyntax? result, MacroResultSlot resultSlot, OperatorMacro macro,
        SyntaxNode site, ref TranspileContext context)
    {
        if (macro.Test != null)
        {
            return AppendConditional(result, resultSlot, macro, site, ref context);
        }

        if (!TryPrepareCall(result, macro, site, ref context, out var call))
        {
            return false;
        }

        if (FailIfSuspendingTarget(macro, ref context))
        {
            return false;
        }

        var model = context.Query.SemanticModel;
        var effects = GetOperandEffects(call, macro, site, model);
        var arguments = PlanArguments(call, macro, effects, EvaluationOrder.LastSpilledOperand(effects), model);
        if (!DeclareOperandTemps(arguments, MacroCall.OperandRoles.Length, site, ref context, out var temps))
        {
            return false;
        }

        AppendCall(call, arguments, temps, resultSlot, site, ref context);
        context.Emitter.Writer.AppendLine(";");
        return true;
    }

    // [auto&& __gsharp_seq_0 = <target>;] <T> __gsharp_seq_1 = <left>;
    // if(<test>(__gsharp_seq_1)) { <target>=__gsharp_seq_1; }
    // else { <right temporaries> <target>(<result>, __gsharp_seq_1, <right>); }
    // A && or || over <macro> (OperatorMacroCallPlanner.PlanConditional): the left operand is evaluated once and
    // tested, and the target is called with the right operand only when the test fails. <result> or <resultSlot> (the
    // temporary or the declared variable that receives the result) receives the left operand or the target's result
    private static bool AppendConditional(ExpressionSyntax? result, MacroResultSlot resultSlot, OperatorMacro macro,
        SyntaxNode site, ref TranspileContext context)
    {
        if (!TryPrepareCall(result, macro, site, ref context, out var call))
        {
            return false;
        }

        if (FailIfSuspendingTarget(macro, ref context))
        {
            return false;
        }

        var effects = GetOperandEffects(call, macro, site, context.Query.SemanticModel);
        var plan = PlanConditional(call, macro, effects);
        int[] temps = [-1, -1, -1, -1];
        if (plan.SpillsTarget && result != null)
        {
            temps[(int)MacroOperandRole.Result] = EvaluationOrder.DeclareTemp(ref context, result, reference: true);
        }

        if (!TryDeclareLeftOperand(call, macro, site, ref context, out temps[(int)MacroOperandRole.Left]))
        {
            return false;
        }

        AppendTest(result, resultSlot, plan, temps, ref context);
        var writer = context.Emitter.Writer;
        writer.AppendLine("else");
        using (writer.Scope())
        {
            if (call.Right.Expression is { } right &&
                !TryDeclareNestedTemp(right, macro, site, ref context, out temps[(int)MacroOperandRole.Right]))
            {
                return false;
            }

            var arguments = PlanArguments(call, macro, null, -1, context.Query.SemanticModel);
            AppendCall(call, arguments, temps, resultSlot, site, ref context);
            writer.AppendLine(";");
        }

        return true;
    }

    // <T> __gsharp_seq_<n> = <left>; the left operand of a && or || into the temporary <temp>: an operator macro
    // computed into its own temporary, or the value of the operand
    private static bool TryDeclareLeftOperand(in MacroCall call, OperatorMacro macro,
        SyntaxNode site, ref TranspileContext context, out int temp)
    {
        var left = call.Left.Expression ?? throw new InvalidOperationException("A && or || has a left operand.");
        if (!TryDeclareNestedTemp(left, macro, site, ref context, out temp))
        {
            return false;
        }

        if (temp < 0)
        {
            var operandType = macro.ResultType ?? throw new InvalidOperationException("An operator macro has a type.");
            temp = DeclareValue(left, operandType, ref context);
        }

        return true;
    }

    internal delegate void TruthTestOperandWriter<in TOperand>(TOperand operand, ref TranspileContext context);

    // <test>([&]<operand>)
    internal static void AppendTruthTestCall<TOperand>(
        IMethodSymbol test,
        bool passByReference,
        TOperand operand,
        TruthTestOperandWriter<TOperand> writeOperand,
        ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        writer.Append(GlobalCppNames.GetFunctionName(test));
        using (writer.Parentheses())
        {
            writer.AppendIf(passByReference, "&");
            writeOperand(operand, ref context);
        }
    }

    // if(<test>(__gsharp_seq_1)) { <target>=__gsharp_seq_1; }
    // The test of the left operand in its temporary (temps[Left]) and the assignment of it to the target
    // (temps[Result], <resultSlot>, or <result>) when the test holds
    private static void AppendTest(ExpressionSyntax? result, MacroResultSlot resultSlot, in MacroConditionalPlan plan,
        int[] temps, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        writer.Append("if");
        using (writer.Parentheses())
        {
            AppendTruthTestCall(
                plan.Test,
                plan.PassesTestByReference,
                temps[(int)MacroOperandRole.Left],
                static (temp, ref ctx) => EvaluationOrder.AppendTempName(ctx.Emitter.Writer, temp),
                ref context
            );
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            AppendTarget(result, temps[(int)MacroOperandRole.Result], resultSlot, ref context);
            writer.Append("=");
            EvaluationOrder.AppendTempName(writer, temps[(int)MacroOperandRole.Left]);
            writer.AppendLine(";");
        }
    }

    // <T> __gsharp_seq_<n> = <operand>; the value of <operand> as the operator's operand type <type>
    private static int DeclareValue(ExpressionSyntax operand, ITypeSymbol type, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        context.Emitter.Dialect.AppendType(ref context, type);
        var temp = context.Emitter.SequenceTempCounter++;
        writer.Append(' ');
        EvaluationOrder.AppendTempName(writer, temp);
        writer.Append(" = ");
        context.Visitor.Visit(operand);
        writer.AppendLine(";");
        return temp;
    }

    // The target that receives the result: its reference <targetTemp>, <resultSlot> (the temporary or the declared
    // variable that receives the result when <result> is null), or <result> itself
    private static void AppendTarget(ExpressionSyntax? result, int targetTemp, MacroResultSlot resultSlot,
        ref TranspileContext context)
    {
        if (result is null)
        {
            resultSlot.Append(ref context);
            return;
        }

        if (targetTemp >= 0)
        {
            EvaluationOrder.AppendTempName(context.Emitter.Writer, targetTemp);
            return;
        }

        context.Visitor.Visit(result);
    }

    // The expansion calls the target of <macro> without the barrier frame it needs to suspend (on any dialect, or
    // inside a struct member); report GSH0062 at macro.OperatorCall before any of the call is written
    private static bool FailIfSuspendingTarget(in OperatorMacro macro, ref TranspileContext context)
    {
        if (macro.TargetMethod is not { } target ||
            !SuspensionAnalysis.IsSuspendingFunction(target, context.Query.SemanticModel.Compilation))
        {
            return false;
        }

        // [WHY] Report on every dialect (StackCppDialect included, so NativeStackTranslator.ForKernel keeps #error
        // instead of replacing it with unguarded stack C++). Narrow this once the CPU stack models give operator
        // macros a barrier frame (#2105).
        var locationContext = context.Emitter.CurrentStructType != null
            ? StructMemberSuspendingCall.DescribeMember(macro.OperatorCall, context.Query.SemanticModel)
            : "through an operator macro";
        context.Reporter.Fail(
            GSharpDiagnostics.SuspendingCallInUnsupportedContext,
            macro.OperatorCall,
            target.Name,
            locationContext,
            SplitPointPlacement.SuspendingDeviceFunctionCallee);
        return true;
    }

    // The call of <macro> (OperatorMacroCallPlanner.TryPrepareCall); a context variable that cannot be found fails the
    // transpilation with the message the analyzer reports as well
    private static bool TryPrepareCall(ExpressionSyntax? result, OperatorMacro macro,
        SyntaxNode site, ref TranspileContext context, out MacroCall call)
    {
        if (OperatorMacroCallPlanner.TryPrepareCall(result, macro, site, context.Query.SemanticModel, out call,
                out var failure))
        {
            return true;
        }

        if (failure != null)
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, site, failure);
        }

        return false;
    }

    // In the order of the arguments (PlanArguments): an operator macro among them as <T> __gsharp_seq_<n>{}; and its
    // own expansion, which computes it into that temporary; an operand that spills as auto&& __gsharp_seq_<n> =
    // <operand passed by reference>; or auto __gsharp_seq_<n> = <operand>; (-1 in <temps>, indexed by MacroOperandRole:
    // emitted in place)
    private static bool DeclareOperandTemps(ImmutableArray<MacroArgument> arguments, int operandCount,
        SyntaxNode site, ref TranspileContext context, out int[] temps)
    {
        temps = new int[operandCount];
        temps.AsSpan().Fill(-1);
        foreach (var argument in arguments)
        {
            switch (argument)
            {
                case { Kind: MacroArgumentKind.NestedOperand, Nested.Inner: var inner }:
                    if (!DeclareNestedTemp(inner, site, ref context, out temps[(int)argument.Role]))
                    {
                        return false;
                    }

                    break;
                case { Kind: MacroArgumentKind.Operand, Spills: true, Operand: { } operand }:
                    temps[(int)argument.Role] = EvaluationOrder.DeclareTemp(ref context, operand,
                        argument.IsPassedByReference);
                    break;
            }
        }

        return true;
    }

    // <T> __gsharp_seq_<n>{}; and the expansion of the operator macro that <operand> is, which computes it into that
    // temporary <temp>; -1 when the operand is not an operator macro
    private static bool TryDeclareNestedTemp(ExpressionSyntax operand, OperatorMacro macro,
        SyntaxNode site, ref TranspileContext context, out int temp)
    {
        temp = -1;
        return !macro.TryGetNested(operand, out var inner) || inner.ResultType is null ||
               DeclareNestedTemp(inner, site, ref context, out temp);
    }

    // <T> __gsharp_seq_<n>{}; and the expansion of the inner operator macro <inner>, which computes it into that
    // temporary <temp>
    private static bool DeclareNestedTemp(OperatorMacro inner, SyntaxNode site,
        ref TranspileContext context, out int temp)
    {
        var innerType = inner.ResultType ?? throw new InvalidOperationException("An inner operator macro has a type.");
        var writer = context.Emitter.Writer;
        temp = context.Emitter.SequenceTempCounter++;
        context.Emitter.Dialect.AppendType(ref context, innerType);
        writer.Append(' ');
        EvaluationOrder.AppendTempName(writer, temp);
        writer.AppendLine("{};");
        return AppendExpansion(null, MacroResultSlot.FromTemp(temp), inner, site, ref context);
    }

    // <target>(<context>, <result>, <operands>): the arguments of the plan in parameter order, each operand in place or
    // its temporary, and for the result when <result> is null its <resultSlot> (the temporary or the declared variable)
    private static void AppendCall(in MacroCall call, ImmutableArray<MacroArgument> arguments, int[]? temps,
        MacroResultSlot resultSlot, SyntaxNode site, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;

        // [WHY] The same resolver as the declaration, so the call names the renamed (and overload-numbered) function.
        CppNameResolver.AppendSymbolName(call.Target, ref context);
        using (writer.Parentheses())
        {
            foreach (var argument in arguments)
            {
                AppendSeparator(argument.Parameter.Ordinal, ref context);
                AppendArgument(call, argument, temps, resultSlot, site, ref context);
            }
        }
    }

    // One argument of the plan: the context variable, an operand in place or its temporary, or <resultSlot> (the
    // temporary or the declared variable that receives the result), passed to its ref or out result parameter
    private static void AppendArgument(in MacroCall call, in MacroArgument argument, int[]? temps,
        MacroResultSlot resultSlot, SyntaxNode site, ref TranspileContext context)
    {
        switch (argument)
        {
            case { Kind: MacroArgumentKind.Context }:
                var variable = call.ContextVariable ??
                               throw new InvalidOperationException("A context argument has a variable.");
                AppendContextArgument(variable, argument.Parameter, site, ref context);
                break;
            case { Operand: { } operand }:
                AppendOperand(operand, temps?[(int)argument.Role] ?? -1, argument, ref context);
                break;
            default:
                context.Emitter.Writer.Append('&');
                resultSlot.Append(ref context);
                break;
        }
    }

    // <operand> or &<operand> (or its temporary), after a comma unless it is the first argument; nothing when the
    // operator has no such operand
    private static void AppendOperand(ExpressionSyntax operand, int temp, in MacroArgument argument,
        ref TranspileContext context)
    {
        if (temp >= 0)
        {
            context.Emitter.Writer.AppendIf(
                argument.IsPassedByReference && RefSyntaxHelper.NeedsAddressOf(operand, context.Query.SemanticModel),
                "&");
            EvaluationOrder.AppendTempName(context.Emitter.Writer, temp);
            return;
        }

        if (argument.IsPassedByReference)
        {
            // [WHY] An auto-property is stored directly as a field on device. Passing its storage address
            // preserves operator operand aliasing (when target and operand are the same property) and
            // avoids an unnecessary defensive copy on device, matching OperatorMacroArgumentTests.
            RefSyntaxHelper.TranspileAsOperatorOperand(ref context, operand);
            return;
        }

        context.Visitor.Visit(operand);
    }

    // The context variable as RoslynToCudaVisitor.VisitIdentifierName and RefSyntaxHelper emit a reference to it
    private static void AppendContextArgument(ISymbol variable, IParameterSymbol parameter,
        SyntaxNode site, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        var writer = context.Emitter.Writer;

        // [WHY] Lowering moves a local that lives across a barrier into the state machine frame and rewrites every
        // reference to it; the call is not one of those references, so it copies one of them.
        if (FindLoweredReference(site, variable, model) is { } reference)
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

        if (context.Emitter.TryGetOverride(variable.Name, variable, out var overrideValue))
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

    // The slot that receives an operator macro's result when there is no assignment target expression: a sequence
    // temporary index (stored 1-based so default is None) or a declared local variable.
    private readonly struct MacroResultSlot
    {
        private readonly int _tempPlusOne;
        private readonly VariableDeclaratorSyntax? _declarator;

        private MacroResultSlot(int tempPlusOne, VariableDeclaratorSyntax? declarator)
        {
            _tempPlusOne = tempPlusOne;
            _declarator = declarator;
        }

        public static MacroResultSlot None => default;

        public static MacroResultSlot FromTemp(int temp)
        {
            if (temp < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(temp));
            }

            return new MacroResultSlot(temp + 1, null);
        }

        public static MacroResultSlot FromDeclarator(VariableDeclaratorSyntax declarator) => new(0, declarator);

        public void Append(ref TranspileContext context)
        {
            if (_declarator != null)
            {
                CppNameResolver.AppendDeclaredName(_declarator, _declarator.Identifier, ref context);
                return;
            }

            if (_tempPlusOne <= 0)
            {
                throw new InvalidOperationException("An operator macro result slot has no temporary or declarator.");
            }

            EvaluationOrder.AppendTempName(context.Emitter.Writer, _tempPlusOne - 1);
        }
    }
}

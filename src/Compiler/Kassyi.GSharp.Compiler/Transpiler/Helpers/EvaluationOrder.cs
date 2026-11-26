using System.Buffers;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Keeps the C# order of evaluation (C# spec §12.4.1: operands from left to right; §12.6.2.3: arguments in order) in the
/// C++ that G# emits, where the order of function arguments and of the operands of most binary operators is unspecified
/// and the right side of an assignment is evaluated first (#608).
/// </summary>
/// <remarks>
/// <para>
/// A form whose operands interfere (<see cref="ExpressionEffects.InterferesWith"/>) is emitted as an immediately invoked
/// lambda that stores the operands in temporaries in the C# order and then evaluates the form on them, Roslyn's
/// <c>BoundSequence</c>:
/// <c>([&amp;]() { &lt;T0&gt; __gsharp_seq_0 = &lt;e0&gt;; &lt;T1&gt; __gsharp_seq_1 = &lt;e1&gt;; return &lt;form over the temporaries&gt;; }())</c>.
/// Every operand up to the last one that takes part in an interference is stored, constants and other operands without
/// effects excepted; the operands after it stay in place, since nothing they read or write is touched by another.
/// </para>
/// <para>
/// [WHY] The emission stage rather than lowering: CUDA does not go through lowering, an expression in a loop condition or
/// an operand of <c>&amp;&amp;</c> cannot be moved to a statement before it, and the C# tree (analysis, diagnostics,
/// managed output) stays as written. See #608 (Device calls evaluate arguments in an unspecified order).
/// </para>
/// </remarks>
internal static class EvaluationOrder
{
    /// <summary>
    /// The index of the last operand that must be evaluated into a temporary, or -1 when no two operands interfere.
    /// <paramref name="operands"/> are in the C# order of evaluation.
    /// </summary>
    public static int LastSpilledOperand(ReadOnlySpan<ExpressionEffects> operands)
    {
        for (var j = operands.Length - 1; j > 0; j--)
        {
            for (var i = 0; i < j; i++)
            {
                if (operands[i].InterferesWith(operands[j]))
                {
                    return j;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// Opens the lambda of a sequence: <c>([&amp;]() {</c>, or <c>([&amp;]() -&gt; &lt;returnType&gt; {</c>.
    /// <see cref="SequenceScope.Return"/> starts its value and <see cref="SequenceScope.Dispose"/> closes it:
    /// <c>; }())</c>.
    /// </summary>
    public static SequenceScope BeginSequence(SourceWriter writer, string? returnType = null) => new(writer, returnType);

    /// <summary>
    /// The return type of the lambda around a call of <paramref name="callee"/>: <c>decltype(auto)</c> for a call that
    /// returns by reference, which stays a reference (an assignment target, a <c>ref</c> argument), otherwise none (the
    /// value).
    /// </summary>
    public static string? GetReturnType(IMethodSymbol? callee) =>
        callee is { RefKind: not RefKind.None } ? ReferenceReturnType : null;

    private const string ReferenceReturnType = "decltype(auto)";

    /// <summary>
    /// Writes <c>auto __gsharp_seq_&lt;n&gt; = &lt;expression&gt;;</c>, or <c>auto&amp;&amp;</c> for a
    /// <paramref name="reference"/> to the location (an assignment target), and returns <c>n</c>. A value is declared
    /// with <paramref name="type"/> when one is given (it is converted when it is evaluated, as in C#, and a brace
    /// initializer such as <c>{}</c> needs a type), except a type parameter or an enum, which keep <c>auto</c>. The
    /// declaration ends with a line break, or with a space when <paramref name="lineBreak"/> is false.
    /// </summary>
    public static int DeclareTemp(ref TranspileContext context, ExpressionSyntax expression, bool reference,
        ITypeSymbol? type = null, bool lineBreak = true)
    {
        if (!reference && type is not { TypeKind: TypeKind.Enum } && TypeMapping.HasDirectCppType(type))
        {
            context.Emitter.Dialect.AppendType(ref context, type);
        }
        else
        {
            context.Emitter.Writer.Append(reference ? "auto&&" : "auto");
        }

        var temp = AppendDeclaredName(ref context);
        context.Visitor.Visit(expression);
        if (lineBreak)
        {
            context.Emitter.Writer.AppendLine(";");
        }
        else
        {
            context.Emitter.Writer.Append("; ");
        }

        return temp;
    }

    /// <summary>
    /// Writes <c>auto __gsharp_seq_&lt;n&gt; = </c> and returns <c>n</c>; the caller writes the value and <c>;</c>
    /// (a value that is not the translation of one expression, such as a getter call or an operation on temporaries).
    /// </summary>
    public static int BeginDeclaration(ref TranspileContext context)
    {
        context.Emitter.Writer.Append("auto");
        return AppendDeclaredName(ref context);
    }

    /// <summary>Writes <c>auto __gsharp_seq_&lt;n&gt; = __gsharp_seq_&lt;source&gt;;</c> (a read of a location) and returns <c>n</c>.</summary>
    public static int DeclareCopy(ref TranspileContext context, int source)
    {
        context.Emitter.Writer.Append("auto");
        var temp = AppendDeclaredName(ref context);
        AppendTempName(context.Emitter.Writer, source);
        context.Emitter.Writer.AppendLine(";");
        return temp;
    }

    /// <summary>Writes <c>__gsharp_seq_&lt;temp&gt;</c>.</summary>
    public static void AppendTempName(SourceWriter writer, int temp) =>
        writer.Append($"{GSharpInternalNames.Lowering.SequenceTempPrefix}{temp}");

    // " __gsharp_seq_<n> = " with the next number of the method
    private static int AppendDeclaredName(ref TranspileContext context)
    {
        var temp = context.Emitter.SequenceTempCounter++;
        context.Emitter.Writer.Append(' ');
        AppendTempName(context.Emitter.Writer, temp);
        context.Emitter.Writer.Append(" = ");
        return temp;
    }

    /// <summary>
    /// Starts the arguments of a call (or of a brace initialization, when <paramref name="isCall"/> is
    /// <see langword="false"/>) whose arguments, in the written order, are <paramref name="arguments"/>: when two of
    /// them interfere, or <paramref name="spillAll"/> (they are passed in another order than written), writes the
    /// lambda and the temporaries and <c>return </c>. The caller then writes the call, emitting each argument with
    /// <see cref="CallSequence.AppendArgument"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="receiver"/> is the receiver of an instance call, which C++ evaluates before the arguments. The
    /// lambda would evaluate the temporaries first, so the call is left as it is when an argument that would be moved
    /// into a temporary interferes with the receiver.
    /// </remarks>
    public static CallSequence BeginCall(ref TranspileContext context, SeparatedSyntaxList<ArgumentSyntax> arguments,
        IMethodSymbol? callee, ExpressionSyntax? receiver, bool spillAll, bool isCall = true) =>
        BeginCallCore(ref context, default, arguments, callee, spillAll, isCall, instanceReceiver: receiver);

    /// <summary>
    /// Starts a call in reduced extension form (<c>receiver.M(arguments)</c>) that C++ emits as a static call
    /// (<c>M(receiver, arguments)</c>): when the receiver and arguments (or two arguments) interfere, writes the lambda
    /// and the temporaries in the C# evaluation order (receiver first, then arguments left-to-right) and <c>return </c>.
    /// The caller then writes the call, emitting the receiver with <see cref="CallSequence.AppendReceiver(ExpressionSyntax, bool, ref TranspileContext)"/> and each
    /// argument with <see cref="CallSequence.AppendArgument"/>.
    /// </summary>
    public static CallSequence BeginExtensionCall(
        ref TranspileContext context,
        ExpressionSyntax? receiver,
        IParameterSymbol? receiverParameter,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IMethodSymbol? callee)
    {
        if (receiver == null)
        {
            return BeginCall(ref context, arguments, callee, receiver: null, spillAll: false);
        }

        var byReference = receiverParameter is { RefKind: not RefKind.None };
        var plan = new ReceiverPlan(
            byReference ? ReceiverForm.Address : ReceiverForm.Value,
            receiver,
            default,
            CopyType: null,
            ReadOnlyCallee: receiverParameter?.RefKind is not RefKind.Ref,
            IsExtensionReceiver: true,
            ValueType: receiverParameter?.Type);
        return BeginReceiverCall(ref context, in plan, arguments, callee);
    }

    /// <summary>
    /// Starts a call whose receiver is planned by <see cref="ReceiverPlanner"/> and emitted as the first argument of a
    /// free C++ function (<c>M(receiver, arguments)</c>).
    /// </summary>
    public static CallSequence BeginReceiverCall(
        ref TranspileContext context,
        scoped in ReceiverPlan receiver,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IMethodSymbol? callee) =>
        receiver.HasSequencedOperand
            ? BeginCallCore(ref context, in receiver, arguments, callee, spillAll: false, isCall: true,
                instanceReceiver: null)
            : BeginCall(ref context, arguments, callee, receiver: null, spillAll: false);

    private static CallSequence BeginCallCore(
        ref TranspileContext context,
        scoped in ReceiverPlan receiverPlan,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IMethodSymbol? callee,
        bool spillAll,
        bool isCall,
        ExpressionSyntax? instanceReceiver)
    {
        var count = arguments.Count;
        var hasReceiver = receiverPlan.HasSequencedOperand;
        var total = hasReceiver ? count + 1 : count;
        if (total < 2 && !spillAll)
        {
            return default;
        }

        var model = context.Query.SemanticModel;
        var effects = ArrayPool<ExpressionEffects>.Shared.Rent(total);
        try
        {
            var effectsSpan = effects.AsSpan(0, total);
            CollectCallEffects(in receiverPlan, arguments, callee, model, effectsSpan);
            if (!TryDetermineLastSpill(effectsSpan, instanceReceiver, spillAll, model, out var last))
            {
                return default;
            }

            var temps = RentTemps(count);
            var lambda = context.Emitter.Writer.ImmediatelyInvokedLambda(GetReturnType(callee));
            var receiverTemp = DeclareSequencedOperands(ref context, in receiverPlan,
                arguments, callee, effectsSpan, last, spillAll, isCall, temps.AsSpan(0, count));

            context.Emitter.Writer.Append("return ");
            return new CallSequence(context.Emitter.Writer, lambda, arguments, temps, receiverTemp, isCall);
        }
        finally
        {
            ArrayPool<ExpressionEffects>.Shared.Return(effects, clearArray: true);
        }
    }

    private static void CollectCallEffects(
        in ReceiverPlan receiverPlan,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IMethodSymbol? callee,
        SemanticModel model,
        Span<ExpressionEffects> effects)
    {
        var offset = 0;
        if (receiverPlan.HasSequencedOperand)
        {
            if (receiverPlan.Form == ReceiverForm.Address && receiverPlan.Expression is { } addrExpr)
            {
                effects[0] = !NeedsConversion(addrExpr, model) &&
                             RefSyntaxHelper.NeedsAddressOf(addrExpr, model)
                    ? ExpressionEffects.OfLocation(addrExpr, model)
                    : ExpressionEffects.Of(addrExpr, model);
            }
            else if (receiverPlan.Expression is { } valueExpr)
            {
                effects[0] = ExpressionEffects.Of(valueExpr, model);
            }
            else
            {
                // A copied implicit this reads memory (this) before evaluating any arguments.
                effects[0] = new ExpressionEffects(new VariableSet { Memory = true }, default);
            }

            offset = 1;
        }

        for (var i = 0; i < arguments.Count; i++)
        {
            // A converted rvalue or non-ref property passed to an in parameter is a value, copied when it is evaluated
            var parameter = callee?.FindParameter(arguments, i);
            var argument = IsPassedAsLocation(arguments[i], parameter, model)
                ? ExpressionEffects.OfLocation(arguments[i].Expression, model)
                : ExpressionEffects.Of(arguments[i].Expression, model);

            // The null check of a non-nullable array argument (ArgumentNullCheck.PlanArgument) is written around the
            // argument, or around its temporary after every argument when the arguments are sequenced
            effects[i + offset] = argument.WithThrow(
                parameter != null && ArgumentNullCheck.ChecksArgument(arguments[i].Expression, parameter, model));
        }
    }

    private static bool TryDetermineLastSpill(
        ReadOnlySpan<ExpressionEffects> effects,
        ExpressionSyntax? instanceReceiver,
        bool spillAll,
        SemanticModel model,
        out int last)
    {
        last = spillAll ? effects.Length - 1 : LastSpilledOperand(effects);
        if (last < 0 && !spillAll)
        {
            return false;
        }

        return instanceReceiver == null || spillAll || !InterferesWithReceiver(instanceReceiver, effects, last, model);
    }

    private static int[] RentTemps(int count)
    {
        var temps = ArrayPool<int>.Shared.Rent(Math.Max(1, count));
        temps.AsSpan(0, count).Fill(-1);
        return temps;
    }

    private static int DeclareSequencedOperands(
        ref TranspileContext context,
        in ReceiverPlan receiverPlan,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IMethodSymbol? callee,
        ReadOnlySpan<ExpressionEffects> effects,
        int last,
        bool spillAll,
        bool isCall,
        Span<int> temps)
    {
        var receiverTemp = -1;
        var hasReceiver = receiverPlan.HasSequencedOperand;
        if (hasReceiver && !effects[0].IsEmpty)
        {
            receiverTemp = DeclareReceiver(ref context, in receiverPlan);
        }

        var offset = hasReceiver ? 1 : 0;
        var maxArgIndex = last - offset;
        for (var i = 0; i <= maxArgIndex && i < arguments.Count; i++)
        {
            if (spillAll || !effects[i + offset].IsEmpty)
            {
                temps[i] = DeclareArgument(ref context, arguments[i], callee?.FindParameter(arguments, i), isCall);
            }
        }

        return receiverTemp;
    }

    private static int DeclareReceiver(ref TranspileContext context, in ReceiverPlan receiverPlan)
    {
        var writer = context.Emitter.Writer;
        var model = context.Query.SemanticModel;

        switch (receiverPlan.Form)
        {
            case ReceiverForm.Address:
                if (receiverPlan.Expression is { } addrExpr &&
                    ((NeedsConversion(addrExpr, model) && !StackArrayLocals.IsStackArrayReference(addrExpr, model)) ||
                     !RefSyntaxHelper.NeedsAddressOf(addrExpr, model)))
                {
                    writer.Append("auto");
                }
                else
                {
                    writer.Append("auto&&");
                }

                var addrTemp = AppendDeclaredName(ref context);
                if (receiverPlan.Expression != null)
                {
                    context.Visitor.Visit(receiverPlan.Expression);
                }

                writer.AppendLine(";");
                return addrTemp;

            case ReceiverForm.Copy:
                writer.Append("auto");
                var copyTemp = AppendDeclaredName(ref context);
                if (receiverPlan.CopyType is { } copyType)
                {
                    context.Emitter.Dialect.AppendType(ref context, copyType);
                    using (writer.Parentheses())
                    {
                        AppendCopySource(in receiverPlan, ref context);
                    }
                }
                else
                {
                    AppendCopySource(in receiverPlan, ref context);
                }

                writer.AppendLine(";");
                return copyTemp;

            case ReceiverForm.Temporary:
                writer.Append(
                    receiverPlan.IsExtensionReceiver &&
                    receiverPlan.Expression is { } extExpr &&
                    !NeedsConversion(extExpr, model) &&
                    !RefSyntaxHelper.NeedsTemporaryValueCopy(extExpr, model)
                        ? "auto&&"
                        : "auto");
                var temp = AppendDeclaredName(ref context);
                if (receiverPlan.Expression != null)
                {
                    context.Visitor.Visit(receiverPlan.Expression);
                }

                writer.AppendLine(";");
                return temp;

            case ReceiverForm.Value:
            default:
                if (receiverPlan.Expression is { } valExpr &&
                    StackArrayLocals.IsStackArrayReference(valExpr, model))
                {
                    writer.Append("auto&&");
                }
                else if (receiverPlan.ValueType == null ||
                         receiverPlan.ValueType is IPointerTypeSymbol ||
                         !TypeMapping.HasDirectCppType(receiverPlan.ValueType))
                {
                    writer.Append("auto");
                }
                else
                {
                    context.Emitter.Dialect.AppendType(ref context, receiverPlan.ValueType);
                }

                var valueTemp = AppendDeclaredName(ref context);
                if (receiverPlan.Expression != null)
                {
                    context.Visitor.Visit(receiverPlan.Expression);
                }

                writer.AppendLine(";");
                return valueTemp;
        }
    }

    private static void AppendCopySource(in ReceiverPlan receiverPlan, ref TranspileContext context)
    {
        if (receiverPlan.IsSelf)
        {
            Rules.ExpressionRules.ThisExpressionTranspilerRule.AppendSelf(ref context);
        }
        else if (receiverPlan.Expression != null)
        {
            context.Visitor.Visit(receiverPlan.Expression);
        }
    }

    // __gsharp_temp_mutable_address(T&&) requires a C++ prvalue: MSVC without /permissive- deduces T=S& for a member
    // access on a prvalue (MakeHolder(10).Rw), and operator[] returns S& for element accesses (MakeBuf(10)[0]).
    // Direct prvalues like Make(7) stay unwrapped so __gsharp_temp_mutable_address(Make(7)) is preserved.
    private static bool NeedsMutableAddressCopy(ExpressionSyntax expression, SemanticModel model)
    {
        if (RefSyntaxHelper.NeedsTemporaryValueCopy(expression, model))
        {
            return true;
        }

        var current = expression.UnwrapParentheses();
        while (current is CheckedExpressionSyntax checkedExpression)
        {
            current = checkedExpression.Expression.UnwrapParentheses();
        }

        return current is MemberAccessExpressionSyntax or ElementAccessExpressionSyntax;
    }

    // Whether an argument up to last interferes with the receiver, which the lambda would evaluate after it
    private static bool InterferesWithReceiver(ExpressionSyntax receiver, ReadOnlySpan<ExpressionEffects> effects, int last,
        SemanticModel model)
    {
        var receiverEffects = ExpressionEffects.OfLocation(receiver, model);
        for (var i = 0; i <= last && i < effects.Length; i++)
        {
            if (!effects[i].IsEmpty && receiverEffects.InterferesWith(effects[i]))
            {
                return true;
            }
        }

        return false;
    }

    // <P> __gsharp_seq_<n> = <argument>; where <P> is the parameter type (a value is converted when it is evaluated, as
    // in C#), auto for a pointer or a type parameter, auto&& for an argument passed by reference (its location; a
    // converted rvalue is copied into the parameter type instead)
    private static int DeclareArgument(ref TranspileContext context, ArgumentSyntax argument, IParameterSymbol? parameter,
        bool isCall)
    {
        var writer = context.Emitter.Writer;
        var byReference = IsByReference(argument, parameter);
        if (byReference && (!isCall || IsPassedAsLocation(argument, parameter, context.Query.SemanticModel)))
        {
            writer.Append("auto&&");
        }
        else if (parameter == null || parameter.Type is IPointerTypeSymbol ||
                 !TypeMapping.HasDirectCppType(parameter.Type))
        {
            writer.Append("auto");
        }
        else
        {
            context.Emitter.Dialect.AppendType(ref context, parameter.Type);
        }

        var temp = AppendDeclaredName(ref context);
        if (isCall && byReference)
        {
            context.Visitor.Visit(argument.Expression);
        }
        else
        {
            context.Visitor.Visit(argument);
        }

        writer.AppendLine(";");
        return temp;
    }

    // Passed by reference: written with ref / out / in, or to an in / ref readonly parameter, which C# also accepts
    // without a modifier
    private static bool IsByReference(ArgumentSyntax argument, IParameterSymbol? parameter) =>
        argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword, SyntaxKind.OutKeyword, SyntaxKind.InKeyword) ||
        parameter?.RefKind is RefKind.In or RefKind.RefReadOnlyParameter;

    private static bool IsPassedAsLocation(ArgumentSyntax argument, IParameterSymbol? parameter, SemanticModel model) =>
        IsByReference(argument, parameter) &&
        !NeedsConversion(argument.Expression, model) &&
        !RefSyntaxHelper.NeedsTemporaryValueCopy(argument.Expression, model);

    private static bool NeedsConversion(ExpressionSyntax expression, SemanticModel model)
    {
        if (!model.TryGetOriginalNode(expression, out var original, out var originalModel))
        {
            return false;
        }

        var typeInfo = originalModel.GetTypeInfo(original);
        return typeInfo.Type != null && typeInfo.ConvertedType != null && !typeInfo.Type.IsEqualTo(typeInfo.ConvertedType);
    }

    /// <summary>The lambda of a sequence: <c>([&amp;]() {</c> ... <c>return &lt;value&gt;; }())</c>.</summary>
    /// <remarks>The default value writes nothing, for a form that may or may not need the sequence.</remarks>
    public readonly ref struct SequenceScope
    {
        private readonly SourceWriter _writer;
        private readonly SourceWriterImmediatelyInvokedLambdaScope _lambda;
        private readonly bool _isOpen;

        public SequenceScope(SourceWriter writer, string? returnType = null)
        {
            _writer = writer;
            _isOpen = true;
            _lambda = writer.ImmediatelyInvokedLambda(returnType);
        }

        /// <summary>Starts the value of the sequence, after its temporaries.</summary>
        public void Return()
        {
            if (_isOpen)
            {
                _writer.Append("return ");
            }
        }

        public void Dispose()
        {
            if (!_isOpen)
            {
                return;
            }

            _writer.AppendLine(";");
            _lambda.Dispose();
        }
    }

    /// <summary>
    /// The arguments of a call started by <see cref="BeginCall"/>: either as written (no temporaries) or the
    /// temporaries they were evaluated into. Disposing it closes the lambda.
    /// </summary>
    public readonly ref struct CallSequence
    {
        private readonly SourceWriter _writer;
        private readonly SourceWriterImmediatelyInvokedLambdaScope _lambda;
        private readonly SeparatedSyntaxList<ArgumentSyntax> _arguments;

        // The temporary of each written argument (-1: in place), rented from ArrayPool
        private readonly int[]? _temps;

        // The temporary of the extension receiver (-1: in place or no receiver)
        private readonly int _receiverTemp;

        // A brace initialization T{...} rather than a call: no argument is passed by address. False in the default
        // value, which emits the arguments of a call as written.
        private readonly bool _isBraces;

        public CallSequence(SourceWriter writer, SourceWriterImmediatelyInvokedLambdaScope lambda,
            SeparatedSyntaxList<ArgumentSyntax> arguments, int[] temps, int receiverTemp, bool isCall)
        {
            _writer = writer;
            _lambda = lambda;
            _arguments = arguments;
            _temps = temps;
            _receiverTemp = receiverTemp;
            _isBraces = !isCall;
        }

        public CallSequence(SourceWriter writer, SeparatedSyntaxList<ArgumentSyntax> arguments, int[] temps,
            int receiverTemp, bool isCall, string? returnType)
            : this(writer, writer.ImmediatelyInvokedLambda(returnType), arguments, temps, receiverTemp, isCall)
        {
        }

        public CallSequence(SourceWriter writer, SeparatedSyntaxList<ArgumentSyntax> arguments, int[] temps, bool isCall,
            string? returnType)
            : this(writer, arguments, temps, receiverTemp: -1, isCall, returnType)
        {
        }

        /// <summary>Whether the call is emitted inside a lambda with temporaries.</summary>
        public bool IsSequenced => _temps != null;

        /// <summary>
        /// Writes <paramref name="receiver"/> as the first argument of the static C++ function for an extension call:
        /// its temporary (by address when <paramref name="byReference"/> is <see langword="true"/>), or the receiver
        /// itself as written.
        /// </summary>
        public void AppendReceiver(ExpressionSyntax receiver, bool byReference, ref TranspileContext context)
        {
            var plan = new ReceiverPlan(
                byReference ? ReceiverForm.Address : ReceiverForm.Value,
                receiver,
                default,
                CopyType: null,
                ReadOnlyCallee: !byReference,
                IsExtensionReceiver: true);
            AppendReceiver(in plan, ref context);
        }

        /// <summary>
        /// Writes the receiver described by <paramref name="receiver"/> as the first argument of the static C++
        /// function for a struct instance device call or a reduced extension call.
        /// </summary>
        public void AppendReceiver(in ReceiverPlan receiver, ref TranspileContext context)
        {
            if (_temps != null && _receiverTemp >= 0)
            {
                var emitAddress = receiver.Form != ReceiverForm.Value &&
                                  (receiver.Form is ReceiverForm.Copy or ReceiverForm.Temporary ||
                                   receiver.Expression == null ||
                                   RefSyntaxHelper.NeedsAddressOf(receiver.Expression, context.Query.SemanticModel));
                context.Emitter.Writer.AppendIf(emitAddress, "&");
                AppendTempName(context.Emitter.Writer, _receiverTemp);
                return;
            }

            switch (receiver.Form)
            {
                case ReceiverForm.Address:
                    if (receiver.Expression != null)
                    {
                        RefSyntaxHelper.TranspileAsByRefArgument(ref context, receiver.Expression);
                    }

                    break;

                case ReceiverForm.Copy:
                    context.Emitter.Writer.Append(GSharpInternalNames.Prelude.TempMutableAddress);
                    using (context.Emitter.Writer.Parentheses())
                    {
                        if (receiver.CopyType is { } copyType)
                        {
                            context.Emitter.Dialect.AppendType(ref context, copyType);
                            using (context.Emitter.Writer.Parentheses())
                            {
                                AppendCopySource(in receiver, ref context);
                            }
                        }
                        else
                        {
                            AppendCopySource(in receiver, ref context);
                        }
                    }

                    break;

                case ReceiverForm.Temporary:
                    if (receiver.ReadOnlyCallee)
                    {
                        if (receiver.Expression is { } readOnlyTempExpr)
                        {
                            // [WHY] Non-variable [InlineArray] elements (and member chains on them) classify as
                            // CppLocation.Storage in ClassifyElementAccess so ordinary in-arguments keep their shape;
                            // for a ReceiverForm.Temporary receiver, force __gsharp_temp_address<T>(static_cast<T>(...))
                            // when NeedsTemporaryValueCopy is true so the callee cannot alias underlying storage.
                            var model = context.Query.SemanticModel;
                            if (RefSyntaxHelper.NeedsAddressOf(readOnlyTempExpr, model) &&
                                RefSyntaxHelper.NeedsTemporaryValueCopy(readOnlyTempExpr, model))
                            {
                                RefSyntaxHelper.AppendTempAddress(ref context, readOnlyTempExpr, model,
                                    forceValueCopy: true);
                            }
                            else
                            {
                                RefSyntaxHelper.TranspileAsByRefArgument(ref context, readOnlyTempExpr);
                            }
                        }

                        break;
                    }

                    context.Emitter.Writer.Append(GSharpInternalNames.Prelude.TempMutableAddress);
                    using (context.Emitter.Writer.Parentheses())
                    {
                        if (receiver.Expression is { } tempExpr)
                        {
                            var needsCopy = receiver.CopyType != null &&
                                            NeedsMutableAddressCopy(tempExpr, context.Query.SemanticModel);
                            if (needsCopy && receiver.CopyType is { } tempCopyType)
                            {
                                context.Emitter.Dialect.AppendType(ref context, tempCopyType);
                                using (context.Emitter.Writer.Parentheses())
                                {
                                    context.Visitor.Visit(tempExpr);
                                }
                            }
                            else
                            {
                                context.Visitor.Visit(tempExpr);
                            }
                        }
                    }

                    break;

                case ReceiverForm.Self:
                    switch (receiver.SelfForm)
                    {
                        case SelfForm.ReceiverParameter:
                            context.Emitter.Writer.Append(GSharpInternalNames.Lowering.ReceiverParameter);
                            break;
                        case SelfForm.ConstructorSelf:
                            context.Emitter.Writer.Append($"&{GSharpInternalNames.Lowering.ConstructorSelf}");
                            break;
                        default:
                            context.Emitter.Writer.Append("this");
                            break;
                    }

                    break;

                case ReceiverForm.Value:
                default:
                    if (receiver.Expression != null)
                    {
                        context.Visitor.Visit(receiver.Expression);
                    }

                    break;
            }
        }

        /// <summary>
        /// Writes <paramref name="argument"/> as the argument of <paramref name="parameter"/>: its temporary (by
        /// address when it is passed by reference to a call), or the argument itself as the call rules write it.
        /// </summary>
        public void AppendArgument(ArgumentSyntax argument, IParameterSymbol? parameter, ref TranspileContext context,
            bool convertToParameter = false)
        {
            var byReference = IsByReference(argument, parameter);
            if (TryGetTemp(argument, out var temp))
            {
                context.Emitter.Writer.AppendIf(
                    !_isBraces && byReference &&
                    RefSyntaxHelper.NeedsAddressOf(argument.Expression, context.Query.SemanticModel),
                    "&");
                AppendTempName(context.Emitter.Writer, temp);
                return;
            }

            if (!_isBraces && parameter?.RefKind is RefKind.In or RefKind.RefReadOnlyParameter)
            {
                // [WHY] An in or ref readonly parameter is a pointer in C++, as a ref parameter is, and C# accepts its
                // argument with in, ref or no modifier; the C++ argument is the address whichever was written.
                RefSyntaxHelper.TranspileAsByRefArgument(ref context, argument.Expression);
                return;
            }

            // [WHY] Also in a brace initialization, which rejects a narrowing conversion that C# makes implicitly (#897)
            if (convertToParameter && !byReference && parameter?.Type is { } parameterType)
            {
                ArithmeticConversions.AppendArgument(argument, parameterType, ref context);
                return;
            }

            context.Visitor.Visit(argument);
        }

        private bool TryGetTemp(ArgumentSyntax argument, out int temp)
        {
            temp = -1;
            if (_temps == null)
            {
                return false;
            }

            var index = _arguments.IndexOf(argument);
            if (index < 0)
            {
                return false;
            }

            temp = _temps[index];
            return temp >= 0;
        }

        public void Dispose()
        {
            if (_temps == null)
            {
                return;
            }

            _writer.AppendLine(";");
            _lambda.Dispose();
            ArrayPool<int>.Shared.Return(_temps);
        }
    }
}

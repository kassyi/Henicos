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
/// managed output) stays as written. See <c>tmp/p3-lanes/608-evaluation-order.md</c>.
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
    /// initializer such as <c>{}</c> needs a type), except a type parameter or an enum, which keep <c>auto</c>.
    /// </summary>
    public static int DeclareTemp(ref TranspileContext context, ExpressionSyntax expression, bool reference,
        ITypeSymbol? type = null)
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
        context.Emitter.Writer.AppendLine(";");
        return temp;
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
        BeginCallCore(ref context, extensionReceiver: null, receiverParameter: null, arguments, callee, spillAll, isCall, instanceReceiver: receiver);

    /// <summary>
    /// Starts a call in reduced extension form (<c>receiver.M(arguments)</c>) that C++ emits as a static call
    /// (<c>M(receiver, arguments)</c>): when the receiver and arguments (or two arguments) interfere, writes the lambda
    /// and the temporaries in the C# evaluation order (receiver first, then arguments left-to-right) and <c>return </c>.
    /// The caller then writes the call, emitting the receiver with <see cref="CallSequence.AppendReceiver"/> and each
    /// argument with <see cref="CallSequence.AppendArgument"/>.
    /// </summary>
    public static CallSequence BeginExtensionCall(
        ref TranspileContext context,
        ExpressionSyntax? receiver,
        IParameterSymbol? receiverParameter,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IMethodSymbol? callee) =>
        receiver == null
            ? BeginCall(ref context, arguments, callee, receiver: null, spillAll: false)
            : BeginCallCore(ref context, extensionReceiver: receiver, receiverParameter, arguments, callee, spillAll: false, isCall: true, instanceReceiver: null);

    private static CallSequence BeginCallCore(
        ref TranspileContext context,
        ExpressionSyntax? extensionReceiver,
        IParameterSymbol? receiverParameter,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IMethodSymbol? callee,
        bool spillAll,
        bool isCall,
        ExpressionSyntax? instanceReceiver)
    {
        var count = arguments.Count;
        var total = extensionReceiver != null ? count + 1 : count;
        if (total < 2 && !spillAll)
        {
            return default;
        }

        var model = context.Query.SemanticModel;
        var effects = ArrayPool<ExpressionEffects>.Shared.Rent(total);
        try
        {
            var effectsSpan = effects.AsSpan(0, total);
            CollectCallEffects(extensionReceiver, receiverParameter, arguments, callee, model, effectsSpan, out var byReference);
            if (!TryDetermineLastSpill(effectsSpan, instanceReceiver, spillAll, model, out var last))
            {
                return default;
            }

            var temps = RentTemps(count);
            var lambda = context.Emitter.Writer.ImmediatelyInvokedLambda(GetReturnType(callee));
            var receiverTemp = DeclareSequencedOperands(ref context, extensionReceiver, receiverParameter, byReference,
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
        ExpressionSyntax? extensionReceiver,
        IParameterSymbol? receiverParameter,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IMethodSymbol? callee,
        SemanticModel model,
        Span<ExpressionEffects> effects,
        out bool receiverByReference)
    {
        var offset = 0;
        receiverByReference = false;
        if (extensionReceiver != null)
        {
            receiverByReference = receiverParameter is { RefKind: not RefKind.None };
            effects[0] = receiverByReference && !NeedsConversion(extensionReceiver, model)
                ? ExpressionEffects.OfLocation(extensionReceiver, model)
                : ExpressionEffects.Of(extensionReceiver, model);
            offset = 1;
        }

        for (var i = 0; i < arguments.Count; i++)
        {
            effects[i + offset] = IsByReference(arguments[i], callee?.FindParameter(arguments, i)) &&
                                  !NeedsConversion(arguments[i].Expression, model)
                ? ExpressionEffects.OfLocation(arguments[i].Expression, model)
                : ExpressionEffects.Of(arguments[i].Expression, model);
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
        ExpressionSyntax? extensionReceiver,
        IParameterSymbol? receiverParameter,
        bool receiverByReference,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IMethodSymbol? callee,
        ReadOnlySpan<ExpressionEffects> effects,
        int last,
        bool spillAll,
        bool isCall,
        Span<int> temps)
    {
        var receiverTemp = -1;
        var hasExtensionReceiver = extensionReceiver != null;
        if (hasExtensionReceiver && !effects[0].IsEmpty)
        {
            receiverTemp = DeclareReceiver(ref context, extensionReceiver, receiverParameter, receiverByReference);
        }

        var offset = hasExtensionReceiver ? 1 : 0;
        var maxArgIndex = hasExtensionReceiver ? last - 1 : last;
        for (var i = 0; i <= maxArgIndex && i < arguments.Count; i++)
        {
            if (spillAll || !effects[i + offset].IsEmpty)
            {
                temps[i] = DeclareArgument(ref context, arguments[i], callee?.FindParameter(arguments, i), isCall);
            }
        }

        return receiverTemp;
    }

    private static int DeclareReceiver(
        ref TranspileContext context,
        ExpressionSyntax receiver,
        IParameterSymbol? receiverParameter,
        bool byReference)
    {
        var writer = context.Emitter.Writer;
        var model = context.Query.SemanticModel;
        if ((byReference && !NeedsConversion(receiver, model)) || StackArrayLocals.IsStackArrayReference(receiver, model))
        {
            writer.Append("auto&&");
        }
        else if (receiverParameter == null || receiverParameter.Type is IPointerTypeSymbol ||
                 !TypeMapping.HasDirectCppType(receiverParameter.Type))
        {
            writer.Append("auto");
        }
        else
        {
            context.Emitter.Dialect.AppendType(ref context, receiverParameter.Type);
        }

        var temp = AppendDeclaredName(ref context);
        context.Visitor.Visit(receiver);
        writer.AppendLine(";");
        return temp;
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
        if (byReference && !(isCall && NeedsConversion(argument.Expression, context.Query.SemanticModel)))
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

    private static bool NeedsConversion(ExpressionSyntax expression, SemanticModel model)
    {
        if (model.GetOriginalNode(expression) is not { } original)
        {
            return false;
        }

        var typeInfo = model.GetTypeInfo(original);
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
            if (_temps != null && _receiverTemp >= 0)
            {
                context.Emitter.Writer.AppendIf(
                    byReference && RefSyntaxHelper.NeedsAddressOf(receiver, context.Query.SemanticModel),
                    "&");
                AppendTempName(context.Emitter.Writer, _receiverTemp);
                return;
            }

            if (byReference)
            {
                RefSyntaxHelper.TranspileAsByRefArgument(ref context, receiver);
                return;
            }

            context.Visitor.Visit(receiver);
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

            if (convertToParameter && !_isBraces && !byReference && parameter?.Type is { } parameterType)
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

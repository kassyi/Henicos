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
        if (!reference && type is not (null or ITypeParameterSymbol or { TypeKind: TypeKind.Enum or TypeKind.Error }) &&
            !IsOrContainsTypeParameter(type))
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
        IMethodSymbol? callee, ExpressionSyntax? receiver, bool spillAll, bool isCall = true)
    {
        var count = arguments.Count;
        if (count < 2 && !spillAll)
        {
            return default;
        }

        var model = context.Query.SemanticModel;
        var effects = ArrayPool<ExpressionEffects>.Shared.Rent(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                // A converted rvalue or non-ref property passed to an in parameter is a value, copied when it is evaluated
                effects[i] = IsPassedAsLocation(arguments[i], callee?.FindParameter(arguments, i), model)
                    ? ExpressionEffects.OfLocation(arguments[i].Expression, model)
                    : ExpressionEffects.Of(arguments[i].Expression, model);
            }

            var last = spillAll ? count - 1 : LastSpilledOperand(effects.AsSpan(0, count));
            if ((last < 0 && !spillAll) || (receiver != null && !spillAll && InterferesWithReceiver(receiver, effects, last, model)))
            {
                return default;
            }

            // CallSequence.Dispose closes the lambda and returns the temporaries to the pool
            var temps = ArrayPool<int>.Shared.Rent(count);
            temps.AsSpan(0, count).Fill(-1);
            var sequence = new CallSequence(context.Emitter.Writer, arguments, temps, isCall, GetReturnType(callee));
            for (var i = 0; i <= last; i++)
            {
                if (spillAll || !effects[i].IsEmpty)
                {
                    temps[i] = DeclareArgument(ref context, arguments[i], callee?.FindParameter(arguments, i), isCall);
                }
            }

            context.Emitter.Writer.Append("return ");
            return sequence;
        }
        finally
        {
            ArrayPool<ExpressionEffects>.Shared.Return(effects, clearArray: true);
        }
    }

    // Whether an argument up to last interferes with the receiver, which the lambda would evaluate after it
    private static bool InterferesWithReceiver(ExpressionSyntax receiver, ExpressionEffects[] effects, int last,
        SemanticModel model)
    {
        var receiverEffects = ExpressionEffects.OfLocation(receiver, model);
        for (var i = 0; i <= last; i++)
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
        else if (parameter == null || parameter.Type is IPointerTypeSymbol || IsOrContainsTypeParameter(parameter.Type))
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
        !RefSyntaxHelper.IsAutoPropertyOrMemberOfAutoProperty(argument.Expression, model);

    private static bool NeedsConversion(ExpressionSyntax expression, SemanticModel model)
    {
        if (model.GetOriginalNode(expression) is not { } original)
        {
            return false;
        }

        var typeInfo = model.GetTypeInfo(original);
        return typeInfo.Type != null && typeInfo.ConvertedType != null && !typeInfo.Type.IsEqualTo(typeInfo.ConvertedType);
    }

    private static bool IsOrContainsTypeParameter(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => IsOrContainsTypeParameter(array.ElementType),
        IPointerTypeSymbol pointer => IsOrContainsTypeParameter(pointer.PointedAtType),
        INamedTypeSymbol named => named.TypeArguments
            .AsStackEnumerable()
            .Any(static argument => IsOrContainsTypeParameter(argument)),
        _ => false
    };

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

        // The temporary of each written argument (-1: in place), rented from ArrayPool and longer than the arguments
        private readonly int[]? _temps;

        // A brace initialization T{...} rather than a call: no argument is passed by address. False in the default
        // value, which emits the arguments of a call as written.
        private readonly bool _isBraces;

        public CallSequence(SourceWriter writer, SeparatedSyntaxList<ArgumentSyntax> arguments, int[] temps, bool isCall,
            string? returnType)
        {
            _writer = writer;
            _arguments = arguments;
            _temps = temps;
            _isBraces = !isCall;
            _lambda = writer.ImmediatelyInvokedLambda(returnType);
        }

        /// <summary>Whether the call is emitted inside a lambda with temporaries.</summary>
        public bool IsSequenced => _temps != null;

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

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// The null check of a C# array read (#706): <c>Length</c> and the other <c>System.Array</c> members, an element and
/// <c>foreach</c> throw <see cref="NullReferenceException"/> on a null array in C#. The receiver goes through the Prelude
/// helper <c>__gsharp_array_ref(&lt;receiver&gt;, "&lt;message&gt;")</c>, which fails on the native CPU build always and
/// on CUDA under <c>GSHARP_CHECKED</c>; a null optional kernel argument reads as an empty array there
/// (<c>GSharpArray::flags</c>, set by the launchers).
/// </summary>
/// <remarks>
/// <para>
/// Only a receiver of a C# array type is checked: a span is never null in C#, and <c>is null</c>, <c>?.</c>,
/// <c>??</c>, a conversion to a span or passing the array along read nothing.
/// </para>
/// <para>
/// No check is emitted where the receiver cannot be null, as RyuJIT drops a null check that an assertion proves
/// (symbol-based only): a stack array local, and an array parameter of a kernel that the kernel never writes (the
/// launcher rejects a null non-optional array, and a null optional one reads as empty anyway), and a conditional whose
/// branches are both such receivers.
/// </para>
/// </remarks>
internal static class ArrayNullCheck
{
    /// <summary>The text of the error, followed by the place of the receiver (<see cref="SourcePlace"/>).</summary>
    public const string MessagePrefix = "G#: a null array is read at ";

    private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<IParameterSymbol, bool>> s_written =
        new();

    /// <summary>Whether a read through <paramref name="receiver"/> needs the null check.</summary>
    public static bool NeedsCheck(ExpressionSyntax receiver, SemanticModel semanticModel) =>
        semanticModel.GetTypeSymbol(receiver) is IArrayTypeSymbol && MayBeNull(receiver, semanticModel);

    private static bool MayBeNull(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        expression = SkipParentheses(expression);
        if (StackArrayLocals.IsStackArrayReference(expression, semanticModel))
        {
            return false;
        }

        // c ? a : b is null only when the branch it takes is (a null literal branch may be)
        if (expression is ConditionalExpressionSyntax conditional)
        {
            return MayBeNull(conditional.WhenTrue, semanticModel) || MayBeNull(conditional.WhenFalse, semanticModel);
        }

        return semanticModel.GetSymbol(expression) switch
        {
            IParameterSymbol parameter => !IsUnwrittenKernelParameter(parameter, semanticModel.Compilation),
            // A host static member, which GSH0065 reports
            IFieldSymbol { IsStatic: true } => false,
            _ => true
        };
    }

    /// <summary>
    /// Writes <paramref name="receiver"/> for a read: <c>__gsharp_array_ref(&lt;receiver&gt;, "&lt;message&gt;")</c> when
    /// it needs the check (<see cref="NeedsCheck"/>), otherwise the receiver as it is.
    /// </summary>
    public static void AppendReceiver(ExpressionSyntax receiver, ref TranspileContext context)
    {
        if (!NeedsCheck(receiver, context.Query.SemanticModel))
        {
            context.Visitor.Visit(receiver);
            return;
        }

        AppendCheck(receiver, SourcePlace.Format(context.Query.SemanticModel, receiver), ref context);
    }

    /// <summary>
    /// Writes the receiver of the member access <paramref name="node"/> with the check when the member reads the array:
    /// an instance member of <c>System.Array</c> (<paramref name="member"/>).
    /// </summary>
    /// <returns><see langword="false"/>, writing nothing, when the receiver needs no check.</returns>
    public static bool TryAppendMemberReceiver(MemberAccessExpressionSyntax node, ISymbol? member,
        ref TranspileContext context)
    {
        var receiver = node.Expression;
        if (!IsArrayMember(member) || !NeedsCheck(receiver, context.Query.SemanticModel))
        {
            return false;
        }

        AppendCheck(receiver, SourcePlace.Format(context.Query.SemanticModel, receiver), ref context);
        return true;
    }

    /// <summary>
    /// Writes the element access <paramref name="node"/> with the check when its receiver needs one:
    /// <c>__gsharp_array_at(&lt;receiver&gt;, &lt;index&gt;, "&lt;message&gt;")</c>, the element as a location.
    /// </summary>
    /// <returns><see langword="false"/>, writing nothing, when the access needs no check.</returns>
    /// <remarks>
    /// <para>
    /// C# evaluates the array, then the index, and only then throws for a null array (ldelem / stelem), so the index is
    /// evaluated before the check: <c>h.Data[Bump(o)]</c> runs <c>Bump</c> first. A check on the receiver alone
    /// (<c>__gsharp_array_ref(a)[i]</c>) would run before the index, since C++17 sequences <c>E1</c> before <c>E2</c>.
    /// The call leaves the order of its arguments unspecified, so when the receiver and the index interfere they are
    /// evaluated in C# order into temporaries first (<see cref="EvaluationOrder"/>):
    /// <c>([&amp;]() -&gt; decltype(auto) { auto __gsharp_seq_0 = &lt;receiver&gt;; auto __gsharp_seq_1 = &lt;index&gt;; return __gsharp_array_at(__gsharp_seq_0, __gsharp_seq_1, "&lt;message&gt;"); }())</c>.
    /// </para>
    /// <para>
    /// A range (<c>a[1..]</c>) and an index from the end (<c>a[^1]</c>) are left out: .NET throws
    /// <see cref="ArgumentNullException"/> for a range, which needs an error code of its own (#871).
    /// </para>
    /// </remarks>
    public static bool TryAppendCheckedElement(ElementAccessExpressionSyntax node, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        if (node.ArgumentList.Arguments is not [var argument] ||
            IsIndexOrRange(argument.Expression, model) ||
            !NeedsCheck(node.Expression, model))
        {
            return false;
        }

        var place = SourcePlace.Format(model, node.Expression);
        var writer = context.Emitter.Writer;
        if (EvaluationOrder.LastSpilledOperand(
                [ExpressionEffects.Of(node.Expression, model), ExpressionEffects.Of(argument.Expression, model)]) < 0)
        {
            AppendElementCall(node.Expression, argument.Expression, place, ref context);
            return true;
        }

        using var sequence = EvaluationOrder.BeginSequence(writer, "decltype(auto)");
        var receiver = EvaluationOrder.DeclareTemp(ref context, node.Expression, reference: false);
        var index = EvaluationOrder.DeclareTemp(ref context, argument.Expression, reference: false);
        sequence.Return();
        writer.Append(GSharpInternalNames.Prelude.ArrayAt);
        using (writer.Parentheses())
        {
            EvaluationOrder.AppendTempName(writer, receiver);
            writer.Append(", ");
            EvaluationOrder.AppendTempName(writer, index);
            writer.Append(", ");
            CppStringLiteral.Append(writer, MessagePrefix + place);
        }

        return true;
    }

    // __gsharp_array_at(<receiver>, <index>, "G#: a null array is read at <place>")
    private static void AppendElementCall(ExpressionSyntax receiver, ExpressionSyntax index, string place,
        ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        writer.Append(GSharpInternalNames.Prelude.ArrayAt);
        using (writer.Parentheses())
        {
            context.Visitor.Visit(receiver);
            writer.Append(", ");
            context.Visitor.Visit(index);
            writer.Append(", ");
            CppStringLiteral.Append(writer, MessagePrefix + place);
        }
    }

    /// <summary>
    /// Writes <paramref name="expression"/> through the check when lowering marked it
    /// (<see cref="LoweringAnnotations.ArrayNullCheckAnnotationKind"/>, the source of a lowered <c>foreach</c>), with the
    /// place the annotation carries.
    /// </summary>
    /// <returns><see langword="false"/>, writing nothing, when the expression carries no such annotation.</returns>
    public static bool TryAppendAnnotatedCheck(ExpressionSyntax expression, ref TranspileContext context)
    {
        if (!expression.HasAnnotations(LoweringAnnotations.ArrayNullCheckAnnotationKind))
        {
            return false;
        }

        foreach (var annotation in expression.GetAnnotations(LoweringAnnotations.ArrayNullCheckAnnotationKind))
        {
            if (annotation.Data is { } place)
            {
                // The expression itself, without the annotation that brought it here
                AppendCheck(expression.WithoutAnnotations(LoweringAnnotations.ArrayNullCheckAnnotationKind), place,
                    ref context);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Writes <c>__gsharp_array_ref(&lt;receiver&gt;, "G#: a null array is read at &lt;place&gt;")</c>.
    /// </summary>
    public static void AppendCheck(ExpressionSyntax receiver, string place, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        writer.Append(GSharpInternalNames.Prelude.ArrayRef);
        using (writer.Parentheses())
        {
            context.Visitor.Visit(receiver);
            writer.Append(", ");
            CppStringLiteral.Append(writer, MessagePrefix + place);
        }
    }

    // a[^1] and a[1..]: an argument of type System.Index or System.Range
    private static bool IsIndexOrRange(ExpressionSyntax argument, SemanticModel model) =>
        argument is RangeExpressionSyntax ||
        model.GetTypeSymbol(argument) is INamedTypeSymbol { Name: "Index" or "Range" } type &&
        type.ContainingNamespace.IsSystemNamespace();

    // An instance member of System.Array: Length, LongLength, Rank, GetLength, ... (not an extension method such as AsSpan)
    private static bool IsArrayMember(ISymbol? member) =>
        member is { IsStatic: false, ContainingType.SpecialType: SpecialType.System_Array } &&
        member is not IMethodSymbol { MethodKind: MethodKind.ReducedExtension };

    private static ExpressionSyntax SkipParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    /// <summary>
    /// Whether <paramref name="parameter"/> is a by-value parameter of a <c>[GpuKernel]</c> that the kernel never writes
    /// (an assignment, also in a tuple or a local function, a <c>ref</c> / <c>out</c> argument, a <c>ref</c> to it): it
    /// holds what the launcher passed. The managed path rewrites the <c>Length</c> of such an optional array the same way.
    /// </summary>
    public static bool IsUnwrittenKernelParameter(IParameterSymbol parameter, Compilation compilation)
    {
        if (parameter is not { RefKind: RefKind.None, ContainingSymbol: IMethodSymbol method } ||
            !method.HasAttribute(KnownAttributes.GpuKernel))
        {
            return false;
        }

        var written = s_written.GetValue(
            compilation,
            static _ => new ConcurrentDictionary<IParameterSymbol, bool>(SymbolEqualityComparer.Default)
        );
        if (!written.TryGetValue(parameter, out var isWritten))
        {
            isWritten = IsWritten(parameter, compilation);
            written.TryAdd(parameter, isWritten);
        }

        return !isWritten;
    }

    // Whether the method body writes the parameter: an assignment to it (also in a tuple), a ref or out argument, or a
    // ref to it that could write it later
    private static bool IsWritten(IParameterSymbol parameter, Compilation compilation)
    {
        foreach (var reference in parameter.ContainingSymbol.DeclaringSyntaxReferences)
        {
            var declaration = reference.GetSyntax();
            var model = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
            foreach (var node in declaration.DescendantNodes())
            {
                var written = node switch
                {
                    AssignmentExpressionSyntax assignment => NamesParameter(assignment.Left, parameter, model),
                    ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.RefKeyword or (int)SyntaxKind.OutKeyword } argument =>
                        NamesParameter(argument.Expression, parameter, model),
                    RefExpressionSyntax refExpression => NamesParameter(refExpression.Expression, parameter, model),
                    _ => false
                };
                if (written)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool NamesParameter(ExpressionSyntax target, IParameterSymbol parameter, SemanticModel model)
    {
        target = SkipParentheses(target);
        if (target is TupleExpressionSyntax tuple)
        {
            return tuple.Arguments
                .AsStackEnumerable()
                .Any((parameter, model), static (argument, state) => NamesParameter(argument.Expression, state.parameter, state.model));
        }

        return target is IdentifierNameSyntax identifier &&
               identifier.Identifier.ValueText == parameter.Name &&
               model.GetSymbol(identifier).IsEqualTo(parameter);
    }
}

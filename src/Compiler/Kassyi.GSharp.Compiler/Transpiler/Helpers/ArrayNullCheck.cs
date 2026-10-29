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
    /// an instance member of <c>System.Array</c> (<paramref name="member"/>), or the <c>Length</c> of a lowered
    /// <c>foreach</c> source whose annotation carries the place (<c>ForEachRewriter</c>).
    /// </summary>
    /// <returns><see langword="false"/>, writing nothing, when the receiver needs no check.</returns>
    public static bool TryAppendMemberReceiver(MemberAccessExpressionSyntax node, ISymbol? member,
        ref TranspileContext context)
    {
        var receiver = node.Expression;
        foreach (var annotation in receiver.GetAnnotations(LoweringAnnotations.ForEachArraySourceAnnotationKind))
        {
            if (annotation.Data is { } place)
            {
                AppendCheck(receiver, place, ref context);
                return true;
            }
        }

        if (!IsArrayMember(member) || !NeedsCheck(receiver, context.Query.SemanticModel))
        {
            return false;
        }

        AppendCheck(receiver, SourcePlace.Format(context.Query.SemanticModel, receiver), ref context);
        return true;
    }

    /// <summary>
    /// Writes the receiver of the element access <paramref name="node"/>, with the check when it needs one
    /// (<see cref="AppendReceiver"/>).
    /// </summary>
    /// <remarks>
    /// A range (<c>a[1..]</c>) is left out: .NET throws <see cref="ArgumentNullException"/> for it, which needs an error
    /// code of its own.
    /// </remarks>
    public static void AppendElementReceiver(ElementAccessExpressionSyntax node, ref TranspileContext context)
    {
        if (node.ArgumentList.Arguments
            .AsStackEnumerable()
            .Any(static argument => argument.Expression is RangeExpressionSyntax))
        {
            context.Visitor.Visit(node.Expression);
            return;
        }

        AppendReceiver(node.Expression, ref context);
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

    private static bool IsUnwrittenKernelParameter(IParameterSymbol parameter, Compilation compilation)
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

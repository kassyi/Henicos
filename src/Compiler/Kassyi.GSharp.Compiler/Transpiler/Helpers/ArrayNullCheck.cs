namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// The null check of a C# array read (#706): <c>Length</c> and the other <c>System.Array</c> members, an element and
/// <c>foreach</c> throw <see cref="NullReferenceException"/> on a null array in C#. The receiver goes through the Prelude
/// helper <c>__gsharp_array_ref(&lt;receiver&gt;, &lt;site&gt;)</c>, which fails on every build (#1409); a null optional
/// kernel argument reads as an empty array there (<c>GSharpArray::flags</c>, set by the launchers). In a kernel body the
/// place is a site of the kernel's error site table, so the host exception carries it on CUDA as well
/// (<see cref="NullCheckSite"/>).
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
    /// <summary>The text of the error of a registered site, whose place the error site table holds.</summary>
    public const string SiteMessage = "G#: a null array is read";

    /// <summary>The text of the error, followed by the place of the receiver (<see cref="SourcePlace"/>).</summary>
    public const string MessagePrefix = SiteMessage + PlaceSeparator;

    // The trap kind of the site ID, which tells the site apart from another trap at the same place
    private const string TrapKind = "ArrayNull";

    /// <summary>The text of the error of a registered site of a null passed where the parameter is not nullable.</summary>
    public const string ArgumentSiteMessage = "G#: a null array is passed where it is not nullable";

    /// <summary>The text of a null passed where a device function parameter is not nullable, followed by the place.</summary>
    public const string ArgumentMessagePrefix = ArgumentSiteMessage + PlaceSeparator;

    private const string ArgumentTrapKind = "ArrayNullArgument";

    /// <summary>The text of the error of a registered site of <c>Gpu.NotNull</c> given a null array.</summary>
    public const string NotNullSiteMessage = "G#: Gpu.NotNull is given a null array";

    private const string NotNullTrapKind = "ArrayNotNull";

    // Between the text of an error and the place it names, in a method without an error site table
    private const string PlaceSeparator = " at ";

    private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<IParameterSymbol, bool>> s_written =
        new();

    /// <summary>Whether a read through <paramref name="receiver"/> needs the null check.</summary>
    public static bool NeedsCheck(ExpressionSyntax receiver, SemanticModel semanticModel) =>
        semanticModel.GetTypeSymbol(receiver) is IArrayTypeSymbol && MayBeNull(receiver, semanticModel);

    private static bool MayBeNull(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        expression = SkipParentheses(expression);

        // c ? a : b is null only when the branch it takes is (a null literal branch may be)
        if (expression is ConditionalExpressionSyntax conditional)
        {
            return MayBeNull(conditional.WhenTrue, semanticModel) || MayBeNull(conditional.WhenFalse, semanticModel);
        }

        // A stack array (#1543), or a non-nullable array that a launch or a call has checked (#1409)
        if (NonNullProof.IsProvenNonNull(expression, semanticModel))
        {
            return false;
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

        AppendCheck(receiver, PlanSite(receiver, ref context), ref context);
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

        AppendCheck(receiver, PlanSite(receiver, ref context), ref context);
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

        var site = PlanSite(node.Expression, ref context);
        var writer = context.Emitter.Writer;
        if (EvaluationOrder.LastSpilledOperand(
                [ExpressionEffects.Of(node.Expression, model), ExpressionEffects.Of(argument.Expression, model)]) < 0)
        {
            AppendElementCall(node.Expression, argument.Expression, site, ref context);
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
            AppendSite(writer, site);
        }

        return true;
    }

    // __gsharp_array_at(<receiver>, <index>, <site>)
    private static void AppendElementCall(ExpressionSyntax receiver, ExpressionSyntax index, NullCheckSite site,
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
            AppendSite(writer, site);
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
                var receiver = expression.WithoutAnnotations(LoweringAnnotations.ArrayNullCheckAnnotationKind);
                AppendCheck(receiver, PlanSite(receiver, place, ref context), ref context);
                return true;
            }
        }

        return false;
    }

    /// <summary>Writes <c>__gsharp_array_ref(&lt;receiver&gt;, &lt;site&gt;)</c>.</summary>
    public static void AppendCheck(ExpressionSyntax receiver, NullCheckSite site, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        writer.Append(GSharpInternalNames.Prelude.ArrayRef);
        using (writer.Parentheses())
        {
            context.Visitor.Visit(receiver);
            writer.Append(", ");
            AppendSite(writer, site);
        }
    }

    /// <summary>
    /// The place a failing check of <paramref name="receiver"/> reports: a site of the kernel's error site table when the
    /// method has one (a kernel body), otherwise a message that names the place (a device function).
    /// </summary>
    public static NullCheckSite PlanSite(ExpressionSyntax receiver, ref TranspileContext context) =>
        PlanSite(receiver, SourcePlace.Format(context.Query.SemanticModel, receiver), ref context);

    /// <summary>
    /// <see cref="PlanSite(ExpressionSyntax, ref TranspileContext)"/> with the place already formatted: the place that
    /// lowering recorded for the source of a <c>foreach</c>.
    /// </summary>
    public static NullCheckSite PlanSite(ExpressionSyntax receiver, string place, ref TranspileContext context) =>
        PlanSite(receiver, place, SiteMessage, TrapKind, ref context);

    // A site of the kernel's error site table for <node> with <message>, or, in a method without a table, the message
    // followed by the place of the node (<place>, formatted here when it is null)
    private static NullCheckSite PlanSite(ExpressionSyntax node, string? place, string message, string trapKind,
        ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        if (context.Emitter.ErrorSites is not { } table)
        {
            return new NullCheckSite(0u, message + PlaceSeparator + (place ?? SourcePlace.Format(model, node)));
        }

        var memberName = GetSourceMemberName(node, model) ?? context.Emitter.CurrentMemberName ?? "";
        return new NullCheckSite(table.Register(node, model, memberName, message, trapKind), "");
    }

    /// <summary>
    /// The check of the call <c>Gpu.NotNull(&lt;array&gt;)</c> (<paramref name="call"/>): none when
    /// <paramref name="array"/> is proven (<see cref="NonNullProof"/>), otherwise the place of the call to report.
    /// </summary>
    /// <remarks>
    /// [WHY] Not <see cref="NeedsCheck"/>, which skips an unwritten nullable kernel parameter: a null optional kernel
    /// argument reads as an empty array, but it is null, and <c>Gpu.NotNull</c> throws for it as C# does.
    /// </remarks>
    public static NotNullCheck PlanNotNull(InvocationExpressionSyntax call, ExpressionSyntax array,
        ref TranspileContext context) =>
        NonNullProof.IsProvenNonNull(array, context.Query.SemanticModel)
            ? new NotNullCheck(array, null)
            : new NotNullCheck(array, PlanSite(call, null, NotNullSiteMessage, NotNullTrapKind, ref context));

    /// <summary>
    /// Writes the plan of <see cref="PlanNotNull"/>: <c>__gsharp_array_not_null(&lt;array&gt;, &lt;site&gt;)</c>, or
    /// <c>(&lt;array&gt;)</c> when the array is proven.
    /// </summary>
    public static void AppendNotNull(in NotNullCheck check, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        if (check.Site is not { } site)
        {
            using (writer.Parentheses())
            {
                context.Visitor.Visit(check.Array);
            }

            return;
        }

        writer.Append(GSharpInternalNames.Prelude.ArrayNotNull);
        using (writer.Parentheses())
        {
            context.Visitor.Visit(check.Array);
            writer.Append(", ");
            AppendSite(writer, site);
        }
    }

    // The method that holds the receiver in the user's source, past lambdas and local functions.
    // [WHY] A member function of a struct (Dispose of a using) is emitted once per kernel that uses it, into one guarded
    // definition of the module, so its site must be the same entry for every kernel: named after the kernel, two kernels
    // would give one site ID two entries (GSH0103)
    private static string? GetSourceMemberName(ExpressionSyntax receiver, SemanticModel model)
    {
        if (model.GetReportLocation(receiver) is not { SourceTree: { } tree } location ||
            !model.Compilation.ContainsSyntaxTree(tree))
        {
            return null;
        }

        var symbol = model.Compilation.GetCachedSemanticModel(tree).GetEnclosingSymbol(location.SourceSpan.Start);
        while (symbol is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction })
        {
            symbol = symbol.ContainingSymbol;
        }

        return symbol is IMethodSymbol method ? method.Name : null;
    }

    /// <summary>
    /// The Prelude helper that checks a device function argument (#1409, the plan of <see cref="PlanArgument"/>):
    /// <c>__gsharp_array_ref</c> for a non-nullable array, <c>__gsharp_struct_ref</c> for a struct that reaches
    /// non-nullable array fields. The caller writes it, then in parentheses the argument and
    /// <see cref="AppendArgumentCheckTail"/>.
    /// </summary>
    public static string GetArgumentCheckHelper(ArgumentCheck check) =>
        check.Kind == ArgumentCheckKind.Array ? GSharpInternalNames.Prelude.ArrayRef : GSharpInternalNames.Prelude.StructRef;

    /// <summary>
    /// Writes what follows the argument in the check: <c>, &lt;site&gt;</c>, and for a struct one getter per field chain,
    /// <c>, [](const auto&amp; s) -&gt; decltype(auto) { return (s.&lt;field&gt;...); }</c>.
    /// </summary>
    public static void AppendArgumentCheckTail(ArgumentCheck check, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        writer.Append(", ");
        AppendSite(writer, check.Site);
        foreach (var path in check.Fields)
        {
            writer.Append(", [](const auto& s) -> decltype(auto) { return (s");
            foreach (var field in path)
            {
                writer.Append(".");
                CppNameResolver.AppendSymbolName(field, ref context);
            }

            writer.Append("); }");
        }
    }

    /// <summary>
    /// The check an argument of a device function call needs (#1409): none when the parameter has no non-nullable array
    /// to guard or the argument is proven (<see cref="NonNullProof"/>), otherwise the array or the struct fields to check
    /// and the place to report.
    /// </summary>
    public static ArgumentCheck PlanArgument(ExpressionSyntax argument, IParameterSymbol parameter,
        ref TranspileContext context)
    {
        if (!NonNullProof.NeedsBoundaryCheck(parameter))
        {
            return ArgumentCheck.None;
        }

        var model = context.Query.SemanticModel;
        if (parameter.Type is IArrayTypeSymbol)
        {
            return NonNullProof.IsProvenNonNull(argument, model)
                ? ArgumentCheck.None
                : new ArgumentCheck(ArgumentCheckKind.Array, PlanArgumentSite(argument, ref context),
                    ImmutableArray<ImmutableArray<IFieldSymbol>>.Empty);
        }

        return NonNullProof.IsProvenStruct(argument, model)
            ? ArgumentCheck.None
            : new ArgumentCheck(ArgumentCheckKind.Struct, PlanArgumentSite(argument, ref context),
                NonNullProof.GetNonNullableArrayFields(parameter.Type));
    }

    // The place of a null passed where the parameter is not nullable
    private static NullCheckSite PlanArgumentSite(ExpressionSyntax argument, ref TranspileContext context) =>
        PlanSite(argument, null, ArgumentSiteMessage, ArgumentTrapKind, ref context);

    // <site>u for a registered site, "<message>" otherwise
    private static void AppendSite(SourceWriter writer, NullCheckSite site)
    {
        if (site.SiteId != 0u)
        {
            writer.Append($"{site.SiteId}u");
            return;
        }

        CppStringLiteral.Append(writer, site.Message);
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

    // Parentheses, and the null-forgiving operator, which has no run-time meaning (a! is null when a is)
    private static ExpressionSyntax SkipParentheses(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    break;
                case PostfixUnaryExpressionSyntax suppressed
                    when suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = suppressed.Operand;
                    break;
                default:
                    return expression;
            }
        }
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

/// <summary>
/// Where a failing array null check reports (<see cref="ArrayNullCheck.PlanSite(ExpressionSyntax, ref TranspileContext)"/>):
/// a site of the kernel's error site table (<paramref name="SiteId"/>, never 0), or <paramref name="Message"/>, which
/// names the place, when the method has no table.
/// </summary>
internal readonly record struct NullCheckSite(uint SiteId, string Message);

/// <summary>
/// The check of a <c>Gpu.NotNull</c> call (<see cref="ArrayNullCheck.PlanNotNull"/>): the array argument, and the place
/// a null reports, or <see langword="null"/> when the array is proven and the call is the array itself.
/// </summary>
internal readonly record struct NotNullCheck(ExpressionSyntax Array, NullCheckSite? Site);

/// <summary>What the check of a device function argument tests (<see cref="ArrayNullCheck.PlanArgument"/>).</summary>
internal enum ArgumentCheckKind
{
    /// <summary>No check: the parameter has no non-nullable array, or the argument is proven.</summary>
    None,

    /// <summary>The argument is a non-nullable array.</summary>
    Array,

    /// <summary>The argument is a struct; its non-nullable array fields are tested.</summary>
    Struct
}

/// <summary>
/// The check of one device function argument: its kind, the place a failure reports, and for a struct the chains of
/// fields to the non-nullable arrays it tests.
/// </summary>
internal readonly record struct ArgumentCheck(ArgumentCheckKind Kind, NullCheckSite Site,
    ImmutableArray<ImmutableArray<IFieldSymbol>> Fields)
{
    /// <summary>The argument needs no check.</summary>
    public static ArgumentCheck None { get; } =
        new(ArgumentCheckKind.None, default, ImmutableArray<ImmutableArray<IFieldSymbol>>.Empty);
}

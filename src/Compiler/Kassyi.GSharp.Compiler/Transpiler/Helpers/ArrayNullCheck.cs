namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// The null check of a C# array read (#706): <c>Length</c> and the other <c>System.Array</c> members, an element and
/// <c>foreach</c> throw <see cref="NullReferenceException"/> on a null array in C#, and so do <c>Length</c>,
/// <c>IsEmpty</c>, an element and <c>foreach</c> of a default <c>ImmutableArray&lt;T&gt;</c>, whose array is null. The
/// receiver goes through the Prelude helper <c>__gsharp_array_ref(&lt;receiver&gt;, &lt;site&gt;)</c>, which fails on
/// every build (#1409). In a kernel body the place is a site of the kernel's error site table, so the host exception
/// carries it on CUDA as well (<see cref="NullCheckSite"/>).
/// </summary>
/// <remarks>
/// <para>
/// Only a receiver of a C# array type or of <c>ImmutableArray&lt;T&gt;</c> is checked: a span is never null in C#, and
/// <c>is null</c>, <c>??</c>, a conversion to a span (<c>AsSpan</c>) or passing the array along read nothing.
/// </para>
/// <para>
/// No check is emitted where the receiver cannot be null, as RyuJIT drops a null check that an assertion proves
/// (symbol-based only, <see cref="NonNullProof"/>): a stack array local, a non-nullable array parameter of a device
/// function whose calls check it, and a conditional whose branches are both such receivers. A kernel parameter is
/// checked whatever its annotation: the launcher passes a null array as a C# call does.
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

    /// <summary>
    /// The text of a null passed where a device function parameter is not nullable, followed by the place.
    /// </summary>
    public const string ArgumentMessagePrefix = ArgumentSiteMessage + PlaceSeparator;

    private const string ArgumentTrapKind = "ArrayNullArgument";

    // Between the text of an error and the place it names, in a method without an error site table
    private const string PlaceSeparator = " at ";

    /// <summary>Whether a read through <paramref name="receiver"/> needs the null check.</summary>
    public static bool NeedsCheck(ExpressionSyntax receiver, SemanticModel semanticModel) =>
        IsCheckedType(semanticModel.GetTypeSymbol(receiver)) && MayBeNull(receiver, semanticModel);

    // A C# array, or an ImmutableArray<T>, whose default holds a null array
    private static bool IsCheckedType(ITypeSymbol? type) =>
        type is IArrayTypeSymbol || IsImmutableArray(type);

    private static bool IsImmutableArray(ITypeSymbol? type) =>
        type is INamedTypeSymbol { IsGenericType: true, Name: "ImmutableArray" } named &&
        named.ContainingNamespace.IsSystemCollectionsImmutable();

    private static bool MayBeNull(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        expression = SkipParentheses(expression);

        // c ? a : b is null only when the branch it takes is (a null literal branch may be)
        if (expression is ConditionalExpressionSyntax conditional)
        {
            return MayBeNull(conditional.WhenTrue, semanticModel) || MayBeNull(conditional.WhenFalse, semanticModel);
        }

        // A stack array (#1543), or a non-nullable array that a call has checked (#1409)
        if (NonNullProof.IsProvenNonNull(expression, semanticModel))
        {
            return false;
        }

        // A host static member, which GSH0065 reports
        return semanticModel.GetSymbol(expression) is not IFieldSymbol { IsStatic: true };
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
        AppendElementCall(writer, receiver, index, site);
        return true;
    }

    /// <summary>
    /// Whether <paramref name="node"/> is an element of a one-dimensional C# array at an <c>int</c>-like index (not
    /// <c>^i</c> or a range): a location whose null and bounds check C# makes when it reads or stores the element.
    /// </summary>
    public static bool IsArrayElement(ElementAccessExpressionSyntax node, SemanticModel model) =>
        node.ArgumentList.Arguments is [{ RefKindKeyword.RawKind: (int)SyntaxKind.None } argument] &&
        model.GetTypeSymbol(node.Expression) is IArrayTypeSymbol { Rank: 1 } &&
        !IsIndexOrRange(argument.Expression, model);

    /// <summary>
    /// The plan of a store into the element <paramref name="node"/> (<see cref="IsArrayElement"/>) through an array and
    /// an index in temporaries: a stack array is bound by reference, and an array that may be null is checked at the
    /// store, with a site of <paramref name="table"/> (or a message naming the place when the method has no table).
    /// </summary>
    public static ElementStorePlan PlanElementStore(ElementAccessExpressionSyntax node, SemanticModel model,
        ErrorSiteTable? table, string? currentMemberName) =>
        new(
            StackArrayLocals.IsStackArrayReference(node.Expression, model),
            NeedsCheck(node.Expression, model)
                ? PlanSite(node.Expression, null, SiteMessage, TrapKind, model, table, currentMemberName)
                : null
        );

    /// <summary>
    /// Writes the element of an array and an index already evaluated into the temporaries <paramref name="receiver"/>
    /// and <paramref name="index"/>, as <paramref name="plan"/> says:
    /// <c>__gsharp_array_at(__gsharp_seq_&lt;receiver&gt;, __gsharp_seq_&lt;index&gt;, &lt;site&gt;)</c> with a check,
    /// otherwise <c>__gsharp_seq_&lt;receiver&gt;[__gsharp_seq_&lt;index&gt;]</c>.
    /// </summary>
    public static void AppendElementOfTemps(in ElementStorePlan plan, int receiver, int index, SourceWriter writer)
    {
        if (plan.Site is { } site)
        {
            AppendElementCall(writer, receiver, index, site);
            return;
        }

        EvaluationOrder.AppendTempName(writer, receiver);
        using (writer.Brackets())
        {
            EvaluationOrder.AppendTempName(writer, index);
        }
    }

    // __gsharp_array_at(__gsharp_seq_<receiver>, __gsharp_seq_<index>, <site>)
    private static void AppendElementCall(SourceWriter writer, int receiver, int index, NullCheckSite site)
    {
        writer.Append(GSharpInternalNames.Prelude.ArrayAt);
        using (writer.Parentheses())
        {
            EvaluationOrder.AppendTempName(writer, receiver);
            writer.Append(", ");
            EvaluationOrder.AppendTempName(writer, index);
            writer.Append(", ");
            AppendSite(writer, site);
        }
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
    public static void AppendCheck(ExpressionSyntax receiver, NullCheckSite site, ref TranspileContext context) =>
        AppendHelperCall(GSharpInternalNames.Prelude.ArrayRef, receiver, site, ref context);

    /// <summary>Writes <c>&lt;helper&gt;(&lt;array&gt;, &lt;site&gt;)</c>: a Prelude check of one array.</summary>
    internal static void AppendHelperCall(string helper, ExpressionSyntax array, NullCheckSite site,
        ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        writer.Append(helper);
        using (writer.Parentheses())
        {
            context.Visitor.Visit(array);
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

    /// <summary>
    /// A site of the kernel's error site table for <paramref name="node"/> with <paramref name="message"/>, or, in a
    /// method without a table, the message followed by the place of the node (<paramref name="place"/>, formatted here
    /// when it is <see langword="null"/>).
    /// </summary>
    internal static NullCheckSite PlanSite(ExpressionSyntax node, string? place, string message, string trapKind,
        ref TranspileContext context) =>
        PlanSite(node, place, message, trapKind, context.Query.SemanticModel, context.Emitter.ErrorSites,
            context.Emitter.CurrentMemberName);

    /// <summary>
    /// <see cref="PlanSite(ExpressionSyntax, string, string, string, ref TranspileContext)"/> with the parts of the
    /// context it reads: the model, the error site table of the method (<see langword="null"/> in a device function) and
    /// the name of the method being emitted.
    /// </summary>
    internal static NullCheckSite PlanSite(ExpressionSyntax node, string? place, string message, string trapKind,
        SemanticModel model, ErrorSiteTable? table, string? currentMemberName)
    {
        if (table is null)
        {
            return new NullCheckSite(0u, message + PlaceSeparator + (place ?? SourcePlace.Format(model, node)));
        }

        var memberName = GetSourceMemberName(node, model) ?? currentMemberName ?? "";
        return new NullCheckSite(table.Register(node, model, memberName, message, trapKind), "");
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
        if (!ChecksArgument(argument, parameter, context.Query.SemanticModel))
        {
            return ArgumentCheck.None;
        }

        return parameter.Type is IArrayTypeSymbol
            ? new ArgumentCheck(ArgumentCheckKind.Array, PlanArgumentSite(argument, ref context),
                ImmutableArray<ImmutableArray<IFieldSymbol>>.Empty)
            : new ArgumentCheck(ArgumentCheckKind.Struct, PlanArgumentSite(argument, ref context),
                NonNullProof.GetNonNullableArrayFields(parameter.Type));
    }

    /// <summary>
    /// Whether <paramref name="argument"/>, passed to <paramref name="parameter"/> of a device function, is checked at
    /// the call (<see cref="PlanArgument"/>): the parameter asks for it and the argument is not proven.
    /// </summary>
    public static bool ChecksArgument(ExpressionSyntax argument, IParameterSymbol parameter, SemanticModel model) =>
        NonNullProof.NeedsBoundaryCheck(parameter) &&
        !(parameter.Type is IArrayTypeSymbol
            ? NonNullProof.IsProvenNonNull(argument, model)
            : NonNullProof.IsProvenStruct(argument, model));

    // The place of a null passed where the parameter is not nullable
    private static NullCheckSite PlanArgumentSite(ExpressionSyntax argument, ref TranspileContext context) =>
        PlanSite(argument, null, ArgumentSiteMessage, ArgumentTrapKind, ref context);

    /// <summary>Writes <c>&lt;site&gt;u</c> for a registered site, otherwise the message as a string literal.</summary>
    internal static void AppendSite(SourceWriter writer, NullCheckSite site)
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

    // An instance member of System.Array: Length, LongLength, Rank, GetLength, ... (not an extension method such as
    // AsSpan), or Length / IsEmpty of ImmutableArray<T>, which read its array (IsDefault and AsSpan do not)
    private static bool IsArrayMember(ISymbol? member) =>
        member is { IsStatic: false, ContainingType.SpecialType: SpecialType.System_Array } &&
        member is not IMethodSymbol { MethodKind: MethodKind.ReducedExtension } ||
        member is IPropertySymbol { IsStatic: false, Name: "Length" or "IsEmpty" } property &&
        IsImmutableArray(property.ContainingType);

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
}

/// <summary>
/// Where a failing array null check reports (<see cref="ArrayNullCheck.PlanSite(ExpressionSyntax, ref TranspileContext)"/>):
/// a site of the kernel's error site table (<paramref name="SiteId"/>, never 0), or <paramref name="Message"/>, which
/// names the place, when the method has no table.
/// </summary>
internal readonly record struct NullCheckSite(uint SiteId, string Message);

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

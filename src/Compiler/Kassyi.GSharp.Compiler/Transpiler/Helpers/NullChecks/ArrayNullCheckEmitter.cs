using Kassyi.GSharp.Compiler.Backends.Cpp;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;

/// <summary>
/// Writes C++ array null checks and plans syntax error sites for the syntax transpiler (<see cref="ArrayNullCheck"/>).
/// </summary>
internal static class ArrayNullCheckEmitter
{
    // The trap kind of the check of the array a StackLinq query starts from
    private const string LinqSourceTrapKind = "LinqSourceNull";

    /// <summary>
    /// Writes <paramref name="receiver"/> for a read: <c>__gsharp_array_ref(&lt;receiver&gt;, "&lt;message&gt;")</c> when
    /// it needs the check (<see cref="ArrayNullCheck.NeedsCheck"/>), otherwise the receiver as it is.
    /// </summary>
    public static void AppendReceiver(ExpressionSyntax receiver, ref TranspileContext context)
    {
        if (!ArrayNullCheck.NeedsCheck(receiver, context.Query.SemanticModel))
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
        if (!ArrayNullCheck.IsArrayMember(member) || !ArrayNullCheck.NeedsCheck(receiver, context.Query.SemanticModel))
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
            ArrayNullCheck.IsIndexOrRange(argument.Expression, model) ||
            !ArrayNullCheck.NeedsCheck(node.Expression, model))
        {
            return false;
        }

        var site = PlanSite(node.Expression, ref context);
        var writer = context.Emitter.Writer;
        if (EvaluationOrder.LastSpilledOperand(
                [
                    ExpressionEffects.Of(node.Expression, model),
                    ExpressionEffects.Of(argument.Expression, model)
                        .WithThrow(ArrayElementIndex.NeedsOverflowCheck(node, model))
                ]) < 0)
        {
            AppendElementCall(node.Expression, argument.Expression, site, ref context);
            return true;
        }

        using var sequence = EvaluationOrder.BeginSequence(writer, "decltype(auto)");
        var receiver = EvaluationOrder.DeclareTemp(ref context, node.Expression, reference: false);
        var index = EvaluationOrder.BeginDeclaration(ref context);
        ArrayElementIndex.Append(argument.Expression, ref context);
        writer.AppendLine(";");
        sequence.Return();
        AppendElementCall(writer, receiver, index, site);
        return true;
    }

    /// <summary>
    /// The plan of a store into the element <paramref name="node"/> (<see cref="ArrayNullCheck.IsArrayElement"/>) through
    /// an array and an index in temporaries: a stack array is bound by reference, and an array that may be null is
    /// checked at the store, with a site of <paramref name="table"/> (or a message naming the place when the method has
    /// no table).
    /// </summary>
    public static ElementStorePlan PlanElementStore(ElementAccessExpressionSyntax node, SemanticModel model,
        ErrorSiteTable? table, string? currentMemberName) =>
        new(
            StackArrayLocals.IsStackArrayReference(node.Expression, model),
            ArrayNullCheck.NeedsCheck(node.Expression, model)
                ? PlanSite(node.Expression, null, ArrayNullCheck.SiteMessage, ArrayNullCheck.TrapKind, model, table,
                    currentMemberName)
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
            NullCheckSiteWriter.AppendSite(writer, site);
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
            ArrayElementIndex.Append(index, ref context);
            writer.Append(", ");
            NullCheckSiteWriter.AppendSite(writer, site);
        }
    }

    /// <summary>
    /// Writes <paramref name="expression"/> through the check when lowering marked it, the source of a lowered
    /// <c>foreach</c>: the null check of an array (<see cref="LoweringAnnotations.ArrayNullCheckAnnotationKind"/>) or
    /// the check of a default segment (<see cref="LoweringAnnotations.SegmentCheckAnnotationKind"/>), with the place
    /// the annotation carries.
    /// </summary>
    /// <returns><see langword="false"/>, writing nothing, when the expression carries no such annotation.</returns>
    public static bool TryAppendAnnotatedCheck(ExpressionSyntax expression, ref TranspileContext context) =>
        TryAppendMarkedCheck(expression, s_arrayNullCheck, ref context) ||
        TryAppendMarkedCheck(expression, s_segmentCheck, ref context);

    // A check that lowering puts on the source of a foreach: the annotation kind that marks the source, the Prelude
    // helper the source goes through, and the text and trap kind of its error site
    private readonly record struct AnnotatedCheck(string Kind, string Helper, string Message, string TrapKind);

    private static readonly AnnotatedCheck s_arrayNullCheck = new(
        LoweringAnnotations.ArrayNullCheckAnnotationKind,
        GSharpInternalNames.Prelude.ArrayRef,
        ArrayNullCheck.SiteMessage,
        ArrayNullCheck.TrapKind);

    private static readonly AnnotatedCheck s_segmentCheck = new(
        LoweringAnnotations.SegmentCheckAnnotationKind,
        GSharpInternalNames.Prelude.SegmentRef,
        SegmentCheck.SiteMessage,
        SegmentCheck.TrapKind);

    private static bool TryAppendMarkedCheck(ExpressionSyntax expression, in AnnotatedCheck check,
        ref TranspileContext context)
    {
        if (!expression.HasAnnotations(check.Kind))
        {
            return false;
        }

        foreach (var annotation in expression.GetAnnotations(check.Kind))
        {
            if (annotation.Data is { } place)
            {
                // The expression itself, without the annotation that brought it here
                var receiver = expression.WithoutAnnotations(check.Kind);
                var site = PlanSite(receiver, place, check.Message, check.TrapKind, ref context);
                AppendHelperCall(check.Helper, receiver, site, ref context);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The check of the array <paramref name="source"/> that a StackLinq query starts from, which throws
    /// <see cref="ArgumentNullException"/> for a null array as <c>AsStackEnumerable</c> does: <see langword="null"/>
    /// when the source cannot be null or is not a C# array.
    /// </summary>
    /// <remarks>
    /// A method without an error site table (a device function) passes the message itself, with no place, as
    /// <see cref="ArgumentCheckSite"/> does: the message must end with <c>(Parameter 'source')</c> for the mapper to
    /// give the exception its <see cref="ArgumentException.ParamName"/>.
    /// </remarks>
    public static NullCheckSite? PlanLinqSource(ExpressionSyntax source, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        if (model.GetTypeSymbol(source) is not IArrayTypeSymbol || !ArrayNullCheck.NeedsCheck(source, model))
        {
            return null;
        }

        return context.Emitter.ErrorSites is null
            ? new NullCheckSite(0u, ArgumentExceptionMessages.LinqSource)
            : PlanSite(source, null, ArgumentExceptionMessages.LinqSource, LinqSourceTrapKind, ref context);
    }

    /// <summary>
    /// Writes <paramref name="source"/> as <c>__gsharp_source_argument(&lt;source&gt;, &lt;site&gt;)</c> when
    /// <see cref="PlanLinqSource"/> says it needs the check, as it is otherwise.
    /// </summary>
    public static void AppendLinqSource(ExpressionSyntax source, ref TranspileContext context)
    {
        if (PlanLinqSource(source, ref context) is not { } site)
        {
            context.Visitor.Visit(source);
            return;
        }

        AppendHelperCall(GSharpInternalNames.Prelude.SourceArgument, source, site, ref context);
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
            NullCheckSiteWriter.AppendSite(writer, site);
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
        PlanSite(receiver, place, ArrayNullCheck.SiteMessage, ArrayNullCheck.TrapKind, ref context);

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
            return new NullCheckSite(0u, message + NullCheckSiteWriter.PlaceSeparator + (place ?? SourcePlace.Format(model, node)));
        }

        var location = model.GetReportLocation(node);
        var memberName = ErrorSiteTable.GetSourceMemberName(node, model) ?? currentMemberName ?? "";
        return new NullCheckSite(table.Register(location, memberName, message, trapKind), "");
    }
}

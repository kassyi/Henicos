namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;

/// <summary>
/// The null check of an argument passed to a non-nullable array parameter of a device function, or to a struct
/// parameter that holds non-nullable arrays (#1409): the call checks the argument once, so that the callee reads the
/// array without a check. This is the one place where G# throws earlier than C#, and the message says so (#2044).
/// </summary>
internal static class ArgumentNullCheck
{
    /// <summary>The text of the error of a registered site of a null passed where the parameter is not nullable.</summary>
    public const string ArgumentSiteMessage = "G#: a null array is passed where it is not nullable";

    /// <summary>The trap kind of an argument check site ID.</summary>
    internal const string ArgumentTrapKind = "ArrayNullArgument";

    /// <summary>The kind of check a device function parameter needs.</summary>
    internal static ArgumentCheckKind GetArgumentCheckKind(IParameterSymbol parameter)
    {
        if (!NonNullProof.NeedsBoundaryCheck(parameter))
        {
            return ArgumentCheckKind.None;
        }

        return parameter.Type is IArrayTypeSymbol ? ArgumentCheckKind.Array : ArgumentCheckKind.Struct;
    }

    /// <summary>
    /// Whether <paramref name="argument"/>, passed to <paramref name="parameter"/> of a device function, is checked at
    /// the call (<see cref="PlanArgument"/>): the parameter asks for it and the argument is not proven.
    /// </summary>
    public static bool ChecksArgument(ExpressionSyntax argument, IParameterSymbol parameter, SemanticModel model) =>
        GetArgumentCheckKind(parameter) is var kind && kind != ArgumentCheckKind.None &&
        !(kind == ArgumentCheckKind.Array
            ? NonNullProof.IsProvenNonNull(argument, model)
            : NonNullProof.IsProvenStruct(argument, model));

    /// <summary>
    /// The check an argument of a device function call needs (#1409): none when the parameter has no non-nullable array
    /// to guard or the argument is proven (<see cref="NonNullProof"/>), otherwise the array, or each non-nullable array
    /// field of the struct, with the place to report.
    /// </summary>
    public static ArgumentCheck PlanArgument(ExpressionSyntax argument, IParameterSymbol parameter,
        ref TranspileContext context)
    {
        if (!ChecksArgument(argument, parameter, context.Query.SemanticModel))
        {
            return ArgumentCheck.None;
        }

        if (parameter.Type is IArrayTypeSymbol)
        {
            var site = PlanArgumentSite(argument, parameter, null, parameter.Type, ArgumentTrapKind, ref context);
            return new ArgumentCheck(ArgumentCheckKind.Array, site, ImmutableArray<StructFieldCheck>.Empty);
        }

        var paths = NonNullProof.GetNonNullableArrayFields(parameter.Type);
        var builder = ImmutableArray.CreateBuilder<StructFieldCheck>(paths.Length);
        foreach (var path in paths)
        {
            var fieldPath = FormatFieldPath(parameter, path);
            var trapKind = ArgumentTrapKind + "#" + fieldPath;
            var site = PlanArgumentSite(argument, parameter, fieldPath, path[^1].Type, trapKind, ref context);
            builder.Add(new StructFieldCheck(site, path));
        }

        return new ArgumentCheck(ArgumentCheckKind.Struct, default, builder.MoveToImmutable());
    }

    /// <summary>Plans the error site of an argument check from a report location.</summary>
    internal static NullCheckSite PlanArgumentSite(Location location, Compilation compilation,
        ErrorSiteTable? table, string? currentMemberName) =>
        ArrayNullCheck.PlanSite(location, null, ArgumentSiteMessage, ArgumentTrapKind, compilation, table,
            currentMemberName);

    /// <summary>
    /// The Prelude helper that checks a device function argument (the plan of <see cref="PlanArgument"/>):
    /// <c>__gsharp_array_ref</c> for a non-nullable array, <c>__gsharp_struct_ref</c> for a struct that reaches
    /// non-nullable array fields. The caller writes it, then in parentheses the argument and
    /// <see cref="AppendArgumentCheckTail"/>.
    /// </summary>
    public static string GetArgumentCheckHelper(ArgumentCheck check) =>
        check.Kind == ArgumentCheckKind.Array ? GSharpInternalNames.Prelude.ArrayRef : GSharpInternalNames.Prelude.StructRef;

    /// <summary>
    /// Writes what follows the argument in the check: <c>, &lt;site&gt;</c> for an array argument, and for a struct
    /// one check per field chain,
    /// <c>, [](const auto&amp; s) { return __gsharp_array_ref(s.&lt;field&gt;..., &lt;site&gt;); }</c>.
    /// </summary>
    public static void AppendArgumentCheckTail(ArgumentCheck check, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        if (check.Kind == ArgumentCheckKind.Array)
        {
            writer.Append(", ");
            ArrayNullCheck.AppendSite(writer, check.ArraySite);
            return;
        }

        foreach (var fieldCheck in check.StructChecks)
        {
            writer.Append(", [](const auto& s) { return ");
            writer.Append(GSharpInternalNames.Prelude.ArrayRef);
            writer.Append("(s");
            foreach (var field in fieldCheck.Path)
            {
                writer.Append(".");
                CppNameResolver.AppendSymbolName(field, ref context);
            }

            writer.Append(", ");
            ArrayNullCheck.AppendSite(writer, fieldCheck.Site);
            writer.Append("); }");
        }
    }

    // The place of a null passed where the parameter is not nullable: in a kernel a site of its error site table, which
    // holds the place, otherwise the message followed by the place
    private static NullCheckSite PlanArgumentSite(ExpressionSyntax argument, IParameterSymbol parameter,
        string? fieldPath, ITypeSymbol targetType, string trapKind, ref TranspileContext context)
    {
        if (context.Emitter.ErrorSites is null)
        {
            var place = SourcePlace.Format(context.Query.SemanticModel, argument);
            return new NullCheckSite(0u, FormatArgumentMessage(parameter, fieldPath, targetType, place));
        }

        var message = FormatArgumentMessage(parameter, fieldPath, targetType, null);
        return ArrayNullCheck.PlanSite(argument, null, message, trapKind, ref context);
    }

    /// <summary>
    /// The path from <paramref name="parameter"/> to a nested struct field, for example <c>"s.Inner.Data"</c>.
    /// </summary>
    internal static string FormatFieldPath(IParameterSymbol parameter, ImmutableArray<IFieldSymbol> path)
    {
        using var writer = new SourceWriter();
        writer.Append(MessageText.Name(parameter));
        foreach (var field in path)
        {
            writer.Append($".{MessageText.Name(field)}");
        }

        return writer.ToString();
    }

    /// <summary>
    /// Formats the error message explaining why G# checks a non-nullable array argument earlier than C# (#2044).
    /// </summary>
    internal static string FormatArgumentMessage(IParameterSymbol parameter, string? fieldPath, ITypeSymbol targetType,
        string? place)
    {
        var calleeName = parameter.ContainingSymbol is { } callee ? MessageText.Name(callee) : "";
        using var writer = new SourceWriter();
        if (fieldPath is not null)
        {
            writer.Append($"G#: a null array is passed to the non-nullable field '{fieldPath}' of parameter ");
        }
        else
        {
            writer.Append("G#: a null array is passed to the non-nullable parameter ");
        }

        writer.Append($"'{MessageText.Name(parameter)}' of {calleeName}");
        if (place is not null)
        {
            writer.Append($" at {place}");
        }

        writer.Append(". G# checks the nullable annotation at the call, earlier than C#, which throws only when the ");
        writer.Append($"array is read; declare the {(fieldPath is null ? "parameter" : "field")} nullable (");
        writer.AppendCsharpTypeName(targetType);
        writer.Append("?) to keep the C# behavior.");
        return writer.ToString();
    }
}

/// <summary>What the check of a device function argument tests (<see cref="ArgumentNullCheck.PlanArgument"/>).</summary>
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
/// A non-nullable array field checked in a struct argument of a device function call (#1409, #2044):
/// the site of the check, and the path of fields from the struct to the array.
/// </summary>
internal readonly record struct StructFieldCheck(NullCheckSite Site, ImmutableArray<IFieldSymbol> Path);

/// <summary>
/// The check of one device function argument: its kind, the place a failure reports for an array argument,
/// and for a struct the checks and field paths of its non-nullable arrays.
/// </summary>
internal readonly record struct ArgumentCheck(
    ArgumentCheckKind Kind,
    NullCheckSite ArraySite,
    ImmutableArray<StructFieldCheck> StructChecks)
{
    /// <summary>The argument needs no check.</summary>
    public static ArgumentCheck None { get; } =
        new(ArgumentCheckKind.None, default, ImmutableArray<StructFieldCheck>.Empty);
}

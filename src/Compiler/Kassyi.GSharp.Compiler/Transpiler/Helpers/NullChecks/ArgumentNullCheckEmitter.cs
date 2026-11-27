using Kassyi.GSharp.Compiler.Backends.Cpp;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;

/// <summary>
/// Plans and writes device function argument null checks for the syntax transpiler (<see cref="ArgumentNullCheck"/>).
/// </summary>
internal static class ArgumentNullCheckEmitter
{
    /// <summary>
    /// The check an argument of a device function call needs (#1409): none when the parameter has no non-nullable array
    /// to guard or the argument is proven (<see cref="NonNullProof"/>), otherwise the array, or each non-nullable array
    /// field of the struct, with the place to report.
    /// </summary>
    public static ArgumentCheck PlanArgument(ExpressionSyntax argument, IParameterSymbol parameter,
        ref TranspileContext context)
    {
        if (!ArgumentNullCheck.ChecksArgument(argument, parameter, context.Query.SemanticModel))
        {
            return ArgumentCheck.None;
        }

        if (parameter.Type is IArrayTypeSymbol)
        {
            var site = PlanArgumentSite(argument, parameter, null, parameter.Type, ArgumentNullCheck.ArgumentTrapKind,
                ref context);
            return new ArgumentCheck(ArgumentCheckKind.Array, site, ImmutableArray<StructFieldCheck>.Empty);
        }

        var paths = NonNullProof.GetNonNullableArrayFields(parameter.Type);
        var builder = ImmutableArray.CreateBuilder<StructFieldCheck>(paths.Length);
        foreach (var path in paths)
        {
            var fieldPath = ArgumentNullCheck.FormatFieldPath(parameter, path);
            var trapKind = ArgumentNullCheck.ArgumentTrapKind + "#" + fieldPath;
            var site = PlanArgumentSite(argument, parameter, fieldPath, path[^1].Type, trapKind, ref context);
            builder.Add(new StructFieldCheck(site, path));
        }

        return new ArgumentCheck(ArgumentCheckKind.Struct, default, builder.MoveToImmutable());
    }

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
            NullCheckSiteWriter.AppendSite(writer, check.ArraySite);
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
            NullCheckSiteWriter.AppendSite(writer, fieldCheck.Site);
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
            return new NullCheckSite(0u, ArgumentNullCheck.FormatArgumentMessage(parameter, fieldPath, targetType, place));
        }

        var message = ArgumentNullCheck.FormatArgumentMessage(parameter, fieldPath, targetType, null);
        return ArrayNullCheckEmitter.PlanSite(argument, null, message, trapKind, ref context);
    }
}

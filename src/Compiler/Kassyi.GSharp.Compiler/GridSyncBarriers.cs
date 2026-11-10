namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Reads the mode of a <c>Gpu.SyncGrid(mode)</c> call, so that the analyzer and the CUDA transpiler agree on the
/// barrier and on what they reject.
/// </summary>
internal static class GridSyncBarriers
{
    /// <summary>The parameter name of the mode, for GSH0012.</summary>
    public const string ModeParameter = "mode";

    /// <summary>The macro name, for GSH0012.</summary>
    public const string MacroName = "Gpu.SyncGrid";

    /// <summary>The GSH0003 text for a constant mode that names no member of <c>GridSyncMode</c>.</summary>
    public const string UndefinedMode = "a GridSyncMode value that names no member of the enum";

    private const string DeterministicMember = "Deterministic";
    private const string FastMember = "Fast";

    /// <summary>
    /// The barrier of <paramref name="invocation"/>, a <c>Gpu.SyncGrid</c> call without a body
    /// (<paramref name="call"/>), and its mode argument, or <c>null</c> when the call passes none.
    /// </summary>
    /// <remarks>
    /// The member is found by the constant value of the argument, so a cast or a <c>const</c> of the enum type works as
    /// the member itself does.
    /// </remarks>
    public static GridSyncBarrier Get(
        InvocationExpressionSyntax invocation,
        GpuMacroCall call,
        SemanticModel model,
        out ExpressionSyntax? modeArgument)
    {
        modeArgument = GetModeArgument(invocation, call, out var modeType);
        if (modeArgument is null || modeType is null)
        {
            return GridSyncBarrier.Deterministic;
        }

        var value = model.GetSafeConstantValue(modeArgument);
        if (!value.HasValue || value.Value is null)
        {
            return GridSyncBarrier.NotConstant;
        }

        return GetMemberName(modeType, value.Value) switch
        {
            DeterministicMember => GridSyncBarrier.Deterministic,
            FastMember => GridSyncBarrier.Fast,
            _ => GridSyncBarrier.Undefined
        };
    }

    // The argument of the mode parameter and its type, or null for a call with a body or without a mode argument
    private static ExpressionSyntax? GetModeArgument(
        InvocationExpressionSyntax invocation,
        GpuMacroCall call,
        out ITypeSymbol? modeType)
    {
        modeType = null;
        if (call.HasBody || call.Method is not { Parameters: [var parameter] })
        {
            return null;
        }

        modeType = parameter.Type;
        return call.GetArgument(invocation.ArgumentList.Arguments, 0)?.Expression;
    }

    // The name of the member of enumType whose constant is value, or null when no member has it
    private static string? GetMemberName(ITypeSymbol enumType, object value) =>
        enumType.GetMembers()
            .AsStackEnumerable()
            .OfType<IFieldSymbol>()
            .Where(value, static (field, v) => field.HasConstantValue && Equals(field.ConstantValue, v))
            .Select(static field => field.Name)
            .FirstOrDefault();
}

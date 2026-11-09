namespace Kassyi.GSharp.Compiler;

/// <summary>
/// The grid barrier that a <c>Gpu.SyncGrid(mode)</c> call asks for (the <c>GridSyncMode</c> of Kassyi.GSharp.Core), or
/// why it has none.
/// </summary>
internal enum GridSyncBarrier
{
    /// <summary><c>GridSyncMode.Deterministic</c>, also when the call passes no mode.</summary>
    Deterministic,

    /// <summary><c>GridSyncMode.Fast</c>.</summary>
    Fast,

    /// <summary>The mode is not a compile-time constant (GSH0012).</summary>
    NotConstant,

    /// <summary>The mode is a constant that names no member of <c>GridSyncMode</c> (GSH0003).</summary>
    Undefined
}

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
        modeArgument = null;
        if (call.HasBody || call.Method is not { Parameters: [var parameter] })
        {
            return GridSyncBarrier.Deterministic;
        }

        modeArgument = call.GetArgument(invocation.ArgumentList.Arguments, 0)?.Expression;
        if (modeArgument is null)
        {
            return GridSyncBarrier.Deterministic;
        }

        var value = model.GetSafeConstantValue(modeArgument);
        if (!value.HasValue || value.Value is null)
        {
            return GridSyncBarrier.NotConstant;
        }

        var member = parameter.Type.GetMembers()
            .AsStackEnumerable()
            .OfType<IFieldSymbol>()
            .Where(value.Value, static (field, v) => field.HasConstantValue && Equals(field.ConstantValue, v))
            .Select(static field => field.Name)
            .FirstOrDefault();
        return member switch
        {
            DeterministicMember => GridSyncBarrier.Deterministic,
            FastMember => GridSyncBarrier.Fast,
            _ => GridSyncBarrier.Undefined
        };
    }
}

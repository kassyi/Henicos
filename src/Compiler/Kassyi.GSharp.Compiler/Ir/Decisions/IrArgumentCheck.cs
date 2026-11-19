namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>The boundary null check planned for a device function argument (#1561).</summary>
/// <param name="Kind">Whether the argument needs no check, an array check, or struct field checks.</param>
/// <param name="ArraySite">
/// The error site when <paramref name="Kind"/> is <see cref="ArgumentCheckKind.Array"/>.
/// </param>
/// <param name="StructChecks">
/// The field checks when <paramref name="Kind"/> is <see cref="ArgumentCheckKind.Struct"/>.
/// </param>
internal readonly record struct IrArgumentCheck(
    ArgumentCheckKind Kind,
    IrErrorSite? ArraySite,
    ImmutableArray<IrStructFieldCheck> StructChecks)
{
    /// <summary>Shared value indicating no argument check is needed.</summary>
    public static IrArgumentCheck None { get; } =
        new(ArgumentCheckKind.None, null, ImmutableArray<IrStructFieldCheck>.Empty);
}

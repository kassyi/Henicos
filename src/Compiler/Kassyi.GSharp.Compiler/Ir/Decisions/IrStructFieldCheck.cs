namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>
/// A non-nullable array field check within a struct argument of a device function call (#1561).
/// </summary>
/// <param name="Site">The error site (place null, to be formatted with call place in device functions).</param>
/// <param name="Path">The chain of struct fields from the parameter to the array field.</param>
/// <param name="FieldPath">The formatted field path (e.g. <c>"holder.Data"</c>).</param>
internal readonly record struct IrStructFieldCheck(
    IrErrorSite Site,
    ImmutableArray<IFieldSymbol> Path,
    string FieldPath);

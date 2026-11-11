namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>Why a struct is <see cref="TransferClass.Unsupported"/>.</summary>
/// <param name="Reason">
/// The reason, as the end of the GSH0078 message (<c>field 'B' at offset 4 partially overlaps field 'A'</c>).
/// </param>
/// <param name="FieldPath">
/// The dotted path of the member whose type has no device representation (GSH0079), otherwise <see langword="null"/>.
/// </param>
/// <param name="FieldType">The type of that member, otherwise <see langword="null"/>.</param>
public readonly record struct UnsupportedLayout(string Reason, string? FieldPath, ITypeSymbol? FieldType)
{
    /// <summary>
    /// Whether a member type has no device representation (GSH0079) rather than the layout being one the device cannot
    /// express (GSH0078).
    /// </summary>
    public bool IsMissingDeviceType => FieldType is not null;
}

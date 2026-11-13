namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// A type parameter of a generic intrinsic method, emitted as <c>&lt;Name&gt;</c> and
/// <c>where Name : Constraints</c>.
/// </summary>
internal sealed class TypeParameterDefinition
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the constraints, in order (<c>struct</c>, <c>unmanaged</c>, an interface, ...).</summary>
    public ImmutableArray<string> Constraints { get; set; } = ImmutableArray<string>.Empty;

    public string? Description { get; set; }
}

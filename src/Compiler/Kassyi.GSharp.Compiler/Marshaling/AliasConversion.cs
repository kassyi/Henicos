namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// A converted element type among the arguments that a launcher compares for overlaps (<see cref="LaunchAliasPlan"/>):
/// a group of such arguments is converted as a whole, element by element, into one buffer in the device layout
/// (<c>HostAliasStaging</c> of the runtime), with the element's generated <c>Marshal_</c> and <c>Unmarshal_</c>.
/// </summary>
/// <param name="Key">
/// The number that the ranges of the element type carry (<see cref="AliasRange.ConversionKey"/>).
/// </param>
/// <param name="HostType">The host element type.</param>
/// <param name="ShadowType">The <c>AotContexts</c> shadow struct of the element, its device layout.</param>
/// <param name="MarshalMethod">The <c>Marshal_</c> method of the element.</param>
/// <param name="UnmarshalMethod">The <c>Unmarshal_</c> method of the element.</param>
/// <param name="Allocations">
/// What <c>Marshal_</c> records its allocations in: the CUDA launch's <c>DeviceArrayRegions</c> (or <c>null</c>), or
/// the CPU launch's list of pinned handles.
/// </param>
internal readonly record struct AliasConversion(
    int Key,
    string HostType,
    string ShadowType,
    string MarshalMethod,
    string UnmarshalMethod,
    string Allocations);

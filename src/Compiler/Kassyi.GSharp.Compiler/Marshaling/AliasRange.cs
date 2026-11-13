namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// One argument that a launcher compares by address for overlapping memory (<c>HostAliasGroups</c> of the runtime),
/// as <see cref="AliasRangeEmitter"/> writes it: the <c>fixed</c> pointer, what it pins, the byte length, the host and
/// device element types, the device alignment, the conversion, the traits and the name for the exception messages.
/// </summary>
/// <param name="Pinned">The <c>fixed</c> pointer local (<see cref="LauncherLocal.AliasPinned"/>).</param>
/// <param name="PinCall">
/// The <c>HostAliasPins</c> call that gives the reference to pin (<c>Start(&lt;span&gt;)</c>).
/// </param>
/// <param name="ByteLength">The length in bytes, 0 for a null or empty argument.</param>
/// <param name="ElementType">The host element type.</param>
/// <param name="DeviceElementType">The device element type: the shadow struct of a converted element.</param>
/// <param name="Alignment">The device alignment of the element, 0 to derive it from the size at run time.</param>
/// <param name="ConversionKey">
/// 0 when the device reads the bytes as they are, otherwise the number of the converted element type in the launch
/// (<see cref="AliasConversion"/>).
/// </param>
/// <param name="Traits">The <c>HostAliasGroups</c> traits (<see cref="LaunchAliasPlan.CopiesIn"/>, ...).</param>
/// <param name="Name">The parameter name as metadata spells it.</param>
internal readonly record struct AliasRange(
    string Pinned,
    string PinCall,
    string ByteLength,
    string ElementType,
    string DeviceElementType,
    int Alignment,
    int ConversionKey,
    byte Traits,
    string Name)
{
    /// <summary>The range of an array-like argument: <c>Start(&lt;span&gt;)</c>, length times element size.</summary>
    public static AliasRange ForArray(string pinned, string span, string count, in AliasElement element,
        byte traits, string name) =>
        new(pinned, $"Start({span})", $"(nint){count} * sizeof({element.HostType})", element.HostType,
            element.DeviceType, element.Alignment, element.ConversionKey, traits, name);

    /// <summary>The range of a value passed by reference: <c>StartOf(ref &lt;value&gt;)</c>, one element.</summary>
    public static AliasRange ForReference(string pinned, string reference, in AliasElement element, byte traits,
        string name) =>
        new(pinned, $"StartOf(ref {reference})", $"sizeof({element.HostType})", element.HostType,
            element.DeviceType, element.Alignment, element.ConversionKey, traits, name);
}

/// <summary>The element of an <see cref="AliasRange"/>: host and device types, device alignment, conversion.</summary>
/// <param name="HostType">The host element type.</param>
/// <param name="DeviceType">The device element type (the host type when the device reads it as it is).</param>
/// <param name="Alignment">The device alignment, 0 to derive it from the size at run time.</param>
/// <param name="ConversionKey">0, or the number of the converted element type in the launch.</param>
internal readonly record struct AliasElement(string HostType, string DeviceType, int Alignment, int ConversionKey)
{
    /// <summary>An element that the device reads as it is.</summary>
    public static AliasElement Raw(string type, int alignment) => new(type, type, alignment, 0);
}

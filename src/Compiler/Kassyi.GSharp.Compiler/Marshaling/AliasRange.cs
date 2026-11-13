namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// One argument that a launcher compares by address for overlapping memory (<c>HostAliasGroups</c> of the runtime),
/// as <see cref="AliasRangeEmitter"/> writes it: the <c>fixed</c> pointer, what it pins, the byte length, the element
/// type, the traits and the name for the exception messages.
/// </summary>
/// <param name="Pinned">The <c>fixed</c> pointer local (<see cref="LauncherLocal.AliasPinned"/>).</param>
/// <param name="PinCall">
/// The <c>HostAliasPins</c> call that gives the reference to pin (<c>Start(&lt;span&gt;)</c>).
/// </param>
/// <param name="ByteLength">The length in bytes, 0 for a null or empty argument.</param>
/// <param name="ElementType">The element type, whose size is the third value of the range.</param>
/// <param name="Traits">The <c>HostAliasGroups</c> traits (<see cref="LaunchAliasPlan.CopiesIn"/>, ...).</param>
/// <param name="Name">The parameter name as metadata spells it.</param>
internal readonly record struct AliasRange(
    string Pinned,
    string PinCall,
    string ByteLength,
    string ElementType,
    byte Traits,
    string Name)
{
    /// <summary>The range of an array-like argument: <c>Start(&lt;span&gt;)</c>, length times element size.</summary>
    public static AliasRange ForArray(string pinned, string span, string count, string element, byte traits,
        string name) =>
        new(pinned, $"Start({span})", $"(nint){count} * sizeof({element})", element, traits, name);

    /// <summary>The range of a value passed by reference: <c>StartOf(ref &lt;value&gt;)</c>, one element.</summary>
    public static AliasRange ForReference(string pinned, string reference, string type, byte traits, string name) =>
        new(pinned, $"StartOf(ref {reference})", $"sizeof({type})", type, traits, name);
}

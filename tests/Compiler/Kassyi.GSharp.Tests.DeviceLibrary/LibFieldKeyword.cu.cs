using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.DeviceLibrary;

/// <summary>
/// A struct with a property whose setter uses the <c>field</c> keyword and an auto-property: a consumer reads the
/// accessors from the library's record, since the metadata has no bodies.
/// </summary>
public struct LibScaled
{
    public int X { get; set => field = value * 2; }

    public int Y { get; set; }
}

/// <summary>
/// A value whose <c>+</c> is an operator macro, for a target that is an auto-property of a library struct.
/// </summary>
public struct LibMacroValue
{
    public int V;

    [GpuOperatorMacro(typeof(LibMacroValue), nameof(Add))]
    public static LibMacroValue operator +(LibMacroValue left, LibMacroValue right) => throw new GpuOnlyException();

    [GpuDeviceFunction]
    public static void Add(ref LibMacroValue result, LibMacroValue left, LibMacroValue right)
    {
        result.V = left.V + right.V;
    }
}

/// <summary>
/// A struct with an auto-property: a consumer finds its backing field by name, since Roslyn does not link it to the
/// property in metadata.
/// </summary>
public struct LibMacroHolder
{
    public LibMacroValue P { get; set; }
}

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>How a kernel parameter crosses from the host to the device.</summary>
internal enum KernelParameterKind
{
    /// <summary>Passed by value.</summary>
    Scalar,

    /// <summary>
    /// A <c>ref</c> / <c>out</c> parameter that is not a user struct: copied through a device allocation.
    /// </summary>
    RefScalar,

    /// <summary>
    /// An array-like parameter (see <see cref="ArrayContainerKind"/>): copied as a <c>GSharpArray</c>.
    /// </summary>
    Array,

    /// <summary>A user struct, by value or by reference, marshaled through its native shadow struct.</summary>
    CustomStruct
}

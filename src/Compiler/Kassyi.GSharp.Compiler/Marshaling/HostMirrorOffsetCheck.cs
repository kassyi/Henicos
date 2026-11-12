namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>How the host mirror checks the offset of one of its fields.</summary>
internal enum HostMirrorOffsetCheck
{
    /// <summary>
    /// Not checked: a fixed buffer or a pointer, which <c>Unsafe.As</c> cannot take, or no offset to compare.
    /// </summary>
    None,

    /// <summary>
    /// Against the host offset that the compiler predicts (<see cref="HostMirrorField.ExpectedOffset"/>), for a field
    /// the launcher cannot name on the struct.
    /// </summary>
    PredictedOffset,

    /// <summary>Against the same field of the struct, which the launcher can name.</summary>
    StructField
}

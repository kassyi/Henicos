using System.ComponentModel;

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>One group of overlapping arguments of a launch (<see cref="HostAliasGroups"/>).</summary>
/// <param name="Length">
/// The length in bytes, from the member with the lowest address to the end of the last one.
/// </param>
/// <param name="Shift">
/// The byte offset that the device region of the group starts at past an address aligned to
/// <paramref name="Alignment"/>, so that every member is aligned for its elements.
/// </param>
/// <param name="Alignment">The widest element alignment among the members.</param>
/// <param name="CopiesIn">Whether the group is copied in (any member is).</param>
/// <param name="CopiesBack">Whether the group is copied back (any member is).</param>
/// <param name="ConversionKey">
/// 0 when the device reads the bytes of the group as they are, otherwise the launcher's number of the converted element
/// type of every member, which the launcher converts through <see cref="HostAliasStaging"/>.
/// </param>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly record struct HostAliasGroupLayout(
    nint Length,
    nint Shift,
    int Alignment,
    bool CopiesIn,
    bool CopiesBack,
    int ConversionKey);

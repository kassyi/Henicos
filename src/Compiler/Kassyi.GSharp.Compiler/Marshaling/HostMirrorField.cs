namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>One field of the host mirror, in <see cref="StructAbiMembers"/> order.</summary>
/// <param name="Member">The member of the struct that the field mirrors.</param>
/// <param name="FieldOffset">
/// The <c>[FieldOffset]</c> of a <c>LayoutKind.Explicit</c> mirror, otherwise <see langword="null"/>.
/// </param>
/// <param name="StorageType">
/// The type the mirror declares the field as (<see cref="HostFieldAccess.GetStorageType"/>).
/// </param>
/// <param name="Check">How the offset is checked before the mirror is used.</param>
/// <param name="ExpectedOffset">
/// The predicted host offset for <see cref="HostMirrorOffsetCheck.PredictedOffset"/>.
/// </param>
internal readonly record struct HostMirrorField(
    StructAbiMember Member,
    ITypeSymbol StorageType,
    int? FieldOffset,
    HostMirrorOffsetCheck Check,
    int ExpectedOffset);

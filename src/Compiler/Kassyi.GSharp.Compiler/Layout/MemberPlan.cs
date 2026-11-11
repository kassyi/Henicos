namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>One member of a struct's <see cref="TransferPlan.Blit"/> or <see cref="TransferPlan.Convert"/> plan.</summary>
/// <param name="Name">The member name (<see cref="StructAbiMember.Name"/>).</param>
/// <param name="HostOffset">The host offset, or <see langword="null"/> when the CLR decides it at run time.</param>
/// <param name="DeviceOffset">The device offset.</param>
/// <param name="ElementCount">The element count of an inline array or a fixed buffer, otherwise 1.</param>
/// <param name="Kind">
/// What the member is (<see cref="StructAbiMember.Kind"/>): the emitters copy a fixed buffer, an inline array element
/// (also of <c>[InlineArray(1)]</c>, whose count is 1) and a captured parameter each in its own way.
/// </param>
/// <param name="FullyQualifiedTypeName">The <c>global::</c>-qualified member type.</param>
/// <param name="Access">Whether a launcher can name the member.</param>
/// <param name="AccessAssembly">
/// The name of the assembly that declares an <see cref="MemberAccess.Internal"/> member, otherwise empty.
/// </param>
/// <param name="Plan">The plan of one element of the member.</param>
internal sealed record MemberPlan(
    string Name,
    int? HostOffset,
    int DeviceOffset,
    int ElementCount,
    StructAbiMemberKind Kind,
    string FullyQualifiedTypeName,
    MemberAccess Access,
    string AccessAssembly,
    TransferPlan Plan
)
{
    /// <summary>Whether a launcher in the assembly <paramref name="launcherAssemblyName"/> can name the member.</summary>
    public bool IsNameableFrom(string launcherAssemblyName) =>
        Access == MemberAccess.Public ||
        (Access == MemberAccess.Internal && AccessAssembly == launcherAssemblyName);
}

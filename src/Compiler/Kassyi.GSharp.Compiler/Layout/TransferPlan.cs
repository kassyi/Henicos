namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>
/// How a value of one type crosses the host-device boundary under one dialect, decided once by
/// <see cref="TransferPlanner.Plan"/>.
/// </summary>
/// <remarks>
/// A plan holds no <see cref="ISymbol"/> and compares by value, so it can sit in the incremental generator's cached
/// models. The sizes are in bytes.
/// </remarks>
/// <param name="HostSize">
/// The size of the value in host CLR memory, or <see langword="null"/> when the CLR decides it at run time (a struct
/// with a reference field or <c>LayoutKind.Auto</c>).
/// </param>
/// <param name="DeviceSize">The size of the value in device memory.</param>
internal abstract record TransferPlan(int? HostSize, int DeviceSize)
{
    /// <summary>The host and device bytes are the same: the value is copied as one block.</summary>
    /// <param name="Size">The size on both sides.</param>
    /// <param name="CSharpType">The <c>global::</c>-qualified C# type.</param>
    /// <param name="UserStructDisplayName">
    /// For a user struct, the type name that the launcher's layout check reports; <see langword="null"/> for any
    /// other type, which the launcher does not check.
    /// </param>
    /// <param name="Members">
    /// The members of a struct with their host offsets and plans, from which the launcher's layout check is built
    /// (<see cref="TransferPlanGuards"/>); empty for a primitive, an enum, a pointer or a type of the dialect's type map.
    /// </param>
    public sealed record Blit(
        int Size,
        string CSharpType,
        string? UserStructDisplayName,
        EquatableArray<MemberPlan> Members
    ) : TransferPlan(Size, Size);

    /// <summary>The value goes through its native shadow struct, member by member.</summary>
    /// <param name="CSharpType">The <c>global::</c>-qualified C# type.</param>
    /// <param name="ShadowPath">The <c>AotContexts</c> path of the shadow struct.</param>
    /// <param name="Members">The members in declaration order, each with its own plan.</param>
    /// <param name="HostSize">The host size, or <see langword="null"/> when the CLR decides it at run time.</param>
    /// <param name="DeviceSize">The device size.</param>
    public sealed record Convert(
        string CSharpType,
        string ShadowPath,
        EquatableArray<MemberPlan> Members,
        int? HostSize,
        int DeviceSize
    ) : TransferPlan(HostSize, DeviceSize);

    /// <summary>
    /// A struct that is already being planned further up the tree, reached again through an array field of its own
    /// type (<c>struct Node { Node[] Children; }</c>); its plan is the enclosing one with the same
    /// <see cref="CSharpType"/>.
    /// </summary>
    /// <param name="CSharpType">The <c>global::</c>-qualified C# type.</param>
    /// <param name="ShadowPath">The <c>AotContexts</c> path of the shadow struct.</param>
    /// <param name="HostSize">The host size, or <see langword="null"/> when the CLR decides it at run time.</param>
    /// <param name="DeviceSize">The device size.</param>
    public sealed record StructRef(string CSharpType, string ShadowPath, int? HostSize, int DeviceSize)
        : TransferPlan(HostSize, DeviceSize);

    /// <summary>A <c>Nullable&lt;T&gt;</c>: <c>HasValue</c> and the value, which has its own plan.</summary>
    /// <param name="Value">The plan of <c>T</c>.</param>
    /// <param name="ValueDeviceOffset">The device offset of the value after the <c>HasValue</c> flag.</param>
    /// <param name="HostSize">The host size.</param>
    /// <param name="DeviceSize">The device size.</param>
    public sealed record Nullable(TransferPlan Value, int ValueDeviceOffset, int? HostSize, int DeviceSize)
        : TransferPlan(HostSize, DeviceSize);

    /// <summary>
    /// An array or an array-like value (<c>Span&lt;T&gt;</c>, <c>ImmutableArray&lt;T&gt;</c>, ...). The host holds a
    /// reference, from which the launcher builds a 16-byte <c>GSharpArrayProxy</c> (<see cref="HostProxySize"/>); the
    /// device receives a <c>GSharpArray</c> of the dialect's array view size.
    /// </summary>
    /// <param name="Element">The plan of the element.</param>
    /// <param name="DeviceSize">The dialect's array view size.</param>
    public sealed record ArrayView(TransferPlan Element, int DeviceSize) : TransferPlan(HostProxySize, DeviceSize)
    {
        /// <summary>The size of the <c>GSharpArrayProxy</c> that the launcher passes for the array.</summary>
        public const int HostProxySize = 16;
    }

    /// <summary>The value cannot cross the boundary; the diagnostic says why.</summary>
    /// <remarks>
    /// GSH0078 takes the planned type (<c>{0}</c>, which the reporting rule has) and <see cref="Reason"/>. GSH0079
    /// takes <see cref="FieldTypeName"/> and <see cref="FieldPath"/>, both set; it is used only for a member of a
    /// struct. A planned type outside the user structs that has no device representation (<c>decimal</c> as a
    /// parameter) is GSH0078 with the reason <c>'decimal' has no device representation</c>, as there is no field to
    /// name.
    /// </remarks>
    /// <param name="DiagnosticId">The ID of the diagnostic to report (GSH0078 or GSH0079).</param>
    /// <param name="Reason">The reason, as the end of the GSH0078 message.</param>
    /// <param name="FieldPath">
    /// The dotted path of the member whose type has no device representation (GSH0079), otherwise
    /// <see langword="null"/>.
    /// </param>
    /// <param name="FieldTypeName">
    /// The C# spelling of that member's type (<see cref="TypeLayoutCalculator.FormatTypeName"/>), otherwise
    /// <see langword="null"/>.
    /// </param>
    public sealed record Unsupported(string DiagnosticId, string Reason, string? FieldPath, string? FieldTypeName)
        : TransferPlan(0, 0);
}

/// <summary>Whether a generated launcher can name a member to check its offset.</summary>
internal enum MemberAccess
{
    /// <summary>Not an ordinary field the launcher can name: private, a pointer, a property, an inline array element.</summary>
    NotNameable,

    /// <summary>A public field.</summary>
    Public,

    /// <summary>An internal field, nameable only from its own assembly (<see cref="MemberPlan.AccessAssembly"/>).</summary>
    Internal
}

/// <summary>One member of a struct's <see cref="TransferPlan.Blit"/> or <see cref="TransferPlan.Convert"/> plan.</summary>
/// <param name="Name">The member name (<see cref="StructAbiMember.Name"/>).</param>
/// <param name="HostOffset">The host offset, or <see langword="null"/> when the CLR decides it at run time.</param>
/// <param name="DeviceOffset">The device offset.</param>
/// <param name="ElementCount">The element count of an inline array or a fixed buffer, otherwise 1.</param>
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

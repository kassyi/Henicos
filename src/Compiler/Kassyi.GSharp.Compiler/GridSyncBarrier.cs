namespace Kassyi.GSharp.Compiler;

/// <summary>
/// The grid barrier that a <c>Gpu.SyncGrid(mode)</c> call asks for (the <c>GridSyncMode</c> of Kassyi.GSharp.Core), or
/// why it has none.
/// </summary>
internal enum GridSyncBarrier
{
    /// <summary><c>GridSyncMode.Deterministic</c>, also when the call passes no mode.</summary>
    Deterministic,

    /// <summary><c>GridSyncMode.Fast</c>.</summary>
    Fast,

    /// <summary>The mode is not a compile-time constant (GSH0012).</summary>
    NotConstant,

    /// <summary>The mode is a constant that names no member of <c>GridSyncMode</c> (GSH0003).</summary>
    Undefined
}

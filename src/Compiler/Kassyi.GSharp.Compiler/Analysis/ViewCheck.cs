namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The failure a member of an array view reports for a view without an array
/// (<see cref="ArrayViewMethods.GetChecks"/>), named as the <c>GSharpErrorCode</c> of the Prelude that carries it.
/// </summary>
internal enum ViewCheck : byte
{
    /// <summary>No check.</summary>
    None,

    /// <summary><see cref="InvalidOperationException"/>: a default <c>ArraySegment&lt;T&gt;</c>.</summary>
    InvalidOperation,

    /// <summary><see cref="NullReferenceException"/>: a default <c>ImmutableArray&lt;T&gt;</c>.</summary>
    NullReference,

    /// <summary><see cref="ArgumentNullException"/>: a null destination array.</summary>
    ArgumentNull,
}

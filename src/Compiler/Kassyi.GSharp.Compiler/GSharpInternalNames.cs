namespace Kassyi.GSharp.Compiler;

/// <summary>Identifiers the compiler emits into generated host and device code.</summary>
/// <remarks>
/// <para>
/// GSH0055 rejects user identifiers containing <c>__</c>, so names under the <c>__gsharp_</c> prefix cannot collide with
/// user code. The nested classes other than <see cref="Renamed"/> are generated from <c>Resources/InternalNames.yml</c>,
/// which also records which names the Prelude declares; the build checks those against <c>Resources/Prelude</c>.
/// </para>
/// <para>
/// A name belongs in the YAML when something outside one emitter also spells it: the Prelude, a native export, another
/// emitter, or both the host and the device code. A temporary whose declaration and uses one emitter writes may stay a
/// constant in that file, but it must not start with <see cref="Renamed.IdentifierPrefix"/> or
/// <see cref="Renamed.MemberPrefix"/>.
/// </para>
/// </remarks>
internal static partial class GSharpInternalNames
{
    /// <summary>Family prefixes for renamed user declarations (see CppNameResolver). No other name may start with them.</summary>
    internal static class Renamed
    {
        public const string IdentifierPrefix = GSharpNameEncoding.IdentifierPrefix;
        public const string MemberPrefix = GSharpNameEncoding.MemberPrefix;
    }

    internal static partial class StateMachine
    {
        /// <summary>Marks the current thread's state machine as finished.</summary>
        public const string Termination = State + " = -1;";
    }
}

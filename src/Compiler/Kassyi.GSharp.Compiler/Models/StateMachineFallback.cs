namespace Kassyi.GSharp.Compiler.Models;

/// <summary>
/// One kernel or suspending device function that stays on the CPU state machine under a stack switch.
/// </summary>
/// <param name="MethodName">Display name formatted as <c>Type.Method</c>.</param>
/// <param name="Reason">Why the method stays on the state machine.</param>
internal readonly record struct StateMachineFallback(string MethodName, StateMachineReason Reason)
{
    /// <summary>Whether this entry holds a recorded fallback.</summary>
    public bool HasValue => !string.IsNullOrEmpty(MethodName);

    /// <summary>Deterministic ordinal ordering by path kind, method name, and formatted reason.</summary>
    public sealed class Comparer : IComparer<StateMachineFallback>
    {
        public static Comparer Instance { get; } = new();

        public int Compare(StateMachineFallback x, StateMachineFallback y)
        {
            var pathCompare = StringComparer.Ordinal.Compare(x.Reason.PathName, y.Reason.PathName);
            if (pathCompare != 0)
            {
                return pathCompare;
            }

            var nameCompare = StringComparer.Ordinal.Compare(x.MethodName, y.MethodName);
            if (nameCompare != 0)
            {
                return nameCompare;
            }

            return StringComparer.Ordinal.Compare(x.Reason.FormatReason(), y.Reason.FormatReason());
        }
    }
}

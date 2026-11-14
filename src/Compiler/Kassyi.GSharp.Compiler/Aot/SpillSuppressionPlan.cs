using Kassyi.GSharp.Compiler.Models;
using Kassyi.GSharp.Cuda;

namespace Kassyi.GSharp.Compiler.Aot;

/// <summary>
/// The functions of a CUDA module whose register spills ptxas may report without a GS0001 warning: the kernels under
/// <c>#pragma warning disable GS0001</c> and the device functions that only those kernels reach.
/// </summary>
/// <remarks>
/// A device function that a kernel without the pragma also reaches keeps its warning: the pragma of one kernel does not
/// speak for the other. The build task compares whole names with the function that each ptxas line names
/// (<c>SpillSuppression</c> of <c>Kassyi.GSharp.Compiler.Tasks</c>), reading them from the line that
/// <see cref="SpillSuppressionLine.Marker"/> starts at the top of the module.
/// </remarks>
internal static class SpillSuppressionPlan
{
    /// <summary>The names whose spills are not reported, sorted; empty when no kernel is under the pragma.</summary>
    public static ImmutableArray<string> Create(IReadOnlyList<KernelSpillScope> kernels)
    {
        var suppressed = new SortedSet<string>(StringComparer.Ordinal);
        var reportedElsewhere = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kernel in kernels)
        {
            if (kernel.SuppressesSpill)
            {
                suppressed.Add(kernel.KernelName);
                AddAll(kernel.ReachedFunctions, suppressed);
            }
            else
            {
                AddAll(kernel.ReachedFunctions, reportedElsewhere);
            }
        }

        suppressed.ExceptWith(reportedElsewhere);
        return suppressed.ToImmutableArray();
    }

    /// <summary>The marker line for <paramref name="names"/>, or empty when there is none.</summary>
    public static string FormatLine(ImmutableArray<string> names) =>
        names.IsDefaultOrEmpty ? "" : SpillSuppressionLine.Marker + " " + string.Join(" ", names) + "\n";

    private static void AddAll(EquatableArray<string> names, ISet<string> into)
    {
        if (names.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var name in names)
        {
            into.Add(name);
        }
    }
}

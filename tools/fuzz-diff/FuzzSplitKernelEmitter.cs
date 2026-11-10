using System.Globalization;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>The kinds of split point a split kernel holds.</summary>
[Flags]
public enum FuzzSplitForms
{
    /// <summary>Not a split kernel.</summary>
    None = 0,

    /// <summary>Warp shuffles that read the calling lane, as operands, arguments, indices and compound-assignment
    /// values.</summary>
    Shuffle = 1,

    /// <summary>Block barriers between statements, in counted loops and in branches that every thread takes.</summary>
    Barrier = 2,

    /// <summary>A <c>foreach</c> over a device iterator, with side effects in both bodies and split points around
    /// it.</summary>
    Iterator = 4,

    /// <summary>Calls of device functions that reach a barrier or a shuffle, as arguments and operands.</summary>
    SuspendingCall = 8,

    /// <summary>Every kind.</summary>
    All = Shuffle | Barrier | Iterator | SuspendingCall,
}

/// <summary>
/// Generates split kernels: kernels with the points where the CPU lowering splits a kernel into states (warp shuffles,
/// block barriers, suspending calls) placed where C# order of evaluation matters, plus a device iterator consumed
/// between them. The kernels keep every value in the thread's own locals, read the calling lane in every shuffle and
/// put every split point in control flow that all threads take, so that <see cref="FuzzSplitOracle"/> can check them
/// against sequential C#.
/// </summary>
public static class FuzzSplitKernelEmitter
{
    /// <summary>
    /// Issue #991: the CPU paths misorder the operands around a split point in an arm of <c>?:</c>, <c>||</c> or
    /// <c>&amp;&amp;</c>, in an argument that writes what an earlier argument read, and on the right of a compound
    /// assignment that writes its target.
    /// </summary>
    public const int SplitArgumentOrderIssue = 991;

    /// <summary>Issue #1554: a stack array live across a barrier fails the native CPU build with no C#
    /// diagnostic.</summary>
    public const int StackArrayAcrossSplitIssue = 1554;

    /// <summary>In the default mix, one kernel in this many is a split kernel.</summary>
    public const int DefaultMixOneIn = 16;

    /// <summary>The non-suspending helper that adds to a variable through <see langword="ref"/>.</summary>
    public const string SideBump = "SideBump";

    /// <summary>The non-suspending helper that mixes three values.</summary>
    public const string SideMix = "SideMix";

    /// <summary>The suspending helper with a barrier between computing and returning.</summary>
    public const string SuspendSync = "SuspendSync";

    /// <summary>The suspending helper that writes through <see langword="ref"/> before its barrier.</summary>
    public const string SuspendRef = "SuspendRef";

    /// <summary>The suspending helper whose split point is a shuffle.</summary>
    public const string SuspendShuffle = "SuspendShuffle";

    /// <summary>The suspending helper with an <see langword="out"/> parameter.</summary>
    public const string SuspendOut = "SuspendOut";

    /// <summary>The device iterator with a loop and a <c>yield break</c>.</summary>
    public const string SplitIterA = "SplitIterA";

    /// <summary>The device iterator with straight-line <c>yield return</c>s and a call in its body.</summary>
    public const string SplitIterB = "SplitIterB";

    /// <summary>
    /// Returns the split forms of the kernel with <paramref name="kernelSeed"/> under <paramref name="focus"/>: <see
    /// cref="FuzzSplitForms.None"/> for an ordinary kernel. The draw uses its own stream of
    /// <paramref name="kernelSeed"/>, so an ordinary kernel is generated exactly as it is without split kernels.
    /// </summary>
    public static FuzzSplitForms ChooseForms(ulong kernelSeed, FuzzSwarmFocus focus)
    {
        var rng = new FuzzRandom(FuzzRandom.Mix(kernelSeed ^ 0x5B117C0DE5EEDUL));
        return focus switch
        {
            FuzzSwarmFocus.WarpShuffle => FuzzSplitForms.Shuffle,
            FuzzSwarmFocus.Barrier => FuzzSplitForms.Barrier,
            FuzzSwarmFocus.Iterator => FuzzSplitForms.Iterator,
            FuzzSwarmFocus.SuspendingCall => FuzzSplitForms.SuspendingCall,
            FuzzSwarmFocus.SplitPoints => ChooseRandomForms(rng),
            FuzzSwarmFocus.Default =>
                rng.NextBelow(DefaultMixOneIn) == 0 ? ChooseRandomForms(rng) : FuzzSplitForms.None,
            _ => FuzzSplitForms.None,
        };
    }

    /// <summary>Returns whether <paramref name="focus"/> generates split kernels only.</summary>
    public static bool IsSplitFocus(FuzzSwarmFocus focus) =>
        focus is FuzzSwarmFocus.WarpShuffle or FuzzSwarmFocus.Barrier or FuzzSwarmFocus.Iterator or
            FuzzSwarmFocus.SuspendingCall or FuzzSwarmFocus.SplitPoints;

    /// <summary>
    /// Builds the device functions that split kernels call: two plain helpers, four suspending functions and two
    /// iterators, with constants drawn from <paramref name="fuzzSeed"/>.
    /// </summary>
    public static IReadOnlyList<string> BuildHelperSources(ulong fuzzSeed) => FuzzSplitHelperSources.Build(fuzzSeed);

    /// <summary>Generates the body statements of a split kernel into <paramref name="state"/>.</summary>
    internal static void EmitSplitStatements(KernelGenerationState state, FuzzSplitForms forms)
    {
        var builder = new FuzzSplitKernelBuilder(state, forms);
        builder.EmitPrologue();
        int splitStatements = 2 + state.Size;
        for (int s = 0; s < splitStatements; s++)
        {
            builder.EmitSplitStatement();
            if (state.Rng.NextBool())
            {
                builder.EmitPlainStatement();
            }

            if (s % 3 == 2)
            {
                FuzzChecksumEmitter.EmitChecksumFoldSubset(state);
            }
        }

        builder.EmitFinalFolds();
    }

    /// <summary>Renders the method of a split kernel from the statements of <paramref name="state"/>.</summary>
    internal static string RenderKernelMethod(
        string kernelName,
        ulong kernelSeed,
        int size,
        FuzzSplitForms forms,
        KernelGenerationState state)
    {
        using var writer = new SourceWriter();
        writer.AppendLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"// fuzz-seed: {FuzzRandom.FormatSeed(kernelSeed)}, size: {size}, split: {forms}"));
        writer.AppendLine("[GpuKernel]");
        using (writer.Scope($"public static void {kernelName}(int[] inA, int[] inB, int[] inC, ulong[] checksum)"))
        {
            writer.AppendLine("int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;");
            writer.AppendLine(
                string.Create(CultureInfo.InvariantCulture, $"ulong hash = {ScalarFuzzGenerator.FnvOffsetBasis}UL;"));
            foreach (string statement in state.Statements)
            {
                writer.AppendLine(statement);
            }

            writer.AppendLine("checksum[gid] = hash;");
        }

        return writer.ToString().AsSpan().TrimEnd().ToString();
    }

    private static FuzzSplitForms ChooseRandomForms(FuzzRandom rng)
    {
        var forms = FuzzSplitForms.None;
        ReadOnlySpan<FuzzSplitForms> kinds =
        [
            FuzzSplitForms.Shuffle,
            FuzzSplitForms.Barrier,
            FuzzSplitForms.Iterator,
            FuzzSplitForms.SuspendingCall,
        ];
        foreach (var form in kinds)
        {
            if (rng.NextBool())
            {
                forms |= form;
            }
        }

        return forms != FuzzSplitForms.None
            ? forms
            : (FuzzSplitForms)(1 << rng.NextBelow(4));
    }

}

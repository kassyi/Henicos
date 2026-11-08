using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// What a call recorded by <see cref="MemoryUniformity"/> does to the memory where the graph is solved.
/// </summary>
internal static class MemoryCallEffects
{
    // What a call writes, after the barrier that a device function passes on every path. A device function with a
    // body leaves each memory as its own analysis finds it where it returns (written after its last barrier, or only
    // before one); any other call leaves what it may write written. A by-reference location is written whenever the
    // callee writes its parameter; the other memory only when the call may write memory (DeviceFunctionEffects)
    public static void Apply(MemoryValues values, MemoryReferences references, ImmutableArray<MemoryCallTarget> targets,
        (CalleeVariance Summary, bool WritesMemory) effect, ThreadVariance[] current)
    {
        var (summary, writesMemory) = effect;
        var analyzed = !summary.MemoryAtExit.IsDefault;
        if (analyzed && summary.BarrierOnEveryPath != ThreadVariance.Block)
        {
            values.Sync(
                summary.BarrierOnEveryPath == ThreadVariance.Uniform ? ThreadVariance.Uniform : ThreadVariance.Grid,
                current
            );
        }

        foreach (var target in targets)
        {
            switch (target)
            {
                case { Kind: MemoryCallTargetKind.ByReference, Operation: { } location }:
                    Write(MemoryResolver.Resolve(values, references, location),
                        analyzed ? AtExit(summary, target.Ordinal) : ThreadVariance.Block, current);
                    break;
                case { Kind: MemoryCallTargetKind.Receiver, Operation: { } receiver } when writesMemory:
                    Write(MemoryResolver.ResolveReference(values, references, receiver), ThreadVariance.Block,
                        current);
                    break;
                case { Kind: MemoryCallTargetKind.Argument, Operation: { } argument } when writesMemory:
                    Write(MemoryResolver.ResolveReference(values, references, argument),
                        analyzed ? AtExit(summary, target.Ordinal) : ThreadVariance.Block, current);
                    break;
                case { Kind: MemoryCallTargetKind.Field } when writesMemory:
                    current[target.Memory] |= analyzed ? summary.FieldMemoryAtExit : ThreadVariance.Block;
                    break;
            }
        }
    }

    // The summary of the call's callee and whether the call may write memory
    public static (CalleeVariance Summary, bool WritesMemory) GetEffect(IInvocationOperation invocation,
        UniformityValueReader reader, Compilation compilation)
    {
        var summary = reader.TryGetDeviceFunction(invocation.TargetMethod, out var callee, out var declaration)
            ? reader.GetCalleeSummary(callee, declaration, default)
            : default;
        var writesMemory = DeviceFunctionEffects.Classify(invocation.TargetMethod, compilation) ==
            DeviceFunctionEffect.Memory;
        return (summary, writesMemory);
    }

    /// <summary>Each of <paramref name="memories"/> written with <paramref name="written"/>.</summary>
    public static void Write(ImmutableArray<int> memories, ThreadVariance written, ThreadVariance[] current)
    {
        foreach (var memory in memories)
        {
            current[memory] |= written;
        }
    }

    // The callee's memory of a parameter where it returns; a parameter it has no entry for counts as written
    private static ThreadVariance AtExit(CalleeVariance summary, int ordinal) =>
        ordinal >= 0 && ordinal < summary.MemoryAtExit.Length ? summary.MemoryAtExit[ordinal] : ThreadVariance.Block;
}

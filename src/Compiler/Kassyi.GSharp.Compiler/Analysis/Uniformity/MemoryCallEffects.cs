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
    // callee writes its parameter; the element a struct member is called on when the member may write its receiver;
    // the other memory only when the call may write memory (DeviceFunctionEffects)
    public static void Apply(MemoryValues values, MemoryReferences references, ImmutableArray<MemoryCallTarget> targets,
        MemoryCallEffect effect, ThreadVariance[] current)
    {
        var summary = effect.Summary;
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
            ApplyTarget(values, references, target, effect, analyzed, current);
        }
    }

    private static void ApplyTarget(MemoryValues values, MemoryReferences references, MemoryCallTarget target,
        MemoryCallEffect effect, bool analyzed, ThreadVariance[] current)
    {
        var summary = effect.Summary;
        switch (target)
        {
            case { Kind: MemoryCallTargetKind.ByReference, Operation: { } location }:
                Write(MemoryResolver.Resolve(values, references, location),
                    analyzed ? AtExit(summary, target.Ordinal) : ThreadVariance.Block, current);
                break;
            case { Kind: MemoryCallTargetKind.ReceiverLocation, Operation: { } element }
                when effect.WritesReceiver || effect.WritesMemory:
                Write(MemoryResolver.Resolve(values, references, element), ThreadVariance.Block, current);
                break;
            case { Kind: MemoryCallTargetKind.Receiver, Operation: { } receiver } when effect.WritesMemory:
                Write(MemoryResolver.ResolveReference(values, references, receiver), ThreadVariance.Block, current);
                break;
            case { Kind: MemoryCallTargetKind.Argument, Operation: { } argument } when effect.WritesMemory:
                Write(MemoryResolver.ResolveReference(values, references, argument),
                    analyzed ? AtExit(summary, target.Ordinal) : ThreadVariance.Block, current);
                break;
            case { Kind: MemoryCallTargetKind.Field } when effect.WritesMemory:
                current[target.Memory] |= analyzed ? summary.FieldMemoryAtExit : ThreadVariance.Block;
                break;
        }
    }

    // What the call may write: the summary of its callee, whether it may write memory, and whether a struct member may
    // write its receiver (DeviceFunctionEffects: RefParameters covers a write through this)
    public static MemoryCallEffect GetEffect(IInvocationOperation invocation, UniformityValueReader reader,
        Compilation compilation)
    {
        var summary = reader.TryGetSummarizedCallee(invocation.TargetMethod, out var callee, out var declaration)
            ? reader.GetCalleeSummary(callee, declaration, default)
            : default;
        var classified = DeviceFunctionEffects.Classify(invocation.TargetMethod, compilation);
        return new MemoryCallEffect(summary, classified == DeviceFunctionEffect.Memory,
            classified != DeviceFunctionEffect.None);
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

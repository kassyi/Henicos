namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Why a kernel or suspending device function stays on the CPU state machine under a stack switch: one value shared
/// by the fallback list (<see cref="StateMachineFallbackPipeline"/>), eligibility queries, and diagnostics.
/// </summary>
/// <param name="Path">Which CPU path stays on the state machine.</param>
/// <param name="Name">The short PascalCase reason name (the enum name on native).</param>
/// <param name="Sentence">One English clause explaining why the method stays on the state machine.</param>
/// <param name="Detail">Optional detail appended after <paramref name="Name"/> in the fallback list.</param>
internal readonly record struct StateMachineReason(
    CpuPath Path,
    string Name,
    string Sentence,
    string? Detail = null)
{
    /// <summary>The lowercase path column written to the fallback list.</summary>
    public string PathName => Path switch
    {
        CpuPath.Native => "native",
        CpuPath.Managed => "managed",
        _ => throw new ArgumentOutOfRangeException(nameof(Path), Path, null)
    };

    /// <summary>Appends <c>Name[: Detail]</c> with line breaks and tabs normalized to spaces.</summary>
    public void AppendFormattedReason(in SourceWriter writer)
    {
        writer.Append(Name);
        if (Detail is not { Length: > 0 } detail)
        {
            return;
        }

        writer.Append(": ");
        foreach (var c in detail)
        {
            writer.Append(c is '\r' or '\n' or '\t' ? ' ' : c);
        }
    }

    /// <summary>Formats <c>Name[: Detail]</c> with line breaks and tabs normalized to spaces.</summary>
    public string FormatReason()
    {
        if (string.IsNullOrEmpty(Detail))
        {
            return Name;
        }

        using var writer = new SourceWriter();
        AppendFormattedReason(in writer);
        return writer.ToString();
    }

    /// <summary>
    /// Maps a <see cref="NativeStackObstacle"/> to its <see cref="StateMachineReason"/>, or <see langword="null"/> for
    /// <see cref="NativeStackObstacle.None"/> and <see cref="NativeStackObstacle.NoBlockBarrier"/>.
    /// </summary>
    public static StateMachineReason? ForNative(NativeStackObstacle obstacle, string? detail = null) => obstacle switch
    {
        NativeStackObstacle.None or NativeStackObstacle.NoBlockBarrier => null,
        NativeStackObstacle.GridSync => new(
            CpuPath.Native,
            nameof(NativeStackObstacle.GridSync),
            "it uses Gpu.SyncGrid()",
            detail),
        NativeStackObstacle.Cooperative => new(
            CpuPath.Native,
            nameof(NativeStackObstacle.Cooperative),
            "it is a cooperative kernel (Gpu.SyncGrid)",
            detail),
        NativeStackObstacle.RecursiveSuspendingCall => new(
            CpuPath.Native,
            nameof(NativeStackObstacle.RecursiveSuspendingCall),
            "it reaches a recursive suspending device function",
            detail),
        NativeStackObstacle.ImportedSuspendingCall => new(
            CpuPath.Native,
            nameof(NativeStackObstacle.ImportedSuspendingCall),
            "it calls a suspending device function from another assembly",
            detail),
        NativeStackObstacle.WarpLayout => new(
            CpuPath.Native,
            nameof(NativeStackObstacle.WarpLayout),
            "the visit counts of its warp collectives cannot be placed (a device function of another assembly or a " +
            "recursive one reaches a collective)",
            detail),
        NativeStackObstacle.WarpFrameTooLarge => new(
            CpuPath.Native,
            nameof(NativeStackObstacle.WarpFrameTooLarge),
            "its warp collectives need more visit counts per thread than the stack limit",
            detail),
        NativeStackObstacle.CaseValueWithoutLabel => new(
            CpuPath.Native,
            nameof(NativeStackObstacle.CaseValueWithoutLabel),
            "it has a switch case value that names no C++ label",
            detail),
        NativeStackObstacle.WarpOperatorMacro => new(
            CpuPath.Native,
            nameof(NativeStackObstacle.WarpOperatorMacro),
            "it expands an operator macro whose target reaches a warp collective",
            detail),
        NativeStackObstacle.WarpMethodGroup => new(
            CpuPath.Native,
            nameof(NativeStackObstacle.WarpMethodGroup),
            "it passes a suspending device function that reaches a warp collective as a method group",
            detail),
        NativeStackObstacle.UsingDeclarationWithActiveMask => new(
            CpuPath.Native,
            nameof(NativeStackObstacle.UsingDeclarationWithActiveMask),
            "it reaches Warp.ActiveMask() and declares a local with using: a jump of its reconvergence regions may " +
            "pass the declaration",
            detail),
        NativeStackObstacle.StackBodyLowering => new(
            CpuPath.Native,
            nameof(NativeStackObstacle.StackBodyLowering),
            "its body could not be lowered for the stack model",
            detail),
        _ => throw new ArgumentOutOfRangeException(nameof(obstacle), obstacle, null)
    };
}

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Emits the managed fallback body for a kernel running under the managed stack model
/// (<c>GSharpManagedBarrierModel=Stack</c>).
/// </summary>
internal static class ManagedStackLaunchEmitter
{
    /// <summary>Writes the stack-model launch call and return for <paramref name="kernel"/>.</summary>
    public static void Write(SourceWriter writer, in KernelHostModel kernel)
    {
        var plan = CSharpExecutionPlan.CreateForStack(kernel);
        if (plan.CannotRunReason is { } reason)
        {
            throw new InvalidOperationException(
                $"Kernel '{kernel.MessageName}' was marked stack-eligible but cannot run on the stack model: " +
                $"{reason}.");
        }

        var kernelNameLiteral = SymbolDisplay.FormatLiteral(kernel.MessageName, quote: true);
        CSharpExecutionEmitter.WriteStackRun(writer, plan, kernelNameLiteral, kernel.ReachesWarpShuffle);
        AutoResultEmitter.WriteReturn(writer, kernel);
        if (!kernel.ReturnsValue)
        {
            writer.AppendLine("return;");
        }
    }
}

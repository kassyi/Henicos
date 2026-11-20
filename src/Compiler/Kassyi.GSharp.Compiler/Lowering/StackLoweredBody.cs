namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// The body of a kernel or device function for the CPU stack models (<see cref="AstLoweringPipeline.LowerForStack"/>):
/// the body as the state machine has it right after its reconvergence regions and its labeled jumps, before it is
/// flattened into states.
/// </summary>
/// <remarks>
/// It holds the preparation of the lowering (the pre-lowering expansions, the labels detached from the statements that
/// hold a split point), the temporaries of the split pass of the IR and of the first round of
/// <see cref="ConditionalEvaluationRewriter"/> and <see cref="WarpInstructionRewriter"/>, the region calls of the stack
/// form with the gotos and labels of <see cref="ReconvergenceInserter"/>, and the gotos and labels of
/// <see cref="LabeledJumpRewriter"/>, so that every labeled jump names a target in the tree. It has no state, no frame
/// and no context: no state variable, state switch, hoisted local or resume point. The regions are those of the state
/// machine: the same calls, region numbers and places.
/// </remarks>
/// <param name="Body">The body, with the region calls of the stack form (<c>__gsharp_stack_warp_*</c>).</param>
/// <param name="RegionCount">
/// The reconvergence regions of <paramref name="Body"/>, numbered from 0; 0 when the method does not reach
/// <c>Warp.ActiveMask()</c>.
/// </param>
internal sealed record StackLoweredBody(BlockSyntax Body, int RegionCount);

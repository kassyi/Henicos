namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// The body of a kernel or device function for the CPU stack models (<see cref="AstLoweringPipeline.LowerForStack"/>):
/// its structure as written, with the operands of <c>&amp;&amp;</c>, <c>||</c> and <c>?:</c> and the warp collectives
/// turned into statements, and the reconvergence regions of a method that reaches <c>Warp.ActiveMask()</c> where the state
/// machine puts them. Nothing is flattened or split.
/// </summary>
/// <param name="Body">The body, with the region calls of the stack form (<c>__gsharp_stack_warp_*</c>).</param>
/// <param name="RegionCount">
/// The reconvergence regions of <paramref name="Body"/>, numbered from 0; 0 when the method does not reach
/// <c>Warp.ActiveMask()</c>.
/// </param>
internal sealed record StackLoweredBody(BlockSyntax Body, int RegionCount);

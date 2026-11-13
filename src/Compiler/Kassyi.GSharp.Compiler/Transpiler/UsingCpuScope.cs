namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>A <c>using</c> whose body is being written on the CPU (<see cref="Emitter.UsingScopes"/>).</summary>
/// <param name="Number">The number of its labels (<c>__gsharp_using_exit_&lt;n&gt;_&lt;exit&gt;</c>).</param>
/// <param name="Plan">The jumps that leave its body.</param>
internal sealed record UsingCpuScope(int Number, UsingExitPlan Plan);

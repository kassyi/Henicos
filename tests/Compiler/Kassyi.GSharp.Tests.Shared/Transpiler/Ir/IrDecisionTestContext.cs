#nullable enable
using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Shared factory for <see cref="IrDecisionContext"/> in IR decision tests (#1561).
/// </summary>
internal static class IrDecisionTestContext
{
    /// <summary>Creates a context for deciding the pre-split body <paramref name="method"/>.</summary>
    public static IrDecisionContext Create(Compilation compilation, IrMethod method,
        ITranspilerDialect? dialect = null) =>
        new(
            compilation,
            IrTemporaryInitializers.Create(method.Body),
            dialect ?? HostDialects.Cpu,
            method);
}

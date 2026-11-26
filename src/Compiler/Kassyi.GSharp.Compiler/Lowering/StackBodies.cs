using System.Runtime.ExceptionServices;

namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// The bodies of the methods that reach <c>Warp.ActiveMask()</c> lowered for the CPU stack models
/// (<see cref="AstLoweringPipeline.LowerForStack"/>), once per method and compilation, failures included.
/// </summary>
/// <remarks>
/// [WHY] The native stack eligibility lowers a body to learn whether it can run on stacks, and the stack form writes
/// the same body; one lowering serves both. A failure is kept too, so that the eligibility can state its reason and the
/// writer sees the same exception the lowering threw.
/// </remarks>
internal static class StackBodies
{
    private static readonly PerCompilationSymbolCache<IMethodSymbol, Result> s_results = new();

    /// <summary>
    /// The stack body of <paramref name="owner"/>, or the exception its lowering threw, as
    /// <see cref="AstLoweringPipeline.LowerForStack"/> takes them.
    /// </summary>
    public static Result Get(MethodDeclarationSyntax declaration, SemanticModel semanticModel, IMethodSymbol owner,
        bool isDeviceFunction)
    {
        var results = s_results.For(semanticModel.Compilation);
        if (results.TryGetValue(owner, out var cached))
        {
            return cached;
        }

        Result result;
        try
        {
            result = new Result(
                AstLoweringPipeline.LowerForStack(declaration, semanticModel, owner, isDeviceFunction), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result = new Result(null, ExceptionDispatchInfo.Capture(exception));
        }

        return results.GetOrAdd(owner, result);
    }

    /// <summary>The stack body of a method, or the failure of its lowering.</summary>
    /// <param name="Body">The body; <see langword="null"/> when the lowering failed.</param>
    /// <param name="Failure">The exception the lowering threw; <see langword="null"/> when it succeeded.</param>
    internal sealed record Result(StackLoweredBody? Body, ExceptionDispatchInfo? Failure)
    {
        /// <summary>
        /// The body, or the exception of the lowering thrown again with its original stack trace.
        /// </summary>
        public StackLoweredBody GetBody()
        {
            Failure?.Throw();
            return Body ?? throw new InvalidOperationException("A stack body without a failure has no body.");
        }

        /// <summary>
        /// The type and message of the failure, for the build output; <see langword="null"/> without one.
        /// </summary>
        public string? Reason =>
            Failure is { SourceException: var exception }
                ? exception.GetType().Name + ": " + exception.Message
                : null;
    }
}

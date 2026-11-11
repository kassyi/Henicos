namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>What a call of a function may write, besides its own locals and by-value parameters.</summary>
internal enum DeviceFunctionEffect
{
    /// <summary>Writes nothing that the caller can see.</summary>
    None,

    /// <summary>Writes only through its <c>ref</c> / <c>out</c> parameters (and, for a struct member, its receiver).</summary>
    RefParameters,

    /// <summary>May write any memory: array elements, pointers, shared memory, static fields, or calls that do.</summary>
    Memory
}

/// <summary>
/// Classifies what a call of a <c>[GpuDeviceFunction]</c>, a struct constructor or member, or an intrinsic may write
/// (<see cref="DeviceFunctionEffect"/>), so that <see cref="ExpressionEffects"/> knows which calls can change what
/// another operand of the same expression reads.
/// </summary>
/// <remarks>
/// <para>
/// A function with a body is classified by walking it: every assignment, increment, <c>ref</c> / <c>out</c> argument and
/// call is checked for the variable it writes. A write to a local or by-value parameter of the function (lambdas and
/// local functions inside it included) is invisible to the caller; a write through a <c>ref</c> / <c>out</c> parameter
/// (or to a field of the receiver of a struct member, or to the receiver of a <c>this ref</c> extension) is
/// <see cref="DeviceFunctionEffect.RefParameters"/>; anything else, a local or by-value parameter that a lambda or
/// local function captures from the function around it included, is <see cref="DeviceFunctionEffect.Memory"/>. A
/// constructor that chains with <c>: this(...)</c> also does what the constructor it calls does. A function in a call
/// cycle is <see cref="DeviceFunctionEffect.Memory"/>.
/// </para>
/// <para>
/// A function without a body (YAML mapped, <c>[CudaIntrinsic]</c>, metadata) is classified by its declaration: the
/// atomics, asynchronous copies, inline PTX, barriers, fences and warp operations are
/// <see cref="DeviceFunctionEffect.Memory"/>; a function with <c>ref</c> / <c>out</c> parameters is
/// <see cref="DeviceFunctionEffect.RefParameters"/>; the math of <c>System</c> (<c>Math</c>, <c>MathF</c>, the methods
/// of the primitive types), <c>System.Numerics</c>, <c>FastMath</c> and the constructors of value types are
/// <see cref="DeviceFunctionEffect.None"/>; any other is <see cref="DeviceFunctionEffect.Memory"/>.
/// </para>
/// <para>
/// [WHY] Every result is cached, also those found while another function is being classified: a function only gets a
/// result that depends on the walk when it is in a cycle, and every function in a cycle is
/// <see cref="DeviceFunctionEffect.Memory"/> whichever is queried first, so the cache does not depend on the order of
/// the queries.
/// </para>
/// </remarks>
internal static class DeviceFunctionEffects
{
    private static readonly PerCompilationSymbolCache<IMethodSymbol, DeviceFunctionEffect> s_effects = new();

    /// <summary>What a call of <paramref name="method"/> may write.</summary>
    public static DeviceFunctionEffect Classify(IMethodSymbol method, Compilation compilation)
    {
        var definition = GetDefinition(method);
        return s_effects.For(compilation).TryGetValue(definition, out var cached)
            ? cached
            : Classify(definition, compilation, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
    }

    /// <summary>
    /// Whether a call of <paramref name="method"/> (an accessor, an operator or a conversion, or none) may write memory
    /// (<see cref="DeviceFunctionEffect.Memory"/>).
    /// </summary>
    public static bool WritesMemory(IMethodSymbol? method, Compilation compilation) =>
        method != null && Classify(method, compilation) == DeviceFunctionEffect.Memory;

    // The declaration of a constructed generic method, or the static method of a reduced extension call
    internal static IMethodSymbol GetDefinition(IMethodSymbol method) => (method.ReducedFrom ?? method).OriginalDefinition;

    internal static DeviceFunctionEffect Classify(IMethodSymbol definition, Compilation compilation,
        HashSet<IMethodSymbol> visiting)
    {
        var cache = s_effects.For(compilation);
        if (cache.TryGetValue(definition, out var cached))
        {
            return cached;
        }

        if (!visiting.Add(definition))
        {
            return DeviceFunctionEffect.Memory;
        }

        var effect = DeviceFunctionBodies.TryGet(definition, compilation, out var bodies)
            ? ClassifyBodies(definition, bodies, compilation, visiting)
            : ImportedDeviceFunctionEffects.Classify(definition, compilation);
        visiting.Remove(definition);
        return cache.GetOrAdd(definition, effect);
    }

    /// <summary>
    /// What a call of <paramref name="lambda"/> (a node of the compilation) may write. A write to a local it captures is
    /// <see cref="DeviceFunctionEffect.Memory"/>, as for a local function: the caller treats both as able to touch any local.
    /// </summary>
    public static DeviceFunctionEffect ClassifyLambda(AnonymousFunctionExpressionSyntax lambda, SemanticModel model)
    {
        var compilation = model.Compilation;
        if (!compilation.ContainsSyntaxTree(lambda.SyntaxTree) ||
            compilation.GetCachedSemanticModel(lambda.SyntaxTree).GetSymbolInfo(lambda).Symbol is not IMethodSymbol method)
        {
            return DeviceFunctionEffect.Memory;
        }

        return ClassifyBodies(method, ImmutableArray.Create<SyntaxNode>(lambda.Body), compilation,
            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
    }

    private static DeviceFunctionEffect ClassifyBodies(IMethodSymbol method, ImmutableArray<SyntaxNode> bodies,
        Compilation compilation, HashSet<IMethodSymbol> visiting)
    {
        var effect = DeviceFunctionEffect.None;
        foreach (var body in bodies)
        {
            var model = compilation.GetCachedSemanticModel(body.SyntaxTree);
            foreach (var node in body.DescendantNodesAndSelf())
            {
                effect = Max(effect,
                    DeviceFunctionBodyWrites.ClassifyNode(node, method, body, model, compilation, visiting));
                if (effect == DeviceFunctionEffect.Memory)
                {
                    return effect;
                }
            }
        }

        return effect;
    }

    internal static DeviceFunctionEffect Max(DeviceFunctionEffect a, DeviceFunctionEffect b) => a > b ? a : b;
}

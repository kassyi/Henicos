using Kassyi.GSharp.Compiler.Ir.Bodies;

namespace Kassyi.GSharp.Compiler.Backends.Cpp;

/// <summary>
/// Caches the shared <see cref="IrBodyFront"/> of a method and up to one <see cref="IrBodyResult"/> per C++ route.
/// </summary>
internal sealed class IrBodyCache(IMethodSymbol method, Compilation compilation)
{
    private readonly IrBodyResult?[] _results = new IrBodyResult?[3];
    private readonly Type?[] _dialectTypes = new Type?[3];
    private IrBodyFront? _front;

    /// <summary>The dialect-independent front-end result, created once on first access.</summary>
    public IrBodyFront Front => _front ??= IrBodyFronts.Create(method, compilation, CancellationToken.None);

    /// <summary>
    /// Returns the cached <see cref="IrBodyResult"/> for <paramref name="profile"/>, lowering on first request for
    /// that route.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A different dialect type was passed for the same route, or the cached result does not match the requested route.
    /// </exception>
    public IrBodyResult For(CppBodyProfile profile)
    {
        var slot = SlotFor(profile.Route);
        var dialectType = profile.Dialect.GetType();
        if (_dialectTypes[slot] is { } existing && existing != dialectType)
        {
            throw new InvalidOperationException($"Route '{profile.Route}' was requested with another dialect type.");
        }

        var result = _results[slot];
        if (result is null)
        {
            _dialectTypes[slot] = dialectType;
            result = IrBodyLowering.TryLower(Front, profile, CancellationToken.None);
            _results[slot] = result;
        }

        if (result.Route != profile.Route || (result.Body is { } body && body.Profile.Route != profile.Route))
        {
            throw new InvalidOperationException(
                $"Cached IR body route '{result.Route}' does not match requested route '{profile.Route}'.");
        }

        return result;
    }

    private static int SlotFor(IrBodyRoute route) => route switch
    {
        IrBodyRoute.Cuda => 0,
        IrBodyRoute.NativeStack => 1,
        IrBodyRoute.NativePlain => 2,
        _ => throw new InvalidOperationException($"Unsupported C++ route '{route}'.")
    };
}

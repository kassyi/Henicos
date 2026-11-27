using Kassyi.GSharp.Compiler.Ir.Bodies;

namespace Kassyi.GSharp.Compiler.Backends.Cpp;

/// <summary>
/// The route shape of a C++ dialect: the only place in the C++ backend folder that inspects dialect capability flags
/// to choose a route and split policy.
/// </summary>
internal sealed class CppBodyProfile
{
    private CppBodyProfile(IrBodyRoute route, ITranspilerDialect dialect, IrSplitPolicy splits)
    {
        Route = route;
        Dialect = dialect;
        Splits = splits;
    }

    /// <summary>The backend route of the dialect.</summary>
    public IrBodyRoute Route { get; }

    /// <summary>The dialect the profile was created for.</summary>
    public ITranspilerDialect Dialect { get; }

    /// <summary>The split policy applied to scanned bodies on this route.</summary>
    public IrSplitPolicy Splits { get; }

    /// <summary>Returns the C++ body profile for <paramref name="dialect"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="dialect"/> does not match CUDA, stack-based native C++, or plain native C++.
    /// </exception>
    public static CppBodyProfile For(ITranspilerDialect dialect)
    {
        if (dialect.IsCuda)
        {
            return new CppBodyProfile(IrBodyRoute.Cuda, dialect, IrSplitPolicy.ExcludeEverySplit);
        }

        if (dialect.RunsThreadsOnOwnStacks())
        {
            return new CppBodyProfile(IrBodyRoute.NativeStack, dialect, IrSplitPolicy.ExcludeEverySplit);
        }

        if (dialect.EmulatesThreadsWithStateMachine())
        {
            return new CppBodyProfile(IrBodyRoute.NativePlain, dialect, IrSplitPolicy.ExcludeEverySplit);
        }

        throw new InvalidOperationException("Unsupported C++ dialect.");
    }
}

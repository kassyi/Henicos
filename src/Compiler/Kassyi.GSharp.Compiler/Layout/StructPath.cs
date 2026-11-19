namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>
/// The structs on the current path of a walk over struct field types (layouts, transfer classes, the C++ default
/// constructor of a local), so that the walk stops at a struct that holds itself.
/// </summary>
/// <remarks>
/// C# rejects a struct that holds itself by value (CS0523), but the IDE runs the analyzers and the generator on that
/// code while it is being typed, and Roslyn gives the field the struct's own type. A walk enters each struct with
/// <see cref="Enter"/> and passes <see cref="Step.Path"/> to the walks of its fields; the step leaves the struct when
/// it is disposed, so a struct held twice without a cycle (<c>Outer { Inner A; Inner B; }</c>) is walked both times.
/// A generic struct that holds a larger construction of itself (<c>G&lt;T&gt; { G&lt;G&lt;T&gt;&gt; Inner; }</c>, also
/// CS0523) never meets the same constructed type again, so a construction counts as on the path when the path holds
/// another construction of the same definition and that definition holds itself by value, as Roslyn checks it.
/// </remarks>
internal sealed class StructPath
{
    // [WHY] A walk starts for every struct the compiler lays out; the outermost step gives its empty set back, so a
    // thread allocates one set rather than one per walk
    [ThreadStatic]
    private static StructPath? t_spare;

    private readonly HashSet<INamedTypeSymbol> _structs = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// Enters <paramref name="structType"/> on <paramref name="path"/>, or on a new path when it is
    /// <see langword="null"/> (the start of a walk). <see cref="Step.IsCycle"/> tells that the struct is already on it.
    /// </summary>
    public static Step Enter(StructPath? path, INamedTypeSymbol structType)
    {
        var isOutermost = path is null;
        if (path is null)
        {
            path = t_spare ?? new StructPath();
            t_spare = null;
        }

        var isCycle = path.Contains(structType);
        if (!isCycle)
        {
            path._structs.Add(structType);
        }

        return new Step(path, isCycle ? null : structType, isOutermost);
    }

    /// <summary>Whether <paramref name="structType"/> is on the path: a field of its type closes a cycle.</summary>
    public bool Contains(INamedTypeSymbol structType) =>
        _structs.Contains(structType) || GrowingGenericStructs.HoldsAnotherConstructionByValue(structType, _structs);

    /// <summary>One struct entered on a <see cref="StructPath"/>; disposing it leaves the struct.</summary>
    public readonly ref struct Step
    {
        private readonly INamedTypeSymbol? _entered;
        private readonly bool _isOutermost;

        internal Step(StructPath path, INamedTypeSymbol? entered, bool isOutermost)
        {
            Path = path;
            _entered = entered;
            _isOutermost = isOutermost;
        }

        /// <summary>The path, to pass to the walks of the struct's fields.</summary>
        public StructPath Path { get; }

        /// <summary>Whether the struct was already on the path: the walk stops here.</summary>
        public bool IsCycle => _entered is null;

        public void Dispose()
        {
            if (_entered is not null)
            {
                Path._structs.Remove(_entered);
            }

            if (_isOutermost)
            {
                t_spare = Path;
            }
        }
    }
}

namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>A local, a parameter, <see langword="this"/> or a compiler temporary.</summary>
/// <remarks>
/// The identity of a variable is the instance: within one build every reference to the same symbol in the same region
/// holds the same <see cref="IrVariable"/>, and a temporary is a new instance each time a pass creates one. The C++
/// name of a temporary is not part of it; an emitter numbers temporaries in the order they appear.
/// </remarks>
internal sealed class IrVariable
{
    private IrVariable(IrVariableKind kind, ISymbol? symbol, ITypeSymbol type, RefKind refKind, string name, int region,
        IrSource source)
    {
        Kind = kind;
        Symbol = symbol;
        Type = type;
        RefKind = refKind;
        Name = name;
        Region = region;
        Source = source;
    }

    /// <summary>What the variable stands for.</summary>
    public IrVariableKind Kind { get; }

    /// <summary>The local or parameter; <see langword="null"/> for <see langword="this"/> and temporaries.</summary>
    public ISymbol? Symbol { get; }

    /// <summary>The declared type.</summary>
    public ITypeSymbol Type { get; }

    /// <summary>
    /// Whether the variable is a reference: a <see langword="ref"/> local or a <see langword="ref"/>,
    /// <see langword="in"/> or <see langword="out"/> parameter. <see langword="this"/> of a struct is
    /// <see cref="RefKind.Ref"/>, or <see cref="RefKind.In"/> in a <see langword="readonly"/> member (the members of a
    /// <see langword="readonly"/> struct included).
    /// </summary>
    public RefKind RefKind { get; }

    /// <summary>The symbol's name, <c>this</c>, or what a temporary is for; not a C++ name.</summary>
    public string Name { get; }

    /// <summary>The copy of an inlined body the variable is declared in; 0 outside any copy.</summary>
    public int Region { get; }

    /// <summary>Where the variable is declared (the method for <see langword="this"/>).</summary>
    public IrSource Source { get; }

    /// <summary>Whether the type is a ref struct (a span), which cannot be stored in a frame.</summary>
    public bool IsRefLike => Type.IsRefLikeType;

    /// <summary>Creates a temporary of a pass.</summary>
    /// <param name="type">The type of the temporary.</param>
    /// <param name="refKind">Whether the temporary holds a reference.</param>
    /// <param name="nameHint">What the temporary is for (not a C++ name).</param>
    /// <param name="source">The node the temporary is created for.</param>
    public static IrVariable CreateTemporary(ITypeSymbol type, RefKind refKind, string nameHint, IrSource source) =>
        new(IrVariableKind.Temporary, null, type, refKind, nameHint, source.Region, source);

    // Locals, parameters and this are created by IrVariableTable only, once per symbol and region
    internal static IrVariable CreateLocal(ILocalSymbol local, int region, IrSource source) =>
        new(IrVariableKind.Local, local, local.Type, local.RefKind, local.Name, region, source);

    internal static IrVariable CreateParameter(IParameterSymbol parameter, int region, IrSource source) =>
        new(IrVariableKind.Parameter, parameter, parameter.Type, parameter.RefKind, parameter.Name, region, source);

    internal static IrVariable CreateThis(IMethodSymbol method, int region, IrSource source)
    {
        var type = method.ContainingType;
        var refKind = !type.IsValueType ? RefKind.None : method.IsReadOnly ? RefKind.In : RefKind.Ref;
        return new IrVariable(IrVariableKind.This, null, type, refKind, "this", region, source);
    }
}

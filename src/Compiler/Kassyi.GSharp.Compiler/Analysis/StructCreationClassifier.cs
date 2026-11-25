namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// How a struct creation <c>new T(args)</c> reaches the device, from its symbols and the dialect alone, and whether its
/// written arguments keep the parameter order: the syntax path and the IR read the same answers.
/// </summary>
internal static class StructCreationClassifier
{
    /// <summary>
    /// How <c>new <paramref name="typeSymbol"/>(args)</c> of a struct with <paramref name="constructor"/> reaches the
    /// device in <paramref name="dialect"/>; <paramref name="mappedFunction"/> is the function of
    /// <see cref="StructCreationKind.Mapped"/>, empty otherwise.
    /// </summary>
    public static StructCreationKind Classify(ITypeSymbol typeSymbol, IMethodSymbol? constructor,
        Compilation compilation, ITranspilerDialect dialect, out string mappedFunction)
    {
        mappedFunction = string.Empty;
        if (constructor != null && StructConstructorEmitter.IsEmittedStructConstructor(constructor, compilation))
        {
            return StructCreationKind.Factory;
        }

        // The implicit parameterless constructor is default(T): no initializer or body runs
        if (constructor is null or { Parameters.Length: 0, IsImplicitlyDeclared: true })
        {
            return StructCreationKind.Braces;
        }

        if (dialect.TryMapIntrinsic(constructor, out mappedFunction))
        {
            return StructCreationKind.Mapped;
        }

        // A declared parameterless constructor whose body is not here (another assembly without a record) is not
        // positional, so it is Unsupported: T{} would skip the body and the field initializers (#609)
        return IsPositionalConstructor(typeSymbol, constructor)
            ? StructCreationKind.Braces
            : StructCreationKind.Unsupported;
    }

    // [WHY] A constructor from metadata has no body to emit, so T{args} is right only where G# knows the meaning: the
    // component constructors of the vector types (System.Numerics.Vector2/3/4, Kassyi.GSharp.Core.Float3 ...), whose
    // parameters are the fields in order; Nullable<T>(T), which the C++ constructor of the device type takes; and a
    // span over a pointer and a length, or over an array, which the C++ constructors of GSharpArray take.
    private static bool IsPositionalConstructor(ITypeSymbol typeSymbol, IMethodSymbol constructor)
    {
        var parameters = constructor.Parameters;
        if (typeSymbol.IsSystemNumericsVector() && typeSymbol is INamedTypeSymbol vectorType)
        {
            var index = 0;
            foreach (var member in StructAbiMembers.Get(vectorType))
            {
                if (index >= parameters.Length || !parameters[index].Type.IsEqualTo(member.Type))
                {
                    return false;
                }

                index++;
            }

            return index == parameters.Length;
        }

        if (typeSymbol.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return parameters.Length == 1;
        }

        if (typeSymbol.IsArrayLikeType())
        {
            return parameters switch
            {
                [{ Type: IPointerTypeSymbol }, { Type.SpecialType: SpecialType.System_Int32 }] => true,
                [var source] => source.Type.IsArrayLikeType(),
                _ => false
            };
        }

        return false;
    }

    /// <summary>
    /// Whether the arguments, whose parameters are <paramref name="ordinals"/> in the written order (-1 for none), are
    /// written one per parameter of the <paramref name="parameterCount"/>, in the parameter order.
    /// </summary>
    public static bool IsInParameterOrder(int parameterCount, ReadOnlySpan<int> ordinals)
    {
        if (ordinals.Length != parameterCount)
        {
            return false;
        }

        for (var i = 0; i < ordinals.Length; i++)
        {
            if (ordinals[i] != i)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the arguments, whose parameters are <paramref name="ordinals"/> in the written order, each go to a later
    /// parameter than the one before it: a call can pass them as written.
    /// </summary>
    public static bool IsWrittenInParameterOrder(ReadOnlySpan<int> ordinals)
    {
        var previous = -1;
        foreach (var index in ordinals)
        {
            if (index < previous)
            {
                return false;
            }

            previous = index;
        }

        return true;
    }
}

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Writes, in front of a kernel entry point, a <c>static_assert</c> of the size and of the value offset of each
/// <c>System_Nullable&lt;T&gt;</c> that the kernel's parameters and return value carry, so that the C++ compiler checks
/// the layout that the launcher's <c>Nullable&lt;T&gt;</c> shadow struct is built from (<c>NullableShadowPlan</c>).
/// </summary>
internal static class NullableLayoutAssertEmitter
{
    /// <summary>
    /// The <c>Nullable&lt;T&gt;</c> types that cross the boundary as parameters, elements of array-like parameters or
    /// the return value of <paramref name="kernel"/>, each once, with their device layout under <paramref name="dialect"/>.
    /// </summary>
    public static ImmutableArray<(ITypeSymbol Type, int Size, int ValueOffset)> Plan(IMethodSymbol kernel,
        ITranspilerDialect dialect)
    {
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var asserts = ImmutableArray.CreateBuilder<(ITypeSymbol, int, int)>();
        foreach (var parameter in kernel.Parameters)
        {
            Add(parameter.Type, dialect, seen, asserts);
        }

        Add(kernel.ReturnType, dialect, seen, asserts);
        return asserts.ToImmutable();
    }

    /// <summary>Writes the asserts of <see cref="Plan"/>.</summary>
    // static_assert(sizeof(System_Nullable<T>)==<D>,"<T?>: size");
    // static_assert(offsetof(System_Nullable<T>,Value)==<offset>,"<T?>: value offset");
    public static void Write(IMethodSymbol kernel, ref TranspileContext context)
    {
        foreach (var (type, size, valueOffset) in Plan(kernel, context.Emitter.Dialect))
        {
            var name = TypeLayoutCalculator.FormatTypeName(type);
            WriteAssert(type, "sizeof", null, $"=={size},\"{name}: size\"", ref context);
            WriteAssert(type, "offsetof", ",Value", $"=={valueOffset},\"{name}: value offset\"", ref context);
        }
    }

    // static_assert(<operator>(<type><operand>)<condition>);
    private static void WriteAssert(ITypeSymbol type, string op, string? operand, string condition,
        ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        writer.Append("static_assert");
        using (writer.Parentheses())
        {
            writer.Append(op);
            using (writer.Parentheses())
            {
                context.Emitter.Dialect.AppendType(ref context, type);
                writer.AppendIf(operand is not null, operand);
            }

            writer.Append(condition);
        }

        writer.AppendLine(";");
    }

    private static void Add(ITypeSymbol type, ITranspilerDialect dialect, HashSet<ITypeSymbol> seen,
        ImmutableArray<(ITypeSymbol, int, int)>.Builder asserts)
    {
        var marshaled = type.UnwrapNullableArrayLike() ?? type;
        if (marshaled.TryGetArrayLikeElementType(out var element))
        {
            marshaled = element;
        }

        if (!HostShadowStructs.TryGetNullableValue(marshaled, out var value) || !seen.Add(marshaled))
        {
            return;
        }

        var valueLayout = TypeLayoutCalculator.GetDeviceLayout(value, dialect);
        asserts.Add((
            marshaled,
            TypeLayoutCalculator.GetDeviceLayout(marshaled, dialect).Size,
            TypeLayoutCalculator.AlignUp(1, valueLayout.Alignment)
        ));
    }
}

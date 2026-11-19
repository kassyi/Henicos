namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Writes the declaration of the memory that a <c>new SharedMemory&lt;T&gt;(n)</c> creates, under the name of a local
/// or a generated name (<see cref="SharedMemoryName"/>): a static region (<c>__shared__</c>) or a placement in the
/// dynamic buffer on CUDA, an allocation by site key on the CPU dialects.
/// </summary>
internal static class SharedMemoryDeclarationWriter
{
    /// <param name="name">The name of the declared pointer.</param>
    /// <param name="creation">The creation; it supplies the count and the site key.</param>
    /// <param name="elementType">The element type <c>T</c>.</param>
    /// <param name="splitDeclaration">
    /// Whether a <c>goto</c> may jump past the declaration, which C++ forbids for an initialized one; the CPU dialects
    /// then declare the pointer and assign it in two statements.
    /// </param>
    public static void Write(SharedMemoryName name, BaseObjectCreationExpressionSyntax creation,
        ITypeSymbol elementType, bool splitDeclaration, ref TranspileContext context)
    {
        if (!context.Emitter.Dialect.IsCuda)
        {
            WriteCpu(name, creation, elementType, splitDeclaration, ref context);
            return;
        }

        if (!DynamicSharedMemoryAnalysis.TryGetCount(creation, context.Query.SemanticModel, out var count,
                out var constantCount))
        {
            return;
        }

        if (constantCount == null)
        {
            DynamicSharedMemoryEmitter.WritePlacement(name, creation, count, elementType, ref context);
        }
        else
        {
            WriteStatic(name, count, constantCount.Value, elementType, ref context);
        }
    }

    // <T>* <name> = <allocation>;
    private static void WriteCpu(SharedMemoryName name, BaseObjectCreationExpressionSyntax creation,
        ITypeSymbol elementType, bool splitDeclaration, ref TranspileContext context)
    {
        context.Emitter.Dialect.AppendType(ref context, elementType);
        context.Emitter.Writer.Append("* ");
        name.Append(ref context);
        if (splitDeclaration)
        {
            // A goto may jump past the declaration, and C++ forbids that jump past an initialization:
            // <T>* <name>;
            // <name> = <allocation>;
            context.Emitter.Writer.AppendLine(";");
            name.Append(ref context);
        }

        context.Emitter.Writer.Append(" = ");
        SharedMemorySiteKey.AppendCpuAllocation(creation, elementType, ref context);
        context.Emitter.Writer.Append(';');
    }

    // __shared__ <T> <name>[<n>];
    // [WHY] C++ has no array of 0 elements, so an empty declaration (a count of 0, like new int[0]) is declared with
    // one element, as the CPU paths allocate at least one byte. C# reaches no element of it either way: the count
    // stays 0
    private static void WriteStatic(SharedMemoryName name, ExpressionSyntax count, int constantCount,
        ITypeSymbol elementType, ref TranspileContext context)
    {
        context.Emitter.Writer.Append("__shared__ ");
        context.Emitter.Dialect.AppendType(ref context, elementType);
        context.Emitter.Writer.Append(' ');
        name.Append(ref context);
        using (context.Emitter.Writer.Brackets())
        {
            if (constantCount == 0)
            {
                context.Emitter.Writer.Append('1');
            }
            else
            {
                context.Visitor.Visit(count);
            }
        }

        context.Emitter.Writer.Append(";");
    }
}

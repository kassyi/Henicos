namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Computes the compile-time key that identifies a <c>SharedMemory&lt;T&gt;</c> declaration in the CPU and managed
/// dialects.
/// </summary>
/// <remarks>
/// [WHY] On the GPU a <c>__shared__</c> variable has static storage: one buffer per declaration per block, shared by every
/// call of the function that declares it, from any call site. The CPU and managed dialects allocate shared memory at run
/// time, so they look the buffer up by this key. A key per call site, or a counter of allocations in execution order,
/// would give a callee called from two places two buffers, or give a caller and a callee the same one, and the results
/// would differ from the GPU.
/// <para>
/// The key is the FNV-1a 64-bit hash of <c>"&lt;documentation comment id of the method&gt;#&lt;ordinal&gt;"</c>, where the
/// ordinal counts the <c>SharedMemory&lt;T&gt;</c> creations of the method in source order. The ordinal, unlike a source
/// position, does not change when unrelated lines of the file are edited.
/// </para>
/// </remarks>
internal static class SharedMemorySiteKey
{
    /// <summary>Computes the key of a <c>SharedMemory&lt;T&gt;</c> creation, which may be a node of a lowered body.</summary>
    public static ulong Compute(SemanticModel semanticModel, BaseObjectCreationExpressionSyntax creation)
    {
        var original = semanticModel.GetOriginalNode(creation) as BaseObjectCreationExpressionSyntax
                       ?? throw new InvalidOperationException("A shared memory creation is not mapped to its source declaration.");
        var method = original.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>()
                     ?? throw new InvalidOperationException("A shared memory creation is not inside a method.");
        var methodId = semanticModel.GetDeclaredSymbol(method)?.GetDocumentationCommentId()
                       ?? throw new InvalidOperationException("The method that creates shared memory has no symbol.");

        var ordinal = 0;
        foreach (var node in method.DescendantNodes())
        {
            if (node == original)
            {
                return HashHelper.ComputeFnv1A64($"{methodId}#{ordinal}");
            }

            if (node is BaseObjectCreationExpressionSyntax other && semanticModel.GetTypeInfo(other).Type.IsSharedMemoryType())
            {
                ordinal++;
            }
        }

        throw new InvalidOperationException("A shared memory creation was not found in its method.");
    }

    /// <summary>Formats a key as a C++ <c>unsigned long long</c> literal.</summary>
    public static string ToCppLiteral(ulong key) => $"0x{key:x16}ull";

    /// <summary>Formats a key as a C# <see cref="ulong"/> literal.</summary>
    public static string ToCSharpLiteral(ulong key) => $"0x{key:x16}UL";

    /// <summary>
    /// Appends the CPU allocation of a <c>SharedMemory&lt;T&gt;</c> creation (#544). A constant count (or none) is a
    /// static declaration, as on CUDA (<c>__shared__</c>, not counted in <c>sharedMemBytes</c>):
    /// <c>(&lt;T&gt;*)__gsharp_allocate_smem_static(&lt;siteKey&gt;, (&lt;n&gt;) * sizeof(&lt;T&gt;), alignof(&lt;T&gt;))</c>.
    /// Any other count is a dynamic declaration, placed in the launch's <c>sharedMemBytes</c>:
    /// <c>(&lt;T&gt;*)__gsharp_allocate_smem_dynamic(&lt;siteKey&gt;, (&lt;n&gt;) * sizeof(&lt;T&gt;), alignof(&lt;T&gt;), "&lt;place&gt;")</c>.
    /// The decision is <see cref="DynamicSharedMemoryAnalysis.TryGetCount(BaseObjectCreationExpressionSyntax, SemanticModel, out ExpressionSyntax?, out int?)"/>,
    /// the one that chooses between <c>__shared__</c> and the dynamic placement on CUDA.
    /// </summary>
    public static void AppendCpuAllocation(
        BaseObjectCreationExpressionSyntax creation,
        ITypeSymbol elementType,
        ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        var siteKey = ToCppLiteral(Compute(model, creation));
        var isDynamic = DynamicSharedMemoryAnalysis.TryGetCount(creation, model, out var count, out var constantCount) &&
                        constantCount == null;

        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Dialect.AppendType(ref context, elementType);
            context.Emitter.Writer.Append('*');
        }

        context.Emitter.Writer.Append(isDynamic
            ? GSharpInternalNames.Prelude.AllocateDynamicSharedMemory
            : GSharpInternalNames.Prelude.AllocateStaticSharedMemory);
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Writer.Append($"{siteKey}, ");
            using (context.Emitter.Writer.Parentheses())
            {
                if (count != null)
                {
                    context.Visitor.Visit(count);
                }
                else
                {
                    context.Emitter.Writer.Append("1");
                }
            }

            context.Emitter.Writer.Append(" * sizeof");
            using (context.Emitter.Writer.Parentheses())
            {
                context.Emitter.Dialect.AppendType(ref context, elementType);
            }

            context.Emitter.Writer.Append(", alignof");
            using (context.Emitter.Writer.Parentheses())
            {
                context.Emitter.Dialect.AppendType(ref context, elementType);
            }

            // The place of the declaration names it in the error of a launch whose sharedMemBytes is too small
            if (isDynamic)
            {
                context.Emitter.Writer.Append(", ");
                CppStringLiteral.Append(context.Emitter.Writer, SourcePlace.Format(model, creation));
            }
        }
    }
}

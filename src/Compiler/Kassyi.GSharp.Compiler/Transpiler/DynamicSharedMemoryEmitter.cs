namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Emits the CUDA placement of dynamic <c>SharedMemory&lt;T&gt;</c> declarations: the table type of a kernel, the table
/// that the kernel owns, the hidden table parameter and argument of the device functions that reach a declaration, and
/// the placement of each declaration.
/// </summary>
/// <remarks>
/// [WHY] A dynamic declaration is one region per block, as a <c>__shared__</c> variable is, whichever function declares
/// it (#439). The regions are carved out of the one dynamic buffer of the Prelude (<c>__gsharp_dynamic_smem</c>) in the
/// order a thread first reaches them, so a device function cannot compute its offsets alone. Each thread keeps a table
/// with one slot per declaration that the kernel reaches (<see cref="DynamicSharedMemoryAnalysis.GetReachableSites"/>);
/// the slot is a compile-time constant, so no lookup happens at run time. The CPU dialects keep the site-key lookup of
/// <see cref="SharedMemorySiteKey.AppendCpuAllocation"/>.
/// </remarks>
internal static class DynamicSharedMemoryEmitter
{
    // The type of the table that a device function receives: the table type of the kernel that calls it
    private const string TableTypeParameter = "TDsmem";

    // [WHY] A Prelude macro, upper case like the other C++ macros; InternalNames.yml holds only __gsharp_ names.
    private const string AlignMacro = "__GSHARP_DSMEM_ALIGN";

    /// <summary>
    /// Whether <paramref name="method"/> is emitted for <paramref name="dialect"/> with the hidden table parameter: a
    /// CUDA device function that reaches a dynamic declaration.
    /// </summary>
    public static bool TakesTable(IMethodSymbol method, ITranspilerDialect dialect, Compilation compilation) =>
        dialect.IsCuda && DynamicSharedMemoryAnalysis.ReachesDynamicSharedMemory(method, compilation);

    /// <summary>
    /// Writes the table type of a CUDA kernel that reaches dynamic declarations and makes the table visible to the body
    /// (<see cref="Emitter.DynamicSharedMemorySlotScope"/>); for a device function that takes the table, only the
    /// latter. Writes nothing for any other method.
    /// </summary>
    public static void BeginMethod(in MethodEmissionPlan plan, ref TranspileContext context)
    {
        if (!context.Emitter.Dialect.IsCuda || plan.Symbol is not { } method)
        {
            return;
        }

        var compilation = context.Query.SemanticModel.Compilation;
        if (plan.Kind != MethodEmissionKind.Kernel)
        {
            if (DynamicSharedMemoryAnalysis.ReachesDynamicSharedMemory(method, compilation))
            {
                // A dependent name: the table type is a template parameter
                context.Emitter.DynamicSharedMemorySlotScope = $"{TableTypeParameter}::template ";
            }

            return;
        }

        var sites = DynamicSharedMemoryAnalysis.GetReachableSites(method, compilation);
        if (sites.IsEmpty)
        {
            return;
        }

        var tableType = GetTableTypeName(method);
        WriteTableType(method.Name, tableType, sites, ref context);
        context.Emitter.DynamicSharedMemorySlotScope = tableType + "::";
    }

    /// <summary>
    /// Writes the first lines of the body of a CUDA kernel that reaches dynamic declarations:
    /// <c>__gsharp_dsmem_&lt;kernel&gt; __gsharp_dsmem_storage; __gsharp_dsmem_&lt;kernel&gt;* __gsharp_dsmem = &amp;__gsharp_dsmem_storage; __gsharp_dsmem_init(__gsharp_dsmem);</c>
    /// </summary>
    public static void WriteKernelPrologue(in MethodEmissionPlan plan, ref TranspileContext context)
    {
        if (plan.Kind != MethodEmissionKind.Kernel || plan.Symbol is not { } kernel ||
            context.Emitter.DynamicSharedMemorySlotScope == null)
        {
            return;
        }

        var tableType = GetTableTypeName(kernel);
        context.Emitter.Writer.AppendLine($"{tableType} {GSharpInternalNames.DynamicSharedMemory.TableStorage};");
        context.Emitter.Writer.AppendLine(
            $"{tableType}* {GSharpInternalNames.DynamicSharedMemory.Table} = &{GSharpInternalNames.DynamicSharedMemory.TableStorage};");
        context.Emitter.Writer.AppendLine(
            $"{GSharpInternalNames.Prelude.DynamicSharedMemoryInit}({GSharpInternalNames.DynamicSharedMemory.Table});");
    }

    /// <summary>Appends <c>template &lt;typename TDsmem&gt; </c>, the template header of a device function that takes the table.</summary>
    public static void AppendTemplateHeader(ref TranspileContext context) =>
        context.Emitter.Writer.Append($"template <typename {TableTypeParameter}> ");

    /// <summary>Appends <c>TDsmem* __gsharp_dsmem</c>, the hidden last parameter of a device function that takes the table.</summary>
    public static void AppendTableParameter(bool hasParameters, ref TranspileContext context)
    {
        context.Emitter.Writer.AppendIf(hasParameters, ", ");
        context.Emitter.Writer.Append($"{TableTypeParameter}* {GSharpInternalNames.DynamicSharedMemory.Table}");
    }

    /// <summary>
    /// Appends <c>__gsharp_dsmem</c> as the last argument of a call to <paramref name="callee"/> when the callee takes
    /// the table. The call fails (GSH0003) when the calling code has no table to pass: a struct member, for example.
    /// </summary>
    public static void AppendTableArgument(InvocationExpressionSyntax call, IMethodSymbol callee, ref TranspileContext context)
    {
        if (!TakesTable(callee, context.Emitter.Dialect, context.Query.SemanticModel.Compilation))
        {
            return;
        }

        if (context.Emitter.DynamicSharedMemorySlotScope == null)
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, call, DynamicSharedMemoryAnalysis.ReachedWithoutTable);
            return;
        }

        context.Emitter.Writer.AppendIf(callee.Parameters.Length > 0, ",");
        context.Emitter.Writer.Append(GSharpInternalNames.DynamicSharedMemory.Table);
    }

    /// <summary>
    /// Records a failure (GSH0003) when <paramref name="reference"/> names <paramref name="method"/> without calling it
    /// (a method group) and the method takes the table: the hidden argument cannot be passed through a delegate.
    /// </summary>
    public static void FailIfReferencedWithoutCall(ExpressionSyntax reference, IMethodSymbol method,
        ref TranspileContext context)
    {
        var called = reference.Parent is InvocationExpressionSyntax invocation && invocation.Expression == reference ||
                     reference.Parent is MemberAccessExpressionSyntax access && access.Name == reference &&
                     access.Parent is InvocationExpressionSyntax outer && outer.Expression == access;
        if (!called && TakesTable(method, context.Emitter.Dialect, context.Query.SemanticModel.Compilation))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, reference, DynamicSharedMemoryAnalysis.PassedAsDelegate);
        }
    }

    /// <summary>
    /// Writes the CUDA declaration of a dynamic <c>SharedMemory&lt;T&gt;</c>:
    /// <c>&lt;T&gt;* &lt;name&gt; = (&lt;T&gt;*)__gsharp_dsmem_place&lt;&lt;scope&gt;Slot&lt;&lt;site key&gt;&gt;&gt;(__gsharp_dsmem, (unsigned long long)(long long)(&lt;n&gt;) * sizeof(&lt;T&gt;), __GSHARP_DSMEM_ALIGN(&lt;T&gt;));</c>
    /// </summary>
    /// <remarks>
    /// [WHY] The count is widened through <c>long long</c>, so a negative count becomes a size of at least 2^63 bytes,
    /// beyond any <c>sharedMemBytes</c>. <c>__gsharp_dsmem_place</c> compares that size with <c>sharedMemBytes</c>
    /// before it adds the offset, which could otherwise wrap past 2^64 to a small end, and traps.
    /// </remarks>
    public static void WritePlacement(VariableDeclaratorSyntax declarator, BaseObjectCreationExpressionSyntax creation,
        ExpressionSyntax count, ITypeSymbol elementType, ref TranspileContext context)
    {
        if (context.Emitter.DynamicSharedMemorySlotScope is not { } slotScope)
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, declarator, DynamicSharedMemoryAnalysis.ReachedWithoutTable);
            return;
        }

        var siteKey = SharedMemorySiteKey.ToCppLiteral(SharedMemorySiteKey.Compute(context.Query.SemanticModel, creation));
        context.Emitter.Dialect.AppendType(ref context, elementType);
        context.Emitter.Writer.Append("* ");
        CppNameResolver.AppendDeclaredName(declarator, declarator.Identifier, ref context);
        context.Emitter.Writer.Append(" = ");
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Dialect.AppendType(ref context, elementType);
            context.Emitter.Writer.Append('*');
        }

        context.Emitter.Writer.Append($"{GSharpInternalNames.Prelude.DynamicSharedMemoryPlace}<{slotScope}Slot<{siteKey}>>");
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Writer.Append($"{GSharpInternalNames.DynamicSharedMemory.Table}, (unsigned long long)(long long)");
            using (context.Emitter.Writer.Parentheses())
            {
                context.Visitor.Visit(count);
            }

            context.Emitter.Writer.Append(" * sizeof");
            using (context.Emitter.Writer.Parentheses())
            {
                context.Emitter.Dialect.AppendType(ref context, elementType);
            }

            context.Emitter.Writer.Append($", {AlignMacro}");
            using (context.Emitter.Writer.Parentheses())
            {
                context.Emitter.Dialect.AppendType(ref context, elementType);
            }
        }

        context.Emitter.Writer.Append(';');
    }

    private static string GetTableTypeName(IMethodSymbol kernel) =>
        GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.DynamicSharedMemory.TableTypePrefix, kernel);

    // struct <table type> { static constexpr int Slots = <n>; template <unsigned long long Key> static constexpr int Slot = Key == <site 0> ? 0 : ... : -1; unsigned int cursor; unsigned int offset[<n>]; unsigned int size[<n>]; };
    private static void WriteTableType(string kernelName, string tableType, ImmutableArray<ulong> sites,
        ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        writer.Append($"// {kernelName} reaches {sites.Length} dynamic shared memory declaration");
        writer.AppendIf(sites.Length != 1, "s");
        writer.AppendLine();
        using (writer.Scope("struct " + tableType + " {", "};"))
        {
            writer.AppendLine($"static constexpr int Slots = {sites.Length};");
            writer.Append("template <unsigned long long Key> static constexpr int Slot = ");
            for (var slot = 0; slot < sites.Length; slot++)
            {
                writer.Append($"Key == {SharedMemorySiteKey.ToCppLiteral(sites[slot])} ? {slot} : ");
            }

            writer.AppendLine("-1;");
            writer.AppendLine("unsigned int cursor;");
            writer.AppendLine($"unsigned int offset[{sites.Length}];");
            writer.AppendLine($"unsigned int size[{sites.Length}];");
        }
    }
}

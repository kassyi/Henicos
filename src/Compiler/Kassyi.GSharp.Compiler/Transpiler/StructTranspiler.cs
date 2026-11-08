namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The entry point of the device struct definitions: writes the definition of one struct
/// (<see cref="StructDefinitionEmitter"/>), the definitions a method transpilation writes in front of the method, and a
/// struct exported on its own.
/// </summary>
internal static class StructTranspiler
{
    /// <summary>
    /// Emits the C++ struct definition for the specified named struct symbol, handling InlineArray structures and operator overloads.
    /// </summary>
    public static void WriteStructDefinition(INamedTypeSymbol structSym, ref TranspileContext context)
    {
        var previousStructType = context.Emitter.CurrentStructType;
        context.Emitter.CurrentStructType = structSym;

        try
        {
            var hasPack = StructLayoutReader.TryGetPack(structSym, out var packValue);
            if (hasPack)
            {
                context.Emitter.Writer.AppendLine($"#pragma pack(push, {packValue})");
            }

            var inlineArrayAttr = structSym.GetAttribute(KnownAttributes.InlineArray);
            if (inlineArrayAttr != null)
            {
                StructDefinitionEmitter.WriteInlineArrayDefinition(structSym, inlineArrayAttr, ref context);
            }
            else
            {
                StructDefinitionEmitter.WriteStandardStructDefinition(structSym, ref context);
            }

            if (hasPack)
            {
                context.Emitter.Writer.AppendLine("#pragma pack(pop)");
            }
        }
        finally
        {
            context.Emitter.CurrentStructType = previousStructType;
        }
    }

    /// <summary>
    /// Whether a device code export records <paramref name="type"/> on its own, although no device code of the assembly
    /// uses it: a struct of the source that other assemblies can name and whose device definition has parts that a
    /// consumer cannot build from metadata (a constructor with a body, a computed property, a <c>Dispose</c>).
    /// </summary>
    /// <remarks>A struct without such parts is defined from metadata by the consumer, from its fields alone.</remarks>
    public static bool IsExportedOnItsOwn(INamedTypeSymbol type, Compilation compilation)
    {
        if (!GlobalCppNames.IsEmittedUserType(type) || type.IsGenericType || type.IsRefLikeType ||
            !IsVisibleToOtherAssemblies(type) ||
            !type.TryGetDeclaringSyntax<TypeDeclarationSyntax>(out var declaration) ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree))
        {
            return false;
        }

        return type.InstanceConstructors
                   .AsStackEnumerable()
                   .Any(compilation, static (constructor, c) =>
                       StructConstructorEmitter.IsEmittedStructConstructor(constructor, c)) ||
               type.GetMembers()
                   .AsStackEnumerable()
                   .Any(compilation, static (member, c) =>
                       (member is IPropertySymbol property && StructAbiMembers.IsComputedProperty(property)) ||
                       (member is IMethodSymbol conversion &&
                        StructConversionEmitter.IsEmittedStructConversion(conversion, c))) ||
               UsingTranspiler.FindDisposeMethod(type, compilation) != null;
    }

    // Public or internal, and so are the types that contain it
    private static bool IsVisibleToOtherAssemblies(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current != null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal
                or Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Writes the device definition of <paramref name="type"/> for <paramref name="dialect"/> outside any method, as a
    /// method transpilation writes it in front of the method, with its <c>Dispose</c> definition and the definitions of
    /// every struct that its text names (fields, constructor parameters, operator results), each apart.
    /// </summary>
    /// <remarks>
    /// [WHY] A consumer imports the struct's record alone; the structs that its text names must come from records as
    /// well (the library's own structs) or from the record's text (the structs that no record holds, such as the
    /// Kassyi.GSharp.Core vector types; see <c>DeviceExportEmitter.CreateStructRecords</c>).
    /// </remarks>
    public static StructSourceCollector RenderStandalone(INamedTypeSymbol type, SemanticModel semanticModel,
        ITranspilerDialect dialect, Action<TranspileFailure> reportFailure)
    {
        var compilation = semanticModel.Compilation;
        using var visitor = RoslynToCudaVisitor.Rent(dialect, semanticModel, reportFailure,
            BclIntrinsicMapBuilder.GetOrBuildMap(compilation));
        ref var context = ref visitor.Context;
        var collector = new StructSourceCollector();

        StructOrdering.RegisterUsedStructIfApplicable(type, ref context);
        using var disposeWriter = new SourceWriter();
        if (UsingTranspiler.FindDisposeMethod(type, compilation) != null)
        {
            context.Emitter.Writer = disposeWriter;
            context.Emitter.DisposedStructs.Add(type);
            UsingTranspiler.WriteDisposeDefinitions(ref context, collector.Disposes, isolate: true);
        }

        // Each struct keeps its own failures: a struct that the text names and cannot be emitted (a host type) marks its
        // own record failed, which the consumer reports at the use (GSH0089), not the struct that names it
        using var writer = new SourceWriter();
        context.Emitter.Writer = writer;
        WriteDiscoveredDefinitions(ref context, collector);
        return collector;
    }

    /// <summary>
    /// Writes the definition of <paramref name="type"/> into a writer of its own, with a set of includes of its own,
    /// and returns it with whether it met a construct that could not be emitted; the includes are also added to the
    /// transpilation's.
    /// </summary>
    /// <param name="type">The struct.</param>
    /// <param name="context">The transpilation.</param>
    /// <param name="isolate">
    /// Whether the failures stay with the part instead of also reaching the transpilation's failures, and an exception
    /// of the writer marks the part failed instead of stopping the transpilation: a definition that the method's own
    /// text does not contain (<see cref="WriteDiscoveredDefinitions"/>), or a struct exported on its own.
    /// </param>
    public static StructSourcePart WriteDefinitionPart(INamedTypeSymbol type, ref TranspileContext context, bool isolate)
    {
        var writer = context.Emitter.Writer;
        var includes = context.Emitter.RequiredIncludes;
        var reporter = context.Reporter;
        var own = new HashSet<string>(StringComparer.Ordinal);
        var failure = new PartFailure();
        var internalError = "";
        using var structWriter = new SourceWriter();
        context.Emitter.Writer = structWriter;
        context.Emitter.RequiredIncludes = own;
        context.Reporter = reporter.Intercept(failure.Mark, forward: !isolate);
        try
        {
            WriteStructDefinition(type, ref context);
        }
        catch (Exception exception) when (isolate && exception is not OperationCanceledException)
        {
            // [WHY] Not swallowed: the part is failed and its record keeps the message (InternalError), which a consumer
            // that uses the struct states in GSH0089; a struct that no device code of the library uses does not stop the
            // library's build
            failure.Mark();
            internalError = PartFailure.Describe(exception);
        }
        finally
        {
            context.Emitter.Writer = writer;
            context.Emitter.RequiredIncludes = includes;
            context.Reporter = reporter;
            includes.UnionWith(own);
        }

        return new StructSourcePart(type, structWriter.ToString(), ToIncludes(own), failure.Failed, internalError);
    }

    /// <summary>
    /// Writes into <see cref="StructSourceCollector.Discovered"/> the definitions of the structs that
    /// <paramref name="context"/> registered and <paramref name="collector"/> has no definition of, until the
    /// definitions it writes name no further struct. Each keeps its own failures (<see cref="WriteDefinitionPart"/>).
    /// </summary>
    public static void WriteDiscoveredDefinitions(ref TranspileContext context, StructSourceCollector collector)
    {
        while (true)
        {
            var pending = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
            StructOrdering.GetTopologicallySortedStructs(context.Query.UsedStructs)
                .AsStackEnumerable()
                .Where(collector, static (type, c) => !c.HasDefinition(type))
                .AddTo(pending);
            if (pending.Count == 0)
            {
                return;
            }

            foreach (var type in pending)
            {
                collector.Discovered.Add(WriteDefinitionPart(type, ref context, isolate: true));
            }
        }
    }

    /// <summary>The includes of a set, in ordinal order.</summary>
    public static EquatableArray<string> ToIncludes(HashSet<string> includes)
    {
        var builder = ImmutableArray.CreateBuilder<string>(includes.Count);
        includes
            .AsStackEnumerable()
            .AddTo(builder);
        builder.Sort(StringComparer.Ordinal);
        return builder.MoveToImmutable().AsEquatableArray();
    }
}

using Kassyi.GSharp.Compiler.Transpiler.Rules.DeclarationRules;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Transpiles a <c>using</c> statement and a <c>using</c> declaration of device code into C++ that disposes the resource
/// on every exit of its scope, and emits the <c>Dispose</c> bodies that those scopes call.
/// </summary>
/// <remarks>
/// <para>
/// [WHY] Roslyn lowers a <c>using</c> to <c>try { ... } finally { resource.Dispose(); }</c> (LocalRewriter_UsingStatement).
/// C++ has no <c>finally</c>; its equivalent is a destructor, which runs on every exit of the enclosing block (the end,
/// <c>return</c>, <c>break</c>, <c>continue</c>, <c>goto</c>, also the <c>goto case</c> and <c>return</c> of a lowered
/// state machine) and in reverse order of declaration, as C# disposes nested usings. So each resource gets a guard
/// (<c>__gsharp_using_scope&lt;T&gt;</c>, Prelude) instead of a call written before each exit, which would have to
/// track which usings every jump leaves. On CUDA device code has no exceptions, so <c>finally</c> has no other path to
/// cover. On the CPU a failed check ends the step with an exception, and a destructor that disposes while it unwinds
/// could not let a failure of <c>Dispose</c> leave; the CPU dialect writes the <c>finally</c> out instead
/// (<see cref="UsingCpuEmitter"/>).
/// </para>
/// <para>
/// A resource is a value of a user struct: device code cannot hold a class (GSH0001), and a struct that implements
/// <see cref="IDisposable"/>, or a <c>ref struct</c> with a <c>Dispose()</c> method (the pattern C# accepts), is
/// disposed as the variable itself, without a copy, as C# does. The struct declares <c>__gsharp_dispose()</c>
/// (<see cref="StructTranspiler"/>); its definition is emitted only by the methods that dispose the struct
/// (<see cref="WriteDisposeDefinitions"/>), so that a <c>Dispose</c> written for the host is never transpiled unless
/// device code calls it.
/// </para>
/// </remarks>
internal static class UsingTranspiler
{
    /// <summary>The GSH0003 text for a <c>using</c> whose resource is not a struct that G# defines on the device.</summary>
    public const string ResourceNotStruct = "using of a resource that is not a user struct";

    /// <summary>The GSH0003 text for a <c>using</c> whose <c>Dispose</c> has no body in the compilation.</summary>
    public const string DisposeWithoutBody = "using of a struct whose Dispose has no body in this compilation";

    /// <summary>The GSH0003 text for <c>await using</c>.</summary>
    public const string AwaitUsing = "await using";

    /// <summary>Whether <paramref name="statement"/> is a <c>using</c> declaration (<c>using var x = ...;</c>).</summary>
    public static bool IsUsingDeclaration(StatementSyntax statement) =>
        statement is LocalDeclarationStatementSyntax { UsingKeyword.RawKind: (int)SyntaxKind.UsingKeyword };

    /// <summary>
    /// Whether <paramref name="block"/> declares a resource with a <c>using</c> declaration of its own, also under a
    /// label.
    /// </summary>
    public static bool DeclaresUsing(BlockSyntax block) =>
        block.Statements
            .AsStackEnumerable()
            .Any(static statement => IsUsingDeclaration(Unlabeled(statement)));

    // The statement under the labels of a labeled statement
    private static StatementSyntax Unlabeled(StatementSyntax statement)
    {
        while (statement is LabeledStatementSyntax labeled)
        {
            statement = labeled.Statement;
        }

        return statement;
    }

    /// <summary>
    /// The method that a <c>using</c> of <paramref name="type"/> calls: the implementation of
    /// <see cref="IDisposable.Dispose"/>, or for a <c>ref struct</c> a public parameterless <c>void Dispose()</c>.
    /// </summary>
    public static IMethodSymbol? FindDisposeMethod(INamedTypeSymbol type, Compilation compilation)
    {
        var interfaceMethod = compilation.GetSpecialType(SpecialType.System_IDisposable)
            .GetMembers(nameof(IDisposable.Dispose))
            .AsStackEnumerable()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static method => method.Parameters.Length == 0);
        if (interfaceMethod != null && type.FindImplementationForInterfaceMember(interfaceMethod) is IMethodSymbol implementation)
        {
            return implementation;
        }

        if (!type.IsRefLikeType)
        {
            return null;
        }

        return type.GetMembers(nameof(IDisposable.Dispose))
            .AsStackEnumerable()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static method => method is
            {
                IsStatic: false,
                ReturnsVoid: true,
                Parameters.Length: 0,
                DeclaredAccessibility: Accessibility.Public
            });
    }

    // {
    // <declaration or T __gsharp_using_resource_<n> = <expression>;>
    // __gsharp_using_scope<T> __gsharp_using_guard_<n>{<resource>};
    // <body>
    // }
    public static void TranspileUsingStatement(UsingStatementSyntax node, ref TranspileContext context)
    {
        if (node.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, AwaitUsing);
            context.Visitor.AppendUnsupportedPlaceholder(node);
            return;
        }

        if (!context.Emitter.Dialect.IsCuda)
        {
            UsingCpuEmitter.TranspileUsingStatement(node, ref context);
            return;
        }

        var guards = context.Emitter.ActiveGuards.Count;
        using (context.Emitter.Writer.Scope())
        {
            if (node.Declaration != null)
            {
                WriteResourcesWithGuards(node.Declaration, ref context);
            }
            else if (node.Expression != null)
            {
                WriteExpressionResource(node.Expression, ref context);
            }

            if (node.Statement is BlockSyntax block)
            {
                context.Visitor.VisitBlock(block);
            }
            else
            {
                context.Visitor.Visit(node.Statement);
                context.Emitter.Writer.AppendLine();
            }
        }

        context.Emitter.EndGuardScope(guards);
    }

    /// <summary>
    /// Writes a <c>using</c> declaration (<c>using var x = ...;</c>) with a guard after each variable; the guards
    /// dispose the variables at the end of the enclosing block, as C# does. The CPU dialect writes its <c>using</c>
    /// declarations itself (<see cref="UsingCpuEmitter"/>).
    /// </summary>
    public static void TranspileUsingDeclaration(LocalDeclarationStatementSyntax node, ref TranspileContext context)
    {
        if (node.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, AwaitUsing);
            WriteDeclaration(node, node.Declaration, ref context);
            return;
        }

        // [WHY] On the CPU a guard would dispose while a failure unwinds, where a failing Dispose ends the process
        if (!context.Emitter.Dialect.IsCuda)
        {
            throw new InvalidOperationException(
                "A using declaration of the CPU dialect is written with the statements after it (UsingCpuEmitter).");
        }

        WriteResourcesWithGuards(node.Declaration, ref context);
    }

    // <type> <name> = <init>; __gsharp_using_scope<T> __gsharp_using_guard_<n>{<name>}; for each variable
    // [WHY] C# treats several declarators as nested usings: each variable is protected before the next initializer
    // runs, so a failure there disposes the variables before it.
    private static void WriteResourcesWithGuards(VariableDeclarationSyntax declaration, ref TranspileContext context)
    {
        var type = context.Query.SemanticModel.GetTypeSymbol(declaration.Type);
        if (!TryUseDispose(type, declaration.Type, ref context, out var resourceType))
        {
            WriteDeclaration(declaration.Parent ?? declaration, declaration, ref context);
            return;
        }

        foreach (var variable in declaration.Variables)
        {
            StatementPrelude.Emit(variable, ref context);
            VariableDeclarationTranspilerRule.WriteUsingResource(declaration, variable, ref context);
            context.Emitter.Writer.AppendLine(";");
            WriteGuardHead(resourceType, ref context);
            using (context.Emitter.Writer.Braces())
            {
                CppNameResolver.AppendDeclaredName(variable, variable.Identifier, ref context);
            }

            context.Emitter.Writer.AppendLine(";");
        }
    }

    // <declaration>; without guards
    private static void WriteDeclaration(SyntaxNode owner, VariableDeclarationSyntax declaration,
        ref TranspileContext context)
    {
        StatementPrelude.Emit(owner, ref context);
        context.Visitor.Visit(declaration);
        context.Emitter.Writer.AppendLine(";");
    }

    // T __gsharp_using_resource_<n> = <expression>;
    // __gsharp_using_scope<T> __gsharp_using_guard_<n>{__gsharp_using_resource_<n>};
    // [WHY] C# disposes a copy of the expression's value, which a later assignment to its source does not change.
    private static void WriteExpressionResource(ExpressionSyntax expression, ref TranspileContext context)
    {
        var type = context.Query.SemanticModel.GetTypeSymbol(expression);
        if (!TryUseDispose(type, expression, ref context, out var resourceType))
        {
            context.Visitor.Visit(expression);
            context.Emitter.Writer.AppendLine(";");
            return;
        }

        var number = context.Emitter.UsingCounter;
        context.Emitter.Dialect.AppendType(ref context, resourceType);
        context.Emitter.Writer.Append($" {GSharpInternalNames.Using.ResourcePrefix}{number} = ");
        context.Visitor.Visit(expression);
        context.Emitter.Writer.AppendLine(";");

        WriteGuardHead(resourceType, ref context);
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.Append($"{GSharpInternalNames.Using.ResourcePrefix}{number}");
        }

        context.Emitter.Writer.AppendLine(";");
    }

    // __gsharp_using_scope<T> __gsharp_using_guard_<n>
    private static void WriteGuardHead(INamedTypeSymbol resourceType, ref TranspileContext context)
    {
        context.Emitter.Writer.Append($"{GSharpInternalNames.Using.ScopeGuard}<");
        context.Emitter.Dialect.AppendType(ref context, resourceType);
        var name = GSharpInternalNames.Using.GuardPrefix + context.Emitter.UsingCounter++;
        context.Emitter.Writer.Append($"> {name}");
        context.Emitter.ActiveGuards.Add(name);
    }

    // Records the struct whose Dispose the method calls, or reports why the using cannot dispose its resource
    internal static bool TryUseDispose(ITypeSymbol? type, SyntaxNode node, ref TranspileContext context,
        [NotNullWhen(true)] out INamedTypeSymbol? resourceType)
    {
        resourceType = null;
        var compilation = context.Query.SemanticModel.Compilation;
        type = StructTypeSubstitution.SubstituteTypeParameters(type, context.Emitter.CurrentStructType, compilation);

        // An unresolved type is the C# compiler's error
        if (type is null or IErrorTypeSymbol)
        {
            return false;
        }

        if (type is not INamedTypeSymbol named || !GlobalCppNames.IsEncodedUserType(named, context.Emitter.Dialect) ||
            FindDisposeMethod(named, compilation) is not { } dispose)
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, ResourceNotStruct);
            return false;
        }

        // A struct of another assembly brings its Dispose definition with its record (DeviceExportTable)
        if (!TryGetDisposeBody(dispose, compilation, out _, out _, out _) &&
            !HasImportedDispose(named, compilation, context.Emitter.Dialect) &&
            !HasUnusableRecord(named, compilation, context.Emitter.Dialect))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, DisposeWithoutBody);
            return false;
        }

        StructOrdering.RegisterUsedStructIfApplicable(named, ref context);
        var isRecorded = context.Emitter.DisposedStructs
            .AsStackEnumerable()
            .Any(named, static (recorded, type) => recorded.IsEqualTo(type));
        if (!isRecorded)
        {
            context.Emitter.DisposedStructs.Add(named);
        }

        resourceType = named;
        return true;
    }

    // A struct of another assembly whose record holds its Dispose definition for the dialect
    private static bool HasImportedDispose(INamedTypeSymbol type, Compilation compilation, ITranspilerDialect dialect) =>
        DeviceExportTable.IsOutside(type, compilation) && DeviceExportTable.TryFind(type, out var imported) &&
        imported.Record.GetDialect(dialect.IsCuda).Dispose.Length > 0;

    // A struct of another assembly whose record failed in the dialect or was written by another G#: the analyzer reports
    // the using (GSH0089, ImportedDeviceCodeRule), not a Dispose without a body
    private static bool HasUnusableRecord(INamedTypeSymbol type, Compilation compilation, ITranspilerDialect dialect) =>
        DeviceExportTable.IsOutside(type, compilation) && DeviceExportTable.TryFind(type, out var imported) &&
        (!imported.IsCompatible || imported.FailedFor(dialect.IsCuda));

    // The body of `void Dispose() { ... }` or `=> expr`, declared in a tree of the compilation
    private static bool TryGetDisposeBody(IMethodSymbol dispose, Compilation compilation,
        [NotNullWhen(true)] out MethodDeclarationSyntax? declaration, out BlockSyntax? block, out ExpressionSyntax? expression)
    {
        block = null;
        expression = null;
        if (!dispose.TryGetDeclaringSyntax(out declaration) || !compilation.ContainsSyntaxTree(declaration.SyntaxTree))
        {
            return false;
        }

        block = declaration.Body;
        expression = declaration.ExpressionBody?.Expression;
        return block != null || expression != null;
    }

    /// <summary>
    /// Writes the definition of <c>__gsharp_dispose()</c> for each struct that a <c>using</c> of the method disposes
    /// (<see cref="Emitter.DisposedStructs"/>), each under an include guard of its own.
    /// </summary>
    /// <remarks>
    /// Call it after the method body and before the struct definitions are written: a <c>Dispose</c> body can use more
    /// structs, and dispose others, which this adds to the lists it reads.
    /// </remarks>
    /// <param name="context">The transpilation of the method.</param>
    /// <param name="perStruct">
    /// When given, receives each definition apart as well, with the includes it needs and whether it failed (a device
    /// code export records it with its struct).
    /// </param>
    /// <param name="isolate">
    /// With <paramref name="perStruct"/>: whether a failure or an exception stays with the definition instead of also
    /// reaching the transpilation (<c>StructTranspiler.WriteDefinitionPart</c>).
    /// </param>
    public static void WriteDisposeDefinitions(ref TranspileContext context,
        ImmutableArray<StructSourcePart>.Builder? perStruct = null, bool isolate = false)
    {
        var compilation = context.Query.SemanticModel.Compilation;
        for (var i = 0; i < context.Emitter.DisposedStructs.Count; i++)
        {
            var type = context.Emitter.DisposedStructs[i];
            if (FindDisposeMethod(type, compilation) is not { } dispose ||
                !TryGetDisposeBody(dispose, compilation, out var declaration, out var block, out var expression))
            {
                continue;
            }

            if (perStruct == null)
            {
                WriteDisposeDefinition(type, declaration, block, expression, ref context);
                continue;
            }

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
                WriteDisposeDefinition(type, declaration, block, expression, ref context);
            }
            catch (Exception exception) when (isolate && exception is not OperationCanceledException)
            {
                // [WHY] Not swallowed: the struct's record is failed and keeps the message (InternalError)
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

            var definition = structWriter.ToString();
            writer.Append(definition);
            perStruct.Add(new StructSourcePart(type, definition, StructTranspiler.ToIncludes(own), failure.Failed,
                internalError));
        }
    }

    // #ifndef __GSHARP_DISPOSE_<type>__
    // #define __GSHARP_DISPOSE_<type>__
    // __device__ inline void <type>::__gsharp_dispose() { <body> }
    // #endif
    private static void WriteDisposeDefinition(INamedTypeSymbol type, MethodDeclarationSyntax declaration,
        BlockSyntax? block, ExpressionSyntax? expression, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        writer.Append("#ifndef __GSHARP_DISPOSE_").AppendFullyQualifiedCppName(type).AppendLine("__");
        writer.Append("#define __GSHARP_DISPOSE_").AppendFullyQualifiedCppName(type).AppendLine("__");
        writer.Append("__device__ inline void ").AppendFullyQualifiedCppName(type);
        writer.Append($"::{GSharpInternalNames.Using.Dispose}() ");

        // [WHY] The struct may be declared in another file than the method, so its body is bound with a model of its own tree.
        var saved = SemanticModelSwitch.Enter(ref context, declaration.SyntaxTree);
        var previousStructType = context.Emitter.CurrentStructType;
        context.Emitter.CurrentStructType = type;

        // A return in the body leaves Dispose, not the kernel
        context.Emitter.CallableDepth++;
        try
        {
            if (block != null)
            {
                context.Visitor.VisitStatementOrBlock(block);
            }
            else if (expression != null)
            {
                using (context.Emitter.Writer.Scope())
                {
                    context.Visitor.Visit(expression);
                    context.Emitter.Writer.AppendLine(";");
                }
            }
        }
        finally
        {
            context.Emitter.CallableDepth--;
            context.Emitter.CurrentStructType = previousStructType;
            saved.Restore(ref context);
        }

        context.Emitter.Writer.AppendLine("#endif").AppendLine();
    }
}

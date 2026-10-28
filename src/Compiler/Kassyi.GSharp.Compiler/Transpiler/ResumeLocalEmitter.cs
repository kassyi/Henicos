namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Writes the resume locals (<see cref="ResumeLocals"/>) of a native state machine: takes them out of the frame and
/// declares their storage before the state switch on every resume.
/// </summary>
internal static class ResumeLocalEmitter
{
    /// <summary>
    /// Takes the hoisted locals that are declared again on every resume out of the frame, turns their context accesses in
    /// the body back into locals, and leaves their initialization where they are declared. <c>SharedMemory&lt;T&gt;</c>
    /// stays in the frame, where the launcher sets it up. The C++ array of a stackalloc span is the local itself; a
    /// stackalloc pointer points into an array of its own (<see cref="ResumeLocals.StackStorageName"/>).
    /// </summary>
    public static (LoweredStateMachine Lowered, ImmutableArray<ResumeLocal> ResumeLocals) Separate(
        LoweredStateMachine lowered, in MethodEmissionPlan plan, ITranspilerDialect dialect)
    {
        var semanticModel = plan.SemanticModel;
        var kernelName = plan.Declaration.Identifier.ValueText;
        var frameFields = ImmutableArray.CreateBuilder<VariableDeclaratorSyntax>();
        var resumeLocals = ImmutableArray.CreateBuilder<ResumeLocal>();
        var byKey = new Dictionary<DeclarationKey, NativeResumeLocal>();
        foreach (var field in lowered.ContextFields)
        {
            var type = HoistedFieldTypeResolver.Resolve(field, semanticModel, kernelName);
            var key = LoweringIdentity.ForDeclaration(field, semanticModel);
            if (key.Symbol == null || !ResumeLocals.IsRedeclared(field, type, sharedMemoryInFrame: true))
            {
                frameFields.Add(field);
                continue;
            }

            var local = ResumeLocals.Describe(field, type, semanticModel);
            var storage = local is { Kind: ResumeLocalKind.StackAlloc, Type: IPointerTypeSymbol }
                ? SyntaxFactory.IdentifierName(
                    ResumeLocals.StackStorageName(CppNameResolver.Resolve(local.Declarator, semanticModel, dialect).ToString())
                )
                : null;
            resumeLocals.Add(local);
            byKey[key] = new NativeResumeLocal(local, storage);
        }

        if (resumeLocals.Count == 0)
        {
            return (lowered, ImmutableArray<ResumeLocal>.Empty);
        }

        var redeclared = new NativeResumeLocalSet(byKey, plan.Symbol.AreLocalsZeroed());
        var body = new ContextLocalRewriter(redeclared, new Dictionary<DeclarationKey, string>(), semanticModel, kernelName)
                       .Visit(lowered.LoweredBody) as BlockSyntax
                   ?? throw new InvalidOperationException(
                       $"Turning the resume locals of '{kernelName}' back into locals removed the state machine body.");
        return (lowered with { LoweredBody = body, ContextFields = frameFields.ToImmutable() }, resumeLocals.ToImmutable());
    }

    /// <summary>
    /// Writes the storage of each resume local, before the state switch; the values are written where it is declared.
    /// A stackalloc with a size that is not a constant is reported, as the declaration rule reports it without a barrier.
    /// </summary>
    /// <remarks>
    /// <code>
    /// &lt;type&gt; &lt;name&gt;;                                               (assigned there, through the declaration rules)
    /// &lt;element&gt; &lt;name&gt;[&lt;length&gt;];                                  (a stackalloc span: zeroed or filled there)
    /// &lt;element&gt; __gsharp_stack_&lt;name&gt;[&lt;length&gt;]; &lt;element&gt;* &lt;name&gt;; (a stackalloc pointer: bound, zeroed or filled there)
    /// &lt;element&gt;* &lt;name&gt;;                                           (a stackalloc pointer of length 0: set to nullptr there)
    /// </code>
    /// </remarks>
    public static void WriteDeclarations(ImmutableArray<ResumeLocal> resumeLocals, ref TranspileContext context)
    {
        foreach (var local in resumeLocals)
        {
            switch (local)
            {
                case { Kind: ResumeLocalKind.Unsupported, Unsupported: { } node }:
                    context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, GpuCodeConstraints.DynamicArraySize);
                    break;
                case { Kind: ResumeLocalKind.StackAlloc, ElementType: { } elementType }:
                    WriteStackAllocStorage(local, elementType, ref context);
                    break;
                default:
                    if (local.Declarator.Parent is VariableDeclarationSyntax declaration)
                    {
                        context.Visitor.Visit(SyntaxFactory.LocalDeclarationStatement(
                            declaration.WithVariables(SyntaxFactory.SingletonSeparatedList(local.Declarator.WithInitializer(null)))
                        ));
                        context.Emitter.Writer.AppendLine();
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// <c>&lt;element&gt; __gsharp_stack_&lt;name&gt;[&lt;length&gt;]&lt;elements or {}&gt;; &lt;element&gt;* &lt;name&gt; = __gsharp_stack_&lt;name&gt;</c>,
    /// or <c>&lt;element&gt;* &lt;name&gt; = nullptr</c> for length 0 (a zero-length array is ill-formed in C++), without the
    /// final semicolon: the declaration of a stackalloc pointer, also outside a state machine
    /// (<c>VariableDeclarationTranspilerRule</c>).
    /// </summary>
    /// <param name="name">The C++ name of the pointer; the array is named after it (<see cref="ResumeLocals.StackStorageName"/>).</param>
    /// <param name="pointerType">The type of the pointer.</param>
    /// <param name="length">The element count.</param>
    /// <param name="elements">The initializer list of the stackalloc, written as the initializer of the array.</param>
    /// <param name="zeroed">Whether the array is zeroed (<c>{}</c>) when it has no initializer list.</param>
    /// <param name="bind">
    /// Whether the pointer is initialized; a resume local is bound where it is declared instead
    /// (<see cref="ResumeLocals.CreateInitialization"/>).
    /// </param>
    /// <param name="context">The transpile context.</param>
    /// <remarks>[WHY] The array has a name of its own, so that the pointer can be incremented and assigned as in C#.</remarks>
    public static void WriteStackAllocPointer(string name, IPointerTypeSymbol pointerType, int length,
        SyntaxNode? elements, bool zeroed, bool bind, ref TranspileContext context)
    {
        if (length == 0)
        {
            context.Emitter.Dialect.AppendType(ref context, pointerType);
            context.Emitter.Writer.Append($" {name}");
            context.Emitter.Writer.AppendIf(bind, " = nullptr");
            return;
        }

        var storage = ResumeLocals.StackStorageName(name);
        context.Emitter.Dialect.AppendType(ref context, pointerType.PointedAtType);
        context.Emitter.Writer.Append($" {storage}");
        using (context.Emitter.Writer.Brackets())
        {
            context.Emitter.Writer.Append(length);
        }

        if (elements != null)
        {
            context.Emitter.Writer.Append("=");
            context.Visitor.Visit(elements);
        }
        else
        {
            context.Emitter.Writer.AppendIf(zeroed, "{}");
        }

        context.Emitter.Writer.AppendLine(";");
        context.Emitter.Dialect.AppendType(ref context, pointerType);
        context.Emitter.Writer.Append($" {name}");
        if (bind)
        {
            context.Emitter.Writer.Append($" = {storage}");
        }
    }

    // <element> <name>[<length>]; for a span, the pointer storage for a pointer; <type> <name>{}; for an empty span
    // [WHY] The array is not const even for a ReadOnlySpan<T>: its elements are written where the local is declared.
    private static void WriteStackAllocStorage(ResumeLocal local, ITypeSymbol elementType, ref TranspileContext context)
    {
        var name = CppNameResolver.Resolve(local.Declarator, context.Query.SemanticModel, context.Emitter.Dialect).ToString();
        if (local.Type is IPointerTypeSymbol pointerType)
        {
            WriteStackAllocPointer(name, pointerType, local.Length, elements: null, zeroed: false, bind: false, ref context);
            context.Emitter.Writer.AppendLine(";");
            return;
        }

        context.Emitter.Dialect.AppendType(ref context, local.Length == 0 ? local.Type : elementType);
        if (local.Length == 0)
        {
            // A zero-length array is ill-formed in C++: the span is empty
            context.Emitter.Writer.AppendLine($" {name}{{}};");
            return;
        }

        context.Emitter.Writer.Append($" {name}");
        using (context.Emitter.Writer.Brackets())
        {
            context.Emitter.Writer.Append(local.Length);
        }

        context.Emitter.Writer.AppendLine(";");
    }

    // A resume local of the native state machine and the array that a stackalloc pointer is bound to
    private readonly record struct NativeResumeLocal(ResumeLocal Local, IdentifierNameSyntax? Storage);

    // The resume locals of a native state machine, as ContextLocalRewriter looks them up
    private sealed class NativeResumeLocalSet(Dictionary<DeclarationKey, NativeResumeLocal> locals, bool zeroFill)
        : IResumeLocalSet
    {
        public bool Contains(DeclarationKey key) => locals.ContainsKey(key);

        public StatementSyntax? CreateInitialization(DeclarationKey key, SimpleNameSyntax name, ExpressionSyntax value) =>
            locals[key] switch
            {
                { Local: { Kind: ResumeLocalKind.StackAlloc, Length: 0, Type: IPointerTypeSymbol } } =>
                    ResumeLocals.AssignNull(name),
                // The empty span declared before the switch is all there is
                { Local: { Kind: ResumeLocalKind.StackAlloc, Length: 0 } } => null,
                var (local, storage) => ResumeLocals.CreateInitialization(local, name, value, storage, zeroFill)
            };
    }
}

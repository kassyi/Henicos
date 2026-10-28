namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>How a resume local (<see cref="ResumeLocals"/>) gets its storage and its value.</summary>
internal enum ResumeLocalKind
{
    /// <summary>
    /// Declared with a default value before the state switch; its initializer becomes an assignment where it is declared.
    /// </summary>
    Bound,

    /// <summary>
    /// Initialized with a stackalloc of a constant size: the memory is declared before the state switch, and where the
    /// local is declared the memory is zeroed again (or receives the elements of the initializer list).
    /// </summary>
    StackAlloc,

    /// <summary>
    /// Initialized with a stackalloc whose size is not a constant, or that is not the initializer itself (a cast): reported
    /// as a dynamic array size (GSH0003), as the declaration rule reports it without a barrier.
    /// </summary>
    Unsupported,

    /// <summary>
    /// A managed <c>SharedMemory&lt;T&gt;</c>: kept as it was before resume locals existed (declared with its initializer
    /// before the state switch, nothing where it is declared): the shared memory emitters own the placement of its
    /// region, and a C# class cannot hold it as a field.
    /// </summary>
    Initialized
}

/// <summary>A hoisted local that a state machine declares again on every resume, and how.</summary>
/// <param name="Declarator">The declarator of the local in the lowered body (with its original-span annotation).</param>
/// <param name="Type">Its type.</param>
/// <param name="Kind">How it gets its storage and value.</param>
/// <param name="ElementType">The element type of a <see cref="ResumeLocalKind.StackAlloc"/> local.</param>
/// <param name="Length">The element count of a <see cref="ResumeLocalKind.StackAlloc"/> local.</param>
/// <param name="Unsupported">The node to report for a <see cref="ResumeLocalKind.Unsupported"/> local.</param>
internal readonly record struct ResumeLocal(
    VariableDeclaratorSyntax Declarator,
    ITypeSymbol Type,
    ResumeLocalKind Kind,
    ITypeSymbol? ElementType,
    int Length,
    SyntaxNode? Unsupported = null);

/// <summary>
/// Decides which hoisted locals of a state machine stay out of its frame and are declared again before the state switch
/// on every resume, and writes their initialization where they are declared.
/// </summary>
/// <remarks>
/// <para>
/// [WHY] A ref struct (a span) cannot be a field of the managed frame class, and a stackalloc pointer points into the
/// stack of one call of the state function: copied into the frame, it is null on the native CPU dialect and dangles in
/// managed code. The analyzer rejects such a local when it is live across a split point (GSH0039, GSH0041,
/// <c>CrossBarrierLiveness</c>), so every state that uses it either declares it or assigns it first; declaring its
/// storage again on each resume keeps its memory. The lowering (<c>ContextHoister</c>) still hoists it by its text, and
/// both dialects turn its accesses back into a local (<c>ContextLocalRewriter</c>).
/// </para>
/// <para>
/// <c>SharedMemory&lt;T&gt;</c> is excluded here, in one place: the native frame keeps it where the launcher and the
/// declaration place it (<see cref="IsRedeclared"/> with <c>sharedMemoryInFrame</c>), and the managed path keeps declaring
/// it with its initializer as before (<see cref="ResumeLocalKind.Initialized"/>), so the order in which regions are
/// placed stays the one of the shared memory emitters.
/// </para>
/// <para>
/// Only the storage moves before the switch. The initializer stays where the local is declared, as an assignment, so it
/// runs in the order C# runs it and reads the locals assigned before it. A stackalloc gives zeroed memory each time it
/// runs (unless <c>[SkipLocalsInit]</c> applies), so the memory is zeroed again there, also in the next iteration of a
/// loop within the same resume.
/// </para>
/// </remarks>
internal static class ResumeLocals
{
    /// <summary>
    /// Whether the hoisted local <paramref name="field"/> of type <paramref name="type"/> is declared again on every resume.
    /// </summary>
    /// <param name="field">The declarator of the hoisted local.</param>
    /// <param name="type">Its type (<see cref="HoistedFieldTypeResolver.Resolve"/>).</param>
    /// <param name="sharedMemoryInFrame">
    /// Whether a <c>SharedMemory&lt;T&gt;</c> local stays in the frame: on the native CPU dialect the launcher sets it up
    /// there (it is a ref struct, but its memory belongs to the block, not to the call).
    /// </param>
    public static bool IsRedeclared(VariableDeclaratorSyntax field, ITypeSymbol type, bool sharedMemoryInFrame) =>
        (type.IsRefLikeType && !(sharedMemoryInFrame && type.IsSharedMemoryType())) || IsStackAllocPointer(field, type);

    /// <summary>How the resume local <paramref name="field"/> of type <paramref name="type"/> is declared and initialized.</summary>
    public static ResumeLocal Describe(VariableDeclaratorSyntax field, ITypeSymbol type, SemanticModel semanticModel)
    {
        if (type.IsSharedMemoryType())
        {
            return new ResumeLocal(field, type, ResumeLocalKind.Initialized, null, 0);
        }

        var elementType = type switch
        {
            IPointerTypeSymbol pointer => pointer.PointedAtType,
            INamedTypeSymbol { TypeArguments.Length: 1 } span when IsSpan(span) => span.TypeArguments[0],
            _ => null
        };
        if (field.Initializer?.Value is not { } value || !ContainsStackAlloc(value))
        {
            return new ResumeLocal(field, type, ResumeLocalKind.Bound, null, 0);
        }

        var isConstant = TryGetStackAllocLength(value, semanticModel, out var length, out var unsupported);
        return elementType != null && isConstant
            ? new ResumeLocal(field, type, ResumeLocalKind.StackAlloc, elementType, length)
            : new ResumeLocal(field, type, ResumeLocalKind.Unsupported, null, 0, unsupported ?? value);
    }

    /// <summary>
    /// The element count of <paramref name="value"/> when it is a stackalloc of a constant size (<c>stackalloc T[n]</c>
    /// with a constant <c>n</c>, or with elements); otherwise the node to report as a dynamic array size: the size
    /// expression, or the stackalloc inside another expression (<c>(int*)stackalloc byte[16]</c>).
    /// </summary>
    public static bool TryGetStackAllocLength(ExpressionSyntax value, SemanticModel semanticModel, out int length,
        out SyntaxNode? unsupported)
    {
        length = 0;
        unsupported = null;
        switch (value)
        {
            case StackAllocArrayCreationExpressionSyntax { Type: ArrayTypeSyntax { RankSpecifiers: [{ Sizes: [var size] }] } } creation
                when size is not OmittedArraySizeExpressionSyntax:
                if (semanticModel.GetSafeConstantValue(size) is { HasValue: true, Value: int constant })
                {
                    length = constant;
                    return true;
                }

                if (creation.Initializer is { } explicitElements)
                {
                    length = explicitElements.Expressions.Count;
                    return true;
                }

                unsupported = size;
                return false;
            case StackAllocArrayCreationExpressionSyntax { Initializer: { } elements }:
                length = elements.Expressions.Count;
                return true;
            case ImplicitStackAllocArrayCreationExpressionSyntax implicitCreation:
                length = implicitCreation.Initializer.Expressions.Count;
                return true;
            default:
                unsupported = value.DescendantNodes()
                    .AsStackEnumerable()
                    .FirstOrDefault(static node =>
                        node is StackAllocArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax
                    ) ?? value;
                return false;
        }
    }

    /// <summary>Whether <paramref name="value"/> holds a stackalloc anywhere.</summary>
    public static bool ContainsStackAlloc(ExpressionSyntax value) =>
        value.DescendantNodesAndSelf()
            .AsStackEnumerable()
            .Any(static node => node is StackAllocArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax);

    /// <summary>The name of the C++ array behind the stackalloc pointer named <paramref name="pointer"/>.</summary>
    public static string StackStorageName(string pointer) => GSharpInternalNames.Lowering.StackStoragePrefix + pointer;

    /// <summary>
    /// The statement that takes the place of the declaration of <paramref name="local"/> in the body, or
    /// <see langword="null"/> when nothing remains there.
    /// </summary>
    /// <param name="local">The resume local.</param>
    /// <param name="name">The reference to the local.</param>
    /// <param name="value">Its initializer, with the accesses inside it already rewritten.</param>
    /// <param name="storage">
    /// The stackalloc storage that the local is bound to (the managed storage, or the native C++ array of a pointer), or
    /// <see langword="null"/> for a native span, which is the C++ array itself.
    /// </param>
    /// <param name="zeroFill">Whether a stackalloc gives zeroed memory (<see cref="SymbolExtensions.AreLocalsZeroed"/> of the method).</param>
    public static StatementSyntax? CreateInitialization(ResumeLocal local, SimpleNameSyntax name, ExpressionSyntax value,
        SimpleNameSyntax? storage, bool zeroFill)
    {
        switch (local.Kind)
        {
            // [WHY] An unsupported stackalloc is reported where its storage would be written (GSH0003); writing the
            // stackalloc where it was declared as well would report it a second time
            case ResumeLocalKind.Initialized or ResumeLocalKind.Unsupported:
                return null;
            case ResumeLocalKind.Bound:
                return Assign(name, value);
        }

        var statements = ImmutableArray.CreateBuilder<StatementSyntax>();
        if (storage != null)
        {
            statements.Add(Assign(name, storage));
        }

        var memory = storage ?? name;
        if (GetElements(value) is { } elements)
        {
            for (var i = 0; i < elements.Count; i++)
            {
                statements.Add(Assign(ElementAt(memory, Literal(i)), elements[i]));
            }
        }
        else if (zeroFill)
        {
            statements.Add(CreateZeroFill(memory, local.Length));
        }

        return SyntaxFactory.Block(statements);
    }

    // int* p = stackalloc int[n];
    private static bool IsStackAllocPointer(VariableDeclaratorSyntax field, ITypeSymbol type) =>
        type is IPointerTypeSymbol && field.Initializer?.Value is { } value && ContainsStackAlloc(value);

#pragma warning disable HNT0003 // The namespace is checked in the same expression
    private static bool IsSpan(INamedTypeSymbol type) =>
        type.Name is TypeConstants.SpanType or TypeConstants.ReadOnlySpanType && type.ContainingNamespace.IsSystemNamespace();
#pragma warning restore HNT0003

    private static SeparatedSyntaxList<ExpressionSyntax>? GetElements(ExpressionSyntax value) => value switch
    {
        StackAllocArrayCreationExpressionSyntax { Initializer: { } initializer } => initializer.Expressions,
        ImplicitStackAllocArrayCreationExpressionSyntax implicitCreation => implicitCreation.Initializer.Expressions,
        _ => null
    };

    // for (var __gsharp_resume_fill = 0; __gsharp_resume_fill < <length>; __gsharp_resume_fill++) <memory>[__gsharp_resume_fill] = default;
    private static ForStatementSyntax CreateZeroFill(SimpleNameSyntax memory, int length)
    {
        var index = SyntaxFactory.IdentifierName(GSharpInternalNames.Lowering.ResumeFillIndex);
        var declaration = SyntaxFactory.VariableDeclaration(
            // var (auto): a synthesized declaration has no symbol for the emitter to resolve int from
            SyntaxFactory.IdentifierName("var"),
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.VariableDeclarator(index.Identifier)
                    .WithInitializer(SyntaxFactory.EqualsValueClause(Literal(0)))
            )
        );
        var condition = SyntaxFactory.BinaryExpression(SyntaxKind.LessThanExpression, index, Literal(length));
        var increment = SyntaxFactory.PostfixUnaryExpression(SyntaxKind.PostIncrementExpression, index);
        var clear = Assign(ElementAt(memory, index), SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression));
        return SyntaxFactory.ForStatement(
            declaration,
            SyntaxFactory.SeparatedList<ExpressionSyntax>(),
            condition,
            SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(increment),
            clear
        );
    }

    /// <summary>
    /// <c>&lt;name&gt; = null;</c>: the native binding of a stackalloc pointer of length 0, which has no C++ array (a
    /// zero-length array is ill-formed in C++).
    /// </summary>
    public static StatementSyntax AssignNull(SimpleNameSyntax name) =>
        Assign(name, SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression));

    // <target> = <value>;
    private static ExpressionStatementSyntax Assign(ExpressionSyntax target, ExpressionSyntax value) =>
        SyntaxFactory.ExpressionStatement(
            SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, target, value.WithoutTrivia())
        );

    // <memory>[<index>]
    private static ElementAccessExpressionSyntax ElementAt(ExpressionSyntax memory, ExpressionSyntax index) =>
        SyntaxFactory.ElementAccessExpression(
            memory.WithoutTrivia(),
            SyntaxFactory.BracketedArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(index)))
        );

    private static LiteralExpressionSyntax Literal(int value) =>
        SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(value));
}

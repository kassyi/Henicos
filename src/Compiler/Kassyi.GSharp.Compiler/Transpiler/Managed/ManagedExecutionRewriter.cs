using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Rewrites lowered device function calls and state machine constructs into managed execution equivalents.
/// </summary>
/// <param name="semanticModel">The model of the user's tree that the body comes from.</param>
/// <param name="bodyKind">What the body is emitted as.</param>
/// <param name="accessors">
/// The stubs through which the body calls an operator macro target it cannot name, and reaches the backing field of an
/// auto-property that an operator macro passes by reference.
/// </param>
/// <param name="isUnsafe">
/// Whether the context is an unsafe struct reached through a pointer rather than a class, so its members are read
/// with <c>-&gt;</c>.
/// </param>
internal sealed partial class ManagedExecutionRewriter(
    SemanticModel semanticModel,
    ManagedBodyKind bodyKind,
    ManagedAccessorSet accessors,
    bool isUnsafe = false) : CSharpSyntaxRewriter
{
    private readonly bool _isUnsafe = isUnsafe;

    private int _inLambdaOrLocalFuncCount;

    // The label after the Gpu.SingleThread body being rewritten, when the body has a return, and the lambda depth of
    // that body: a return at that depth jumps to the label (#693)
    private string? _singleThreadEndLabel;
    private int _singleThreadDepth;
    private int _nextSingleThreadId;

    // The number of the next loop variable that a Gpu.ForUnroll becomes; found on the first visit, which is the body
    private int _nextUnrollId = -1;

    // The number of the next hidden variable that a using (<variable>) statement declares
    private int _nextUsingResourceId;

    // The number of the next variable that holds the target of a division read before its value (ManagedDivision)
    private int _nextDivisionCurrentId;

    public override SyntaxNode? Visit(SyntaxNode? node)
    {
        if (_nextUnrollId < 0 && node is not null)
        {
            _nextUnrollId = FindNextUnrollId(node);
        }

        return base.Visit(node);
    }

    // A loop that PreLoweringExpander made of a barrier-bearing ForUnroll already uses these names. Its variable lives
    // in the context, so the two never clash; counting on from it only keeps the variables apart in the generated code
    private static int FindNextUnrollId(SyntaxNode root)
    {
        var next = 0;
        foreach (var token in root.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.IdentifierToken) &&
                token.ValueText.StartsWith(GSharpInternalNames.Lowering.UnrollIndexPrefix, StringComparison.Ordinal) &&
                int.TryParse(
                    token.ValueText[GSharpInternalNames.Lowering.UnrollIndexPrefix.Length..],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var id) &&
                id >= next)
            {
                next = id + 1;
            }
        }

        return next;
    }

    // An int or long division that may divide the minimum by -1 gives the GPU result (ManagedDivision):
    // global::Kassyi.GSharp.Core.ManagedGpuContext.<helper>(<left>, <right>)
    public override SyntaxNode? VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        var visited = base.VisitBinaryExpression(node);
        if (visited is not BinaryExpressionSyntax rewritten ||
            ManagedDivision.Plan(node, semanticModel) is not { } plan)
        {
            return visited;
        }

        return CallContext(plan.Helper, SyntaxFactory.Argument(rewritten.Left), SyntaxFactory.Argument(rewritten.Right))
            .WithTriviaFrom(node);
    }

    // The compound form of the same division:
    // global::Kassyi.GSharp.Core.ManagedGpuContext.<helper>(ref <target>, <value>), which stores and returns the
    // result, or, for a property, <target> = global::Kassyi.GSharp.Core.ManagedGpuContext.<helper>(<target>, <value>)
    public override SyntaxNode? VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        var visited = base.VisitAssignmentExpression(node);
        if (visited is not AssignmentExpressionSyntax rewritten ||
            ManagedDivision.Plan(node, semanticModel) is not { } plan)
        {
            return visited;
        }

        var target = rewritten.Left.WithoutTrivia();
        var value = SyntaxFactory.Argument(rewritten.Right.WithoutTrivia());
        if (plan.Form == ManagedDivisionForm.AssignByReference)
        {
            return CallContext(plan.Helper, ByReference(target), value).WithTriviaFrom(node);
        }

        // The form the plan chose is the one written; a plan whose shape cannot be written is an internal error
        switch (plan)
        {
            case { Form: ManagedDivisionForm.ReadFirst, Reader: { } reader }:
                return ReadFirst(plan.Helper, reader, target, value).WithTriviaFrom(node);
            case
            {
                Form: ManagedDivisionForm.PropertyByReference or ManagedDivisionForm.PropertyOfObject,
                Reader: { } propertyReader,
                ReceiverType: { } receiverType,
                ValueType: { } valueType
            } when rewritten.Left is MemberAccessExpressionSyntax property:
                return ReadFirstProperty(plan, propertyReader, property, receiverType, valueType, value)
                    .WithTriviaFrom(node);
            case { Form: ManagedDivisionForm.Reassign }:
                // <target> = <helper>(<target>, <value>)
                return SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    target,
                    CallContext(plan.Helper, SyntaxFactory.Argument(target), value)
                ).WithTriviaFrom(node);
            default:
                throw new InvalidOperationException(
                    $"The division plan {plan.Form} of '{node}' cannot be written: its plan data is incomplete or " +
                    "the target is not the shape the plan chose.");
        }
    }

    // global::Kassyi.GSharp.Core.ManagedGpuContext.<member>(<first>, <second>)
    private static InvocationExpressionSyntax CallContext(string member, ArgumentSyntax first, ArgumentSyntax second) =>
        SyntaxFactory.InvocationExpression(
            ManagedNames.CreateMemberAccess(member),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList([first, second]))
        );

    // The compound assignment of a property whose receiver is bound once (ManagedDivisionForm.PropertyByReference and
    // PropertyOfObject): the getter and the setter are static lambdas over the receiver, which the reader hands on
    //   <store>(ref <reader>(ref <receiver>, static (ref T r) => r.P, out var c), c, <value>,
    //       static (ref T r, int v) => r.P = v)
    // and for an object <store>(<reader>(<receiver>, static r => r.P, out var c), c, <value>, static (r, v) => r.P = v)
    private ExpressionSyntax ReadFirstProperty(ManagedDivisionPlan plan, string reader,
        MemberAccessExpressionSyntax property, ITypeSymbol receiverType, ITypeSymbol valueType,
        ArgumentSyntax value)
    {
        var current = GSharpInternalNames.IntegerSemantics.DivisionCurrentPrefix +
                      _nextDivisionCurrentId++.ToString(CultureInfo.InvariantCulture);
        var name = property.Name.Identifier.ValueText;
        var receiverTypeSyntax = receiverType.ToCsharpTypeSyntax();
        var valueTypeSyntax = SyntaxFactory.PredefinedType(SyntaxFactory.Token(
            valueType.SpecialType == SpecialType.System_Int64 ? SyntaxKind.LongKeyword : SyntaxKind.IntKeyword));
        var byReference = plan.Form == ManagedDivisionForm.PropertyByReference;
        var getter = CreatePropertyGetter(receiverTypeSyntax, name, byReference);
        var setter = CreatePropertySetter(receiverTypeSyntax, valueTypeSyntax, name, byReference);
        var declaration = SyntaxFactory.DeclarationExpression(
            SyntaxFactory.IdentifierName("var"),
            SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(current))
        );
        var receiverArgument = byReference
            ? ByReference(property.Expression.WithoutTrivia())
            : SyntaxFactory.Argument(property.Expression.WithoutTrivia());
        var read = SyntaxFactory.InvocationExpression(
            ManagedNames.CreateMemberAccess(reader),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList([
                receiverArgument,
                SyntaxFactory.Argument(getter),
                SyntaxFactory.Argument(declaration).WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.OutKeyword))
            ]))
        );
        var arguments = SyntaxFactory.SeparatedList([
            byReference ? ByReference(read) : SyntaxFactory.Argument(read),
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName(current)),
            value,
            SyntaxFactory.Argument(setter)
        ]);
        return SyntaxFactory.InvocationExpression(
            ManagedNames.CreateMemberAccess(plan.Helper),
            SyntaxFactory.ArgumentList(arguments)
        );
    }

    // static (ref <receiver> r) => r.<name>     or, for an object, static (<receiver> r) => r.<name>
    private static ParenthesizedLambdaExpressionSyntax CreatePropertyGetter(TypeSyntax receiverType, string name,
        bool byReference) =>
        SyntaxFactory.ParenthesizedLambdaExpression(
            SyntaxFactory.ParameterList(SyntaxFactory.SingletonSeparatedList(
                CreateReceiverParameter(receiverType, byReference)
            )),
            CreatePropertyAccess(name)
        ).WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.StaticKeyword)));

    // static (ref <receiver> r, <value> v) => r.<name> = v
    // or, for an object, static (<receiver> r, <value> v) => r.<name> = v
    private static ParenthesizedLambdaExpressionSyntax CreatePropertySetter(TypeSyntax receiverType,
        TypeSyntax valueType, string name, bool byReference) =>
        SyntaxFactory.ParenthesizedLambdaExpression(
            SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList([
                CreateReceiverParameter(receiverType, byReference),
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("v")).WithType(valueType)
            ])),
            SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                CreatePropertyAccess(name),
                SyntaxFactory.IdentifierName("v")
            )
        ).WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.StaticKeyword)));

    // [ref] <receiver> r
    private static ParameterSyntax CreateReceiverParameter(TypeSyntax receiverType, bool byReference) =>
        SyntaxFactory.Parameter(SyntaxFactory.Identifier("r"))
            .WithType(receiverType)
            .WithModifiers(byReference
                ? SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.RefKeyword))
                : default);

    // r.<name>
    private static MemberAccessExpressionSyntax CreatePropertyAccess(string name) =>
        SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.IdentifierName("r"),
            SyntaxFactory.IdentifierName(name)
        );

    // ref <expression>
    private static ArgumentSyntax ByReference(ExpressionSyntax expression) =>
        SyntaxFactory.Argument(expression).WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword));

    // [WHY] C# finds the location of the target, reads it, then evaluates the value. The location is found once and
    // read by the reader before the value runs, as ref var t = ref <target>; t = t / <value>; does, in one expression:
    // ManagedGpuContext.<store>(ref ManagedGpuContext.<reader>(ref <target>, out var <current>), <current>, <value>)
    private ExpressionSyntax ReadFirst(string store, string reader, ExpressionSyntax target, ArgumentSyntax value)
    {
        var current = GSharpInternalNames.IntegerSemantics.DivisionCurrentPrefix +
                      _nextDivisionCurrentId++.ToString(CultureInfo.InvariantCulture);
        var declaration = SyntaxFactory.DeclarationExpression(
            SyntaxFactory.IdentifierName("var"),
            SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(current))
        );
        var outCurrent = SyntaxFactory.Argument(declaration)
            .WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.OutKeyword));
        var read = SyntaxFactory.InvocationExpression(
            ManagedNames.CreateMemberAccess(reader),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList([ByReference(target), outCurrent]))
        );
        var arguments = SyntaxFactory.SeparatedList([
            ByReference(read),
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName(current)),
            value
        ]);
        return SyntaxFactory.InvocationExpression(
            ManagedNames.CreateMemberAccess(store),
            SyntaxFactory.ArgumentList(arguments)
        );
    }

    public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node) =>
        VisitCreation(node, base.VisitObjectCreationExpression(node));

    public override SyntaxNode?
        VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node) =>
        VisitCreation(node, base.VisitImplicitObjectCreationExpression(node));

    private SyntaxNode? VisitCreation(BaseObjectCreationExpressionSyntax node, SyntaxNode? visited)
    {
        if (TryRewriteSharedMemory(node, visited) is { } shared)
        {
            return shared;
        }

        if (TryWrapGenericStructConstructor(node, visited) is { } wrapped)
        {
            return wrapped;
        }

        return visited;
    }

    private SyntaxNode? TryRewriteSharedMemory(BaseObjectCreationExpressionSyntax node, SyntaxNode? visited)
    {
        if (visited is BaseObjectCreationExpressionSyntax rewritten &&
            semanticModel.GetTypeSymbol(node).IsSharedMemoryType())
        {
            return SharedMemorySiteKeyAnnotator.AddSharedMemorySiteKey(node, rewritten, semanticModel);
        }

        return null;
    }

    private SyntaxNode? TryWrapGenericStructConstructor(BaseObjectCreationExpressionSyntax node, SyntaxNode? visited)
    {
        // A generic struct constructor is monomorphized per closed struct type on CUDA and on the native CPU path (#543),
        // so the managed path wraps the creation in a type-key scope that salts any SharedMemory<T> declared inside it.
        if (visited is ExpressionSyntax rewrittenExpr &&
            semanticModel.GetTypeSymbol(node) is INamedTypeSymbol { TypeKind: TypeKind.Struct, IsGenericType: true } structType &&
            semanticModel.GetSymbol(node) is IMethodSymbol ctor &&
            StructConstructorEmitter.IsEmittedStructConstructor(ctor, semanticModel.Compilation) &&
            DeclaresSharedMemoryInStruct(structType, semanticModel.Compilation))
        {
            using var typeNameWriter = new SourceWriter();
            typeNameWriter.AppendCsharpTypeName(structType);
            var typeSyntax = SyntaxFactory.ParseTypeName(typeNameWriter.ToString());
            var typeArgs = SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList(typeSyntax));
            var beginCall = SyntaxFactory.InvocationExpression(
                ManagedNames.CreateMemberAccess(
                    SyntaxFactory.GenericName(SyntaxFactory.Identifier("BeginTypeKeyScope"), typeArgs)
                )
            );
            // <context>.EndTypeKeyScope<<struct>>(<context>.BeginTypeKeyScope<<struct>>(), <creation>)
            // The type argument is spelled: a target-typed new(...) has no type of its own as an argument, so C# could
            // not infer it (CS0411, #1255)
            return SyntaxFactory.InvocationExpression(
                ManagedNames.CreateMemberAccess(
                    SyntaxFactory.GenericName(SyntaxFactory.Identifier("EndTypeKeyScope"), typeArgs)
                ),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SeparatedList<ArgumentSyntax>(
                    [
                        SyntaxFactory.Argument(beginCall),
                        SyntaxFactory.Argument(rewrittenExpr.WithoutTrivia())
                    ])
                )
            ).WithTriviaFrom(rewrittenExpr);
        }

        return null;
    }

    private static bool DeclaresSharedMemoryInStruct(INamedTypeSymbol structType, Compilation compilation)
    {
        foreach (var syntaxRef in structType.OriginalDefinition.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is TypeDeclarationSyntax typeDecl)
            {
                var model = compilation.GetCachedSemanticModel(syntaxRef.SyntaxTree);
                foreach (var node in typeDecl.DescendantNodes())
                {
                    if (node is BaseObjectCreationExpressionSyntax creation &&
                        model.GetTypeSymbol(creation).IsSharedMemoryType())
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var visited = base.VisitMemberAccessExpression(node);
        if (TryRouteMemberAccess(node, visited) is { } routed)
        {
            return routed;
        }

        if (!ContextLocalRewriter.IsContextPointerAccess(visited) ||
            visited is not MemberAccessExpressionSyntax memberAccess)
        {
            return visited;
        }

        if (_isUnsafe)
        {
            return visited;
        }

        return SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            memberAccess.Expression,
            memberAccess.Name).WithTriviaFrom(memberAccess);
    }

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (node.Parent is MemberAccessExpressionSyntax parent && parent.Name == node)
        {
            return base.VisitIdentifierName(node);
        }

        return node.Identifier.Text switch
        {
            // The managed shuffle helpers keep the warp exchange themselves; any context will do
            GSharpInternalNames.SuspendingCall.Root =>
                SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0)).WithTriviaFrom(node),
            // A grid sync request goes to the launcher whether a kernel or a device function makes it (#665):
            // global::Kassyi.GSharp.Core.ManagedGpuContext.GridSyncRequested
            GSharpInternalNames.StateMachine.YieldedForGridSync =>
                ManagedNames.CreateMemberAccess(ManagedNames.GridSyncRequestedName).WithTriviaFrom(node),
            // A block barrier the same way: global::Kassyi.GSharp.Core.ManagedGpuContext.BlockBarrierRequested
            GSharpInternalNames.StateMachine.YieldedForBlockBarrier =>
                ManagedNames.CreateMemberAccess(ManagedNames.BlockBarrierRequestedName).WithTriviaFrom(node),
            // __gsharp_ctx.<name> or __gsharp_ctx-><name>
            GSharpInternalNames.StateMachine.State =>
                SyntaxFactory.MemberAccessExpression(
                    _isUnsafe ? SyntaxKind.PointerMemberAccessExpression : SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context),
                    node
                ).WithTriviaFrom(node),
            // The generated file has none of the user's using directives
            _ => RouteOrQualify(node, base.VisitIdentifierName(node))
        };
    }

    // [WHY] A warp collective passes the frame it runs in (__gsharp_ctx) to the managed helpers, which read its path and
    // visit counts through IManagedWarpFrame (#1468). A reference lets the helper update the counts of an unsafe
    // context, a struct reached through a pointer, in place: ref __gsharp_ctx or ref *__gsharp_ctx
    public override SyntaxNode? VisitArgument(ArgumentSyntax node)
    {
        if (node.Expression is not IdentifierNameSyntax
            {
                Identifier.ValueText: GSharpInternalNames.StateMachine.Context
            } frame)
        {
            var visited = base.VisitArgument(node);
            var inKeyword = SyntaxFactory.Token(SyntaxKind.InKeyword).WithTrailingTrivia(SyntaxFactory.Space);
            return IsVariableToRefReadOnlyWithoutKeyword(node) && visited is ArgumentSyntax argument
                ? argument.WithRefKindKeyword(inKeyword)
                : visited;
        }

        ExpressionSyntax target = _isUnsafe
            ? SyntaxFactory.PrefixUnaryExpression(SyntaxKind.PointerIndirectionExpression, frame.WithoutTrivia())
            : frame.WithoutTrivia();
        return SyntaxFactory.Argument(target)
            .WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword).WithTrailingTrivia(SyntaxFactory.Space))
            .WithTriviaFrom(node);
    }

    // [WHY] A variable passed to a ref readonly parameter without a keyword is passed by reference, and C# warns
    // that it should carry one (CS9192). The user's source already reports or suppresses that warning, so the copy
    // writes the keyword (in), which passes the same reference without repeating it. A value that is not a variable
    // keeps no keyword: C# passes a temporary for it, and in would not compile. The operation is looked up from the
    // expression: an argument without a keyword has the span of its expression, and the lookup of a lowered node by
    // span finds the innermost node
    private bool IsVariableToRefReadOnlyWithoutKeyword(ArgumentSyntax node) =>
        node.RefKindKeyword.IsKind(SyntaxKind.None) &&
        semanticModel.GetSafeOperation(node.Expression) is
        {
            Parent: IArgumentOperation { Parameter.RefKind: RefKind.RefReadOnlyParameter }
        } value &&
        value.IsVariable();

    // [WHY] C# ends a switch expression that is not exhaustive with an arm of its own that throws, and warns about it
    // (CS8509). The user's source already reports or suppresses that warning, so the copy writes the arm out, which
    // throws the same exception without repeating it
    public override SyntaxNode? VisitSwitchExpression(SwitchExpressionSyntax node)
    {
        var visited = base.VisitSwitchExpression(node);
        return visited is SwitchExpressionSyntax rewritten &&
               semanticModel.GetSafeOperation(node) is ISwitchExpressionOperation { IsExhaustive: false } operation
            ? AddUnmatchedArm(rewritten, PlanUnmatchedArm(operation.Value.Type, semanticModel.Compilation))
            : visited;
    }

    // <arms>, <unmatched arm>,
    // The comma after the new arm keeps the closing brace on its own line: the body is written through
    // NormalizeWhitespace, which breaks the line after an arm separator but not after the ) of the throw
    private static SwitchExpressionSyntax AddUnmatchedArm(SwitchExpressionSyntax switchExpression,
        UnmatchedSwitchArm plan)
    {
        var armsAndSeparators = switchExpression.Arms
            .Add(CreateUnmatchedArm(plan))
            .GetWithSeparators()
            .Add(SyntaxFactory.Token(SyntaxKind.CommaToken));
        return switchExpression.WithArms(SyntaxFactory.SeparatedList<SwitchExpressionArmSyntax>(armsAndSeparators));
    }

    // The exception C# throws when no arm matches (as the lowering
    // of Roslyn does): SwitchExpressionException with the boxed input, without it when the input cannot be boxed (a ref
    // struct, a pointer), and InvalidOperationException where the library has no SwitchExpressionException
    internal static UnmatchedSwitchArm PlanUnmatchedArm(ITypeSymbol? inputType, Compilation compilation)
    {
        if (compilation.GetTypeByMetadataName(SwitchExpressionExceptionMetadataName) is null)
        {
            return UnmatchedSwitchArm.InvalidOperation;
        }

        return inputType is { IsRefLikeType: false } and not (IPointerTypeSymbol or IFunctionPointerTypeSymbol)
            ? UnmatchedSwitchArm.SwitchExpressionWithValue
            : UnmatchedSwitchArm.SwitchExpression;
    }

    // var __gsharp_unmatched => throw new <SwitchExpressionException>(__gsharp_unmatched)
    // _ => throw new <SwitchExpressionException>()
    // _ => throw new global::System.InvalidOperationException()
    // <SwitchExpressionException> is global::System.Runtime.CompilerServices.SwitchExpressionException
    private static SwitchExpressionArmSyntax CreateUnmatchedArm(UnmatchedSwitchArm plan)
    {
        var withValue = plan == UnmatchedSwitchArm.SwitchExpressionWithValue;
        var pattern = withValue
            ? (PatternSyntax)SyntaxFactory.VarPattern(
                SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(UnmatchedValueName))
            )
            : SyntaxFactory.DiscardPattern();
        var arguments = withValue
            ? SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Argument(SyntaxFactory.IdentifierName(UnmatchedValueName))
            )
            : default;
        var exceptionType = plan == UnmatchedSwitchArm.InvalidOperation
            ? "global::System.InvalidOperationException"
            : "global::" + SwitchExpressionExceptionMetadataName;
        var exception = SyntaxFactory.ObjectCreationExpression(
            SyntaxFactory.ParseTypeName(exceptionType),
            SyntaxFactory.ArgumentList(arguments),
            null
        );
        return SyntaxFactory.SwitchExpressionArm(pattern, SyntaxFactory.ThrowExpression(exception));
    }

    private const string UnmatchedValueName = "__gsharp_unmatched";

    private const string SwitchExpressionExceptionMetadataName =
        "System.Runtime.CompilerServices.SwitchExpressionException";

    // [WHY] The copied bodies keep only the active branch of each #if; its directives and the disabled text would be
    // read again in the generated file, under its symbols (which define what a kept [Conditional] call needs,
    // RemovedCalls.GetSymbolsToDefine) and without the #endif or #endregion of a block that ends at a brace the copy
    // leaves out. Other directives (#pragma warning) stay as they were
    public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia) =>
        IsConditionalCompilationTrivia(trivia) ? default : base.VisitTrivia(trivia);

    private static bool IsConditionalCompilationTrivia(SyntaxTrivia trivia) =>
        trivia.Kind() is SyntaxKind.IfDirectiveTrivia or SyntaxKind.ElifDirectiveTrivia
            or SyntaxKind.ElseDirectiveTrivia or SyntaxKind.EndIfDirectiveTrivia or SyntaxKind.RegionDirectiveTrivia
            or SyntaxKind.EndRegionDirectiveTrivia or SyntaxKind.DisabledTextTrivia;

    public override SyntaxNode? VisitGenericName(GenericNameSyntax node) =>
        RouteOrQualify(node, base.VisitGenericName(node));

    public override SyntaxNode? VisitInterpolation(InterpolationSyntax node) =>
        ManagedNameQualifier.ParenthesizeInterpolation(base.VisitInterpolation(node));

    public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) =>
        VisitNestedCallable(node, base.VisitLocalFunctionStatement);

    public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) =>
        VisitNestedCallable(node, base.VisitSimpleLambdaExpression);

    public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) =>
        VisitNestedCallable(node, base.VisitParenthesizedLambdaExpression);

    public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) =>
        VisitNestedCallable(node, base.VisitAnonymousMethodExpression);

    // A lambda or local function whose expression body is a call that C# removes gets an empty body (#1540)
    private SyntaxNode? VisitNestedCallable<TNode>(TNode node, Func<TNode, SyntaxNode?> baseVisitor)
        where TNode : SyntaxNode
    {
        _inLambdaOrLocalFuncCount++;
        try
        {
            return RemovedCallEraser.WithEmptyBodyIfRemoved(node, baseVisitor(node), semanticModel);
        }
        finally
        {
            _inLambdaOrLocalFuncCount--;
        }
    }
}

namespace Kassyi.GSharp.Compiler;

/// <summary>A statement of a flattened <c>Gpu.SyncGrid(body)</c> and the body it was written in.</summary>
/// <param name="Node">
/// The statement, or the expression of an expression-bodied lambda (<c>Gpu.SyncGrid(() =&gt; x += 1)</c>).
/// </param>
/// <param name="BodyIndex">The index of the body in <see cref="SyncGridStatements.BodyParents"/>; 0 is the outermost.</param>
/// <param name="HasBodyReturn">
/// Whether the statement holds a <c>return</c> of its body (<see cref="SyncGridBody.ContainsBodyReturn"/>).
/// </param>
internal readonly record struct SyncGridElement(CSharpSyntaxNode Node, int BodyIndex, bool HasBodyReturn);

/// <summary>The statements of a <c>Gpu.SyncGrid(body)</c>, the bodies of nested <c>Gpu.SyncGrid(body)</c> statements flattened in.</summary>
/// <param name="Elements">The statements in source order.</param>
/// <param name="BodyParents">
/// For each body, the index of the body it is nested in; -1 for the outermost body (index 0).
/// </param>
internal readonly record struct SyncGridStatements(
    ImmutableArray<SyncGridElement> Elements,
    ImmutableArray<int> BodyParents)
{
    /// <summary>Whether the statements hold anything other than empty statements.</summary>
    public bool HasExecutableStatements => Elements
        .AsStackEnumerable()
        .Any(static element => element.Node is not EmptyStatementSyntax);

    /// <summary>Whether a statement of body <paramref name="body"/> itself holds a <c>return</c> of the body.</summary>
    public bool BodyHasReturn(int body) => Elements
        .AsStackEnumerable()
        .Any(body, static (element, b) => element.BodyIndex == b && element.HasBodyReturn);

    /// <summary>Whether <paramref name="ancestor"/> is <paramref name="descendant"/> or a body it is nested in.</summary>
    public bool IsWithin(int descendant, int ancestor)
    {
        for (var body = descendant; body >= 0; body = BodyParents[body])
        {
            if (body == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The body directly nested in <paramref name="ancestor"/> on the way to <paramref name="descendant"/>.</summary>
    public int ChildOnPath(int descendant, int ancestor)
    {
        var body = descendant;
        while (BodyParents[body] != ancestor)
        {
            body = BodyParents[body];
        }

        return body;
    }

    /// <summary>
    /// The bodies whose done flag guards statement <paramref name="index"/>, outermost first: each body that the statement
    /// is in and that returned in an earlier statement of its own. Empty for a statement that is never guarded
    /// (<see cref="SyncGridBody.IsNeverGuarded"/>).
    /// </summary>
    public ImmutableArray<int> GetGuardBodies(int index, SemanticModel? model)
    {
        var element = Elements[index];
        if (SyncGridBody.IsNeverGuarded(element.Node, model))
        {
            return ImmutableArray<int>.Empty;
        }

        var guards = ImmutableArray.CreateBuilder<int>();
        for (var body = element.BodyIndex; body >= 0; body = BodyParents[body])
        {
            for (var earlier = 0; earlier < index; earlier++)
            {
                if (Elements[earlier].BodyIndex == body && Elements[earlier].HasBodyReturn)
                {
                    guards.Insert(0, body);
                    break;
                }
            }
        }

        return guards.ToImmutable();
    }
}

/// <summary>
/// The statements of a <c>Gpu.SyncGrid(body)</c>, the gaps between them that get a grid sync, and the lowering of a
/// <c>return</c> of the body. One definition for the CUDA emission (<c>SyncGridMacroRule</c>), the CPU lowering
/// (<see cref="PreLoweringExpander"/>) and the analyzer (<c>GpuMacroArgumentRule</c>), so that both dialects synchronize
/// the grid at the same places (#626).
/// </summary>
/// <remarks>
/// <para>
/// A grid sync goes between two statements of the body. Nested <c>Gpu.SyncGrid(body)</c> statements are flattened into
/// the list, so their statements take part in the same gaps. There is no sync after the last statement, next to an
/// explicit <c>Gpu.SyncGrid()</c>, or around an empty statement or a local function declaration (a declaration does not
/// run).
/// </para>
/// <para>
/// A <c>return</c> of a body ends the body for the thread, as it ends the lambda in C#, but the thread still takes part in
/// the grid syncs of the remaining statements: the body declares a done flag, the <c>return</c> sets it and jumps past its
/// statement (<see cref="RewriteReturns"/>), and every later statement runs only while the flags of its bodies are clear
/// (<see cref="SyncGridStatements.GetGuardBodies"/>). A local declared in a guarded statement is declared at the top of its
/// body and assigned in the guard, so the later statements still see it (<see cref="CanSplit"/>).
/// </para>
/// <para>
/// With a semantic model, <c>Gpu.SyncGrid</c> is recognized by its symbol (<see cref="GpuMacroClassifier"/>). Without one
/// (the Tier 2 lowering tests), by its syntax: <c>Gpu.SyncGrid(...)</c> or <c>SyncGrid(...)</c>, which also matches a
/// user type named <c>Gpu</c>.
/// </para>
/// </remarks>
internal static class SyncGridBody
{
    /// <summary>Gets the lambda of a <c>Gpu.SyncGrid(body)</c> expression statement.</summary>
    /// <returns><see langword="false"/> for any other statement, and for a <c>Gpu.SyncGrid</c> whose argument is not a lambda.</returns>
    public static bool TryGetBody(
        StatementSyntax statement,
        SemanticModel? model,
        [NotNullWhen(true)] out LambdaExpressionSyntax? body)
    {
        body = null;
        return statement is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation } &&
               TryGetBody(invocation, model, out body);
    }

    /// <summary>
    /// Whether <paramref name="invocation"/> is a statement or the expression body of an enclosing
    /// <c>Gpu.SyncGrid(body)</c>, so that <see cref="Flatten"/> of that body already covers its statements.
    /// </summary>
    public static bool IsFlattenedIntoEnclosingBody(InvocationExpressionSyntax invocation, SemanticModel? model)
    {
        var lambda = invocation.Parent switch
        {
            ExpressionStatementSyntax { Parent: BlockSyntax { Parent: LambdaExpressionSyntax blockLambda } } => blockLambda,
            LambdaExpressionSyntax expressionLambda when expressionLambda.Body == invocation => expressionLambda,
            _ => null
        };
        return lambda is { Parent: ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax outer } } } &&
               TryGetBody(outer, model, out var outerBody) &&
               outerBody == lambda;
    }

    /// <summary>Flattens the statements of <paramref name="body"/>; an expression body is one statement, its expression.</summary>
    public static SyncGridStatements Flatten(LambdaExpressionSyntax body, SemanticModel? model)
    {
        var elements = ImmutableArray.CreateBuilder<SyncGridElement>();
        var bodyParents = ImmutableArray.CreateBuilder<int>();
        FlattenBody(body.Body, parent: -1, model, elements, bodyParents);
        return new SyncGridStatements(elements.ToImmutable(), bodyParents.ToImmutable());
    }

    /// <summary>
    /// Whether a grid sync follows <c>elements[index]</c>: not after the last executable statement, not next to an
    /// explicit <c>Gpu.SyncGrid()</c>, and not around an empty statement or a local function declaration.
    /// </summary>
    public static bool IsSyncPoint(ImmutableArray<SyncGridElement> elements, int index, SemanticModel? model)
    {
        var current = elements[index].Node;
        if (IsSkipped(current) || IsExplicitSync(current, model))
        {
            return false;
        }

        for (var next = index + 1; next < elements.Length; next++)
        {
            if (IsSkipped(elements[next].Node))
            {
                continue;
            }

            return !IsExplicitSync(elements[next].Node, model);
        }

        return false;
    }

    /// <summary>Whether <paramref name="statement"/> is a parameterless <c>Gpu.SyncGrid()</c> (statement or expression).</summary>
    public static bool IsExplicitSync(CSharpSyntaxNode statement, SemanticModel? model)
    {
        var invocation = statement switch
        {
            ExpressionStatementSyntax { Expression: InvocationExpressionSyntax inv } => inv,
            InvocationExpressionSyntax inv => inv,
            _ => null
        };
        return invocation != null && IsSyncGrid(invocation, model, withBody: false);
    }

    /// <summary>
    /// Whether a statement runs on the threads that returned as well: an empty statement, a local function declaration
    /// (guarding it would hide it from the calls after it), an explicit <c>Gpu.SyncGrid()</c> (every thread must reach it)
    /// and a <c>const</c> declaration (no side effect).
    /// </summary>
    public static bool IsNeverGuarded(CSharpSyntaxNode statement, SemanticModel? model) =>
        IsSkipped(statement) ||
        IsExplicitSync(statement, model) ||
        statement is LocalDeclarationStatementSyntax { IsConst: true };

    /// <summary>
    /// Whether <paramref name="statement"/> holds a <c>return</c> of the body itself: not one of a nested lambda,
    /// anonymous method or local function.
    /// </summary>
    public static bool ContainsBodyReturn(CSharpSyntaxNode statement) =>
        statement
            .DescendantNodesAndSelf(static node => !IsNestedFunction(node))
            .AsStackEnumerable()
            .Any(static node => node is ReturnStatementSyntax);

    /// <summary>
    /// Rewrites each <c>return;</c> of the body in <paramref name="statement"/> to
    /// <c>{ &lt;flagName&gt; = true; goto &lt;labelName&gt;; }</c>, copying the annotations of the <c>return</c>. A
    /// <c>return</c> of a nested function and a <c>return</c> with a value (which the analyzer reports) stay.
    /// </summary>
    public static StatementSyntax RewriteReturns(StatementSyntax statement, string flagName, string labelName) =>
        (StatementSyntax)(new ReturnRewriter(flagName, labelName).Visit(statement) ?? statement);

    /// <summary>
    /// Whether the local declaration <paramref name="declaration"/> can be split into a declaration without initializers
    /// at the top of the body and assignments in the guard: not a <c>ref</c>, <c>scoped</c> or <c>using</c> local, no
    /// pattern or deconstruction variable in an initializer (<c>out</c> variables move as well,
    /// <see cref="AddMovedOutVariables"/>), and (with a model) not a shared memory, array-like or iterator local, which the
    /// emitters declare in their own forms.
    /// </summary>
    public static bool CanSplit(LocalDeclarationStatementSyntax declaration, SemanticModel? model)
    {
        var type = declaration.Declaration.Type;
        if (declaration.UsingKeyword.IsKind(SyntaxKind.UsingKeyword) ||
            type is RefTypeSyntax or ScopedTypeSyntax or ArrayTypeSyntax ||
            declaration.Declaration.Variables
                .AsStackEnumerable()
                .Any(static v => v.Initializer != null && DeclaresNonOutVariables(v.Initializer.Value)))
        {
            return false;
        }

        if (model == null)
        {
            return true;
        }

        var typeSymbol = model.GetTypeSymbol(type);
        return !typeSymbol.IsSharedMemoryType() &&
               !typeSymbol.TryGetArrayLikeElementType(out _) &&
               !IteratorTypeResolver.IsGenericIEnumerable(typeSymbol, out _);
    }

    /// <summary>
    /// Whether <paramref name="statement"/>, when it is guarded, declares a local that cannot stay visible to the later
    /// statements: a local declaration that <see cref="CanSplit"/> rejects, or a pattern or deconstruction variable of an
    /// expression statement, an <c>if</c> condition or a <c>switch</c> expression, whose scope is the enclosing block
    /// (GSH0003). Rewriting those as assignments to a variable declared earlier could change their meaning.
    /// </summary>
    public static bool IsUnsplittable(CSharpSyntaxNode statement, SemanticModel? model) => statement switch
    {
        LocalDeclarationStatementSyntax { IsConst: true } => false,
        LocalDeclarationStatementSyntax declaration => !CanSplit(declaration, model),
        ExpressionStatementSyntax expressionStatement => DeclaresNonOutVariables(expressionStatement.Expression),
        IfStatementSyntax ifStatement => DeclaresNonOutVariables(ifStatement.Condition),
        SwitchStatementSyntax switchStatement => DeclaresNonOutVariables(switchStatement.Expression),
        _ => false
    };

    /// <summary>
    /// The <c>out</c> variables (<c>out var x</c>, <c>out int x</c>) that <paramref name="statement"/> declares into the
    /// enclosing block: in an expression statement, an <c>if</c> condition, a <c>switch</c> expression or the initializers
    /// of a local declaration, outside nested functions. When the statement is guarded, they are declared at the top of the
    /// body and passed as <c>out x</c> (<see cref="RewriteOutVariables"/>).
    /// </summary>
    public static void AddMovedOutVariables(CSharpSyntaxNode statement, ICollection<DeclarationExpressionSyntax> variables)
    {
        switch (statement)
        {
            case ExpressionStatementSyntax expressionStatement:
                AddOutVariables(expressionStatement.Expression, variables);
                break;
            case IfStatementSyntax ifStatement:
                AddOutVariables(ifStatement.Condition, variables);
                break;
            case SwitchStatementSyntax switchStatement:
                AddOutVariables(switchStatement.Expression, variables);
                break;
            case LocalDeclarationStatementSyntax declaration:
                foreach (var declarator in declaration.Declaration.Variables)
                {
                    if (declarator.Initializer != null)
                    {
                        AddOutVariables(declarator.Initializer.Value, variables);
                    }
                }

                break;
        }
    }

    /// <summary>
    /// Rewrites each <c>out var x</c> / <c>out T x</c> of <paramref name="variables"/> in <paramref name="node"/> to
    /// <c>out x</c>. The name carries the annotations of the designation, so it binds to the declared local
    /// (<see cref="LoweringIdentity.ForReference"/>, <c>CppNameResolver.AppendReferencedName</c>).
    /// </summary>
    public static TNode RewriteOutVariables<TNode>(TNode node, IReadOnlyCollection<DeclarationExpressionSyntax> variables)
        where TNode : SyntaxNode =>
        variables.Count == 0
            ? node
            : node.ReplaceNodes(
                variables,
                static (original, _) =>
                {
                    // Only declaration expressions with a single variable designation are collected
                    var designation = (SingleVariableDesignationSyntax)original.Designation;
                    return SyntaxFactory.IdentifierName(designation.Identifier.WithoutTrivia())
                        .WithTriviaFrom(original)
                        .WithAdditionalAnnotations(designation.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind));
                });

    /// <summary>
    /// The <c>goto</c> statements of <paramref name="statement"/> whose label is outside it: they would jump across a
    /// gap of the body, past the grid sync and the guards (GSH0003).
    /// </summary>
    public static void AddGotosLeaving(CSharpSyntaxNode statement, ICollection<GotoStatementSyntax> gotos)
    {
        var nodes = statement.DescendantNodesAndSelf(static node => !IsNestedFunction(node));
        nodes
            .AsStackEnumerable()
            .OfType<GotoStatementSyntax>()
            .Where(statement, static (g, s) =>
                g.IsKind(SyntaxKind.GotoStatement) &&
                g.Expression is IdentifierNameSyntax label &&
                !DefinesLabel(s, label.Identifier.ValueText)
            ).AddTo(gotos);
    }

    // Statements that neither run nor separate two running statements: an empty statement (with or without a label) and
    // a local function declaration
    private static bool IsSkipped(CSharpSyntaxNode statement) =>
        statement is EmptyStatementSyntax or
            LocalFunctionStatementSyntax or
            LabeledStatementSyntax { Statement: EmptyStatementSyntax };

    private static bool IsNestedFunction(SyntaxNode node) =>
        node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax;

    // A pattern or deconstruction variable outside nested functions (out variables can move)
    private static bool DeclaresNonOutVariables(SyntaxNode node) =>
        node
            .DescendantNodesAndSelf(static n => !IsNestedFunction(n))
            .AsStackEnumerable()
            .Any(static n => n is SingleVariableDesignationSyntax designation && !IsOutVariable(designation));

    // out var x / out int x: a declaration expression of one variable passed as an out argument
    private static bool IsOutVariable(SingleVariableDesignationSyntax designation) =>
        designation.Parent is DeclarationExpressionSyntax
        {
            Parent: ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.OutKeyword }
        };

    private static void AddOutVariables(SyntaxNode node, ICollection<DeclarationExpressionSyntax> variables) =>
        node
            .DescendantNodesAndSelf(static n => !IsNestedFunction(n))
            .AsStackEnumerable()
            .OfType<SingleVariableDesignationSyntax>()
            .Where(IsOutVariable)
            .Select(static designation => designation.Parent as DeclarationExpressionSyntax)
            .OfType<DeclarationExpressionSyntax>()
            .AddTo(variables);

    private static bool DefinesLabel(CSharpSyntaxNode statement, string label) =>
        statement
            .DescendantNodesAndSelf(static node => !IsNestedFunction(node))
            .AsStackEnumerable()
            .Any(label, static (node, name) => node is LabeledStatementSyntax labeled && labeled.Identifier.ValueText == name);

    private static bool TryGetBody(
        InvocationExpressionSyntax invocation,
        SemanticModel? model,
        [NotNullWhen(true)] out LambdaExpressionSyntax? body)
    {
        body = null;
        return IsSyncGrid(invocation, model, withBody: true) &&
               GpuCodeConstraints.TryGetSyncGridBody(invocation, out body);
    }

    private static void FlattenBody(
        CSharpSyntaxNode body,
        int parent,
        SemanticModel? model,
        ImmutableArray<SyncGridElement>.Builder elements,
        ImmutableArray<int>.Builder bodyParents)
    {
        var bodyIndex = bodyParents.Count;
        bodyParents.Add(parent);
        switch (body)
        {
            case BlockSyntax block:
                foreach (var statement in block.Statements)
                {
                    if (TryGetBody(statement, model, out var nested))
                    {
                        FlattenBody(nested.Body, bodyIndex, model, elements, bodyParents);
                    }
                    else
                    {
                        elements.Add(new SyncGridElement(statement, bodyIndex, ContainsBodyReturn(statement)));
                    }
                }

                break;
            // An expression body that is itself a Gpu.SyncGrid(body) is that body
            case InvocationExpressionSyntax invocation when TryGetBody(invocation, model, out var nestedBody):
                FlattenBody(nestedBody.Body, bodyIndex, model, elements, bodyParents);
                break;
            case ExpressionSyntax expression:
                elements.Add(new SyncGridElement(expression, bodyIndex, HasBodyReturn: false));
                break;
        }
    }

    private static bool IsSyncGrid(InvocationExpressionSyntax invocation, SemanticModel? model, bool withBody)
    {
        if (model != null)
        {
            return model.GetSymbol(invocation) is IMethodSymbol symbol &&
                   GpuMacroClassifier.Classify(symbol) is { Kind: GpuMacroKind.SyncGrid } call &&
                   call.HasBody == withBody;
        }

        var isSyncGridSyntax = invocation.Expression switch
        {
            MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: TypeConstants.GpuType },
                Name.Identifier.ValueText: TypeConstants.GpuMacros.SyncGrid
            } => true,
            IdentifierNameSyntax { Identifier.ValueText: TypeConstants.GpuMacros.SyncGrid } => true,
            _ => false
        };
        return isSyncGridSyntax && invocation.ArgumentList.Arguments.Count == (withBody ? 1 : 0);
    }

    // return; -> { <flag> = true; goto <label>; }, not inside nested functions
    private sealed class ReturnRewriter(string flagName, string labelName) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => node;

        public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => node;

        public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) => node;

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) => node;

        public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
        {
            if (node.Expression != null)
            {
                return node;
            }

            var annotations = node.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind);
            // <flag> = true;
            var setFlag = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName(flagName),
                    SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)
                )
            ).WithAdditionalAnnotations(annotations);
            // goto <label>;
            var jump = SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement, SyntaxFactory.IdentifierName(labelName))
                .WithAdditionalAnnotations(annotations);
            return SyntaxFactory.Block(setFlag, jump).WithAdditionalAnnotations(annotations);
        }
    }
}

namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// Makes the closed copies of generic kernels and device functions: for each instance, the declaration with its type
/// parameters replaced by the type arguments (<see cref="GenericCopyPlanner"/>, <see cref="GenericCopyEmitter"/>),
/// added next to the declaration in a derived compilation, and checked against the declaration
/// (<see cref="GenericCopyChecker"/>). A copy that passes is an ordinary method that the rest of the pipeline can
/// transpile as it is; <see cref="GenericInstanceTable"/> names it as its instance.
/// </summary>
internal static class GenericCopies
{
    private const string NoBody = "the declaration has no body to copy";

    /// <summary>Makes the copies of <paramref name="instances"/>, all in one derived compilation.</summary>
    public static GenericCopySet Create(Compilation compilation, ImmutableArray<GenericInstance> instances)
    {
        var plans = ImmutableArray.CreateBuilder<GenericCopyPlan>(instances.Length);
        foreach (var instance in instances)
        {
            plans.Add(Plan(compilation, instance));
        }

        return Create(compilation, plans.MoveToImmutable());
    }

    /// <summary>Plans the copy of <paramref name="instance"/> (<see cref="GenericCopyPlanner"/>).</summary>
    public static GenericCopyPlan Plan(Compilation compilation, GenericInstance instance)
    {
        if (FindDeclaration(instance) is not { } declaration)
        {
            return new GenericCopyPlan(instance, ImmutableDictionary<int, CopyRewrite>.Empty,
                ImmutableArray.Create(new CopyProblem(0, NoBody)));
        }

        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        return GenericCopyPlanner.Plan(declaration, CopyNodeIndex.List(declaration), model,
            new InstanceSymbolMap(instance, compilation), instance);
    }

    /// <summary>Writes the copies that <paramref name="plans"/> describe into one derived compilation and checks them.</summary>
    public static GenericCopySet Create(Compilation compilation, ImmutableArray<GenericCopyPlan> plans)
    {
        var prepared = new PreparedCopy?[plans.Length];
        var byType = new Dictionary<TypeDeclarationSyntax, ImmutableArray<MemberDeclarationSyntax>.Builder>();
        for (var i = 0; i < plans.Length; i++)
        {
            if (Prepare(compilation, plans[i]) is not { } copy)
            {
                continue;
            }

            prepared[i] = copy;
            if (!byType.TryGetValue(copy.ContainingType, out var members))
            {
                members = ImmutableArray.CreateBuilder<MemberDeclarationSyntax>();
                byType[copy.ContainingType] = members;
            }

            members.Add(copy.Copy);
        }

        var derived = compilation;
        var replacedTrees = new Dictionary<SyntaxTree, SyntaxTree>();
        foreach (var tree in CollectTrees(byType))
        {
            var types = ImmutableArray.CreateBuilder<TypeDeclarationSyntax>();
            byType
                .AsStackEnumerable()
                .Where(tree, static (entry, t) => entry.Key.SyntaxTree == t)
                .Select(static entry => entry.Key)
                .AddTo(types);

            var root = tree.GetRoot().ReplaceNodes<SyntaxNode, TypeDeclarationSyntax>(types, (original, rewritten) =>
                rewritten.AddMembers(byType[original].ToArray()));
            var replaced = tree.WithRootAndOptions(root, tree.Options);
            replacedTrees[tree] = replaced;
            derived = derived.ReplaceSyntaxTree(tree, replaced);
        }

        var copies = ImmutableArray.CreateBuilder<GenericCopy>(plans.Length);
        for (var i = 0; i < plans.Length; i++)
        {
            copies.Add(prepared[i] is { } copy
                ? Finish(derived, replacedTrees[copy.Declaration.SyntaxTree], copy)
                : new GenericCopy(plans[i].Instance, null, null,
                    ImmutableArray.Create(new CopyFinding(Location.None, NoBody))));
        }

        return new GenericCopySet(derived, copies.MoveToImmutable());
    }

    private static MethodDeclarationSyntax? FindDeclaration(GenericInstance instance) =>
        instance.Definition.DeclaringSyntaxReferences is [var reference] &&
        reference.GetSyntax() is MethodDeclarationSyntax { Parent: TypeDeclarationSyntax } declaration &&
        (declaration.Body is not null || declaration.ExpressionBody is not null)
            ? declaration
            : null;

    private static PreparedCopy? Prepare(Compilation compilation, GenericCopyPlan plan)
    {
        var instance = plan.Instance;
        if (FindDeclaration(instance) is not { Parent: TypeDeclarationSyntax type } declaration)
        {
            return null;
        }

        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        var nodes = CopyNodeIndex.List(declaration);
        var map = new InstanceSymbolMap(instance, compilation);
        var annotated = CopyNodeIndex.Annotate(LoweringAnnotations.AnnotateOriginalSpans(declaration, compilation));
        var marker = new SyntaxAnnotation(GenericCopyEmitter.CopyAnnotationKind);
        var copy = GenericCopyEmitter.Emit(annotated, plan, GenericInstanceNames.GetCopyName(instance), marker);
        return new PreparedCopy(instance, declaration, type, nodes, model, map, plan, copy, marker);
    }

    private static GenericCopy Finish(Compilation derived, SyntaxTree tree, PreparedCopy prepared)
    {
        var copy = tree.GetRoot()
            .GetAnnotatedNodes(prepared.Marker)
            .AsStackEnumerable()
            .OfType<MethodDeclarationSyntax>()
            .First();
        var copyModel = derived.GetSemanticModel(tree);
        var symbol = copyModel.GetDeclaredSymbol(copy);
        if (symbol is not null)
        {
            GenericInstanceTable.Register(symbol, prepared.Instance);
        }

        var problems = prepared.Plan.Problems.AddRange(GenericCopyChecker.Check(
            prepared.Nodes,
            prepared.Declaration,
            prepared.Model,
            copy,
            copyModel,
            prepared.Plan,
            prepared.Map
        ));
        var findings = ImmutableArray.CreateBuilder<CopyFinding>(problems.Length);
        foreach (var problem in problems)
        {
            var node = problem.NodeIndex > 0 ? prepared.Nodes[problem.NodeIndex] : prepared.Declaration;
            findings.Add(new CopyFinding(node.GetLocation(), problem.Reason));
        }

        return new GenericCopy(prepared.Instance, copy, symbol, findings.MoveToImmutable());
    }

    private static SyntaxTree[] CollectTrees(
        Dictionary<TypeDeclarationSyntax, ImmutableArray<MemberDeclarationSyntax>.Builder> byType)
    {
        var trees = new HashSet<SyntaxTree>();
        foreach (var type in byType.Keys)
        {
            trees.Add(type.SyntaxTree);
        }

        return trees
            .AsStackEnumerable()
            .ToArray();
    }

    private sealed record PreparedCopy(
        GenericInstance Instance,
        MethodDeclarationSyntax Declaration,
        TypeDeclarationSyntax ContainingType,
        SyntaxNode[] Nodes,
        SemanticModel Model,
        InstanceSymbolMap Map,
        GenericCopyPlan Plan,
        MethodDeclarationSyntax Copy,
        SyntaxAnnotation Marker);
}

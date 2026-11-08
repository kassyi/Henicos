namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Binds a body declared in another syntax tree than the one being transpiled (a struct's constructor, property
/// accessor, operator or <c>Dispose</c>, which may live in another file): installs a <see cref="Query"/> and a
/// <see cref="Reporter"/> for the semantic model of that tree, and puts the previous ones back.
/// </summary>
/// <remarks>
/// <code>
/// var saved = SemanticModelSwitch.Enter(ref context, tree);
/// try { ... } finally { saved.Restore(ref context); }
/// </code>
/// </remarks>
internal readonly struct SemanticModelSwitch
{
    private readonly Query _query;
    private readonly Reporter _reporter;

    private SemanticModelSwitch(Query query, Reporter reporter, SemanticModel model)
    {
        _query = query;
        _reporter = reporter;
        Model = model;
    }

    /// <summary>The semantic model of the tree that <see cref="Enter"/> switched to.</summary>
    public SemanticModel Model { get; }

    /// <summary>
    /// Switches <paramref name="context"/> to the semantic model of <paramref name="tree"/>, keeping the intrinsic map
    /// and the used structs, and the failure sink of the transpilation.
    /// </summary>
    public static SemanticModelSwitch Enter(ref TranspileContext context, SyntaxTree tree)
    {
        var query = context.Query;
        var reporter = context.Reporter;
        var model = query.SemanticModel.Compilation.GetSemanticModel(tree);
        context.Query = new Query(model, query.BclIntrinsicMap, query.UsedStructs);
        context.Reporter = reporter.WithSemanticModel(model);
        return new SemanticModelSwitch(query, reporter, model);
    }

    /// <summary>Puts back the <see cref="Query"/> and <see cref="Reporter"/> that <see cref="Enter"/> replaced.</summary>
    public void Restore(ref TranspileContext context)
    {
        context.Query = _query;
        context.Reporter = _reporter;
    }
}

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Locals;

/// <summary>
/// What <see cref="LocalFunctionOrder"/> needs to write the local functions of a statement list: which statements use
/// which local functions, and which local functions call themselves or each other. Plain data, computed once per
/// statement list, so that the writer decides nothing.
/// </summary>
/// <remarks>
/// A statement uses a local function when an identifier of its tree names it (same name, same region and, when the
/// model binds it, the same symbol). A local function is recursive when its uses lead back to itself through the uses
/// of the local functions of the list; the local functions that lead to each other form its group.
/// </remarks>
internal sealed class LocalFunctionPlan
{
    private readonly bool[] _isFunction;
    private readonly int[][] _uses;
    private readonly int[][] _groups;
    private readonly bool[] _recursive;

    private LocalFunctionPlan(bool[] isFunction, int[][] uses, int[][] groups, bool[] recursive)
    {
        _isFunction = isFunction;
        _uses = uses;
        _groups = groups;
        _recursive = recursive;
    }

    /// <summary>
    /// The plan of <paramref name="statements"/>, or <see langword="null"/> when it declares no local function.
    /// </summary>
    /// <param name="statements">The statement list.</param>
    /// <param name="model">The semantic model of the tree of the list.</param>
    public static LocalFunctionPlan? Create(SyntaxList<StatementSyntax> statements, SemanticModel model)
    {
        var count = statements.Count;
        var isFunction = new bool[count];
        var any = false;
        for (var i = 0; i < count; i++)
        {
            isFunction[i] = statements[i] is LocalFunctionStatementSyntax;
            any |= isFunction[i];
        }

        if (!any)
        {
            return null;
        }

        var uses = FindUses(statements, model);
        var reach = FindReach(isFunction, uses);
        var groups = new int[count][];
        var recursive = new bool[count];
        for (var i = 0; i < count; i++)
        {
            if (!isFunction[i])
            {
                groups[i] = [];
                continue;
            }

            recursive[i] = reach[i, i];
            List<int> group = [];
            for (var j = 0; j < count; j++)
            {
                if (j == i || (isFunction[j] && reach[i, j] && reach[j, i]))
                {
                    group.Add(j);
                }
            }

            groups[i] = [.. group];
        }

        return new LocalFunctionPlan(isFunction, uses, groups, recursive);
    }

    /// <summary>Whether the statement at <paramref name="index"/> is a local function.</summary>
    public bool IsFunction(int index) => _isFunction[index];

    /// <summary>The indices of the local functions that the statement at <paramref name="index"/> uses.</summary>
    public IReadOnlyList<int> Uses(int index) => _uses[index];

    /// <summary>
    /// The local function at <paramref name="index"/> and the ones that lead to each other with it, in order.
    /// </summary>
    public IReadOnlyList<int> Group(int index) => _groups[index];

    /// <summary>Whether the local function at <paramref name="index"/> reaches itself.</summary>
    public bool IsRecursive(int index) => _recursive[index];

    private static int[][] FindUses(SyntaxList<StatementSyntax> statements, SemanticModel model)
    {
        var byName = new Dictionary<string, List<int>>();
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i] is LocalFunctionStatementSyntax function)
            {
                var name = function.Identifier.ValueText;
                if (!byName.TryGetValue(name, out var indices))
                {
                    byName[name] = indices = [];
                }

                indices.Add(i);
            }
        }

        var uses = new int[statements.Count][];
        for (var s = 0; s < statements.Count; s++)
        {
            List<IdentifierNameSyntax> identifiers = [];
            statements[s]
                .DescendantNodesAndSelf()
                .AsStackEnumerable()
                .OfType<IdentifierNameSyntax>()
                .AddTo(identifiers);
            List<int> used = [];
            foreach (var identifier in identifiers)
            {
                if (!byName.TryGetValue(identifier.Identifier.ValueText, out var candidates))
                {
                    continue;
                }

                foreach (var function in candidates)
                {
                    if (!used.Contains(function) && Names(statements[function], identifier, model))
                    {
                        used.Add(function);
                    }
                }
            }

            used.Sort();
            uses[s] = [.. used];
        }

        return uses;
    }

    // The identifier names the local function: its region and, when the model binds it, its symbol
    private static bool Names(StatementSyntax function, IdentifierNameSyntax identifier, SemanticModel model)
    {
        var symbol = model.GetDeclaredSymbol<ISymbol>(function);
        return LoweringIdentity.GetRegion(identifier) == LoweringIdentity.GetRegion(function) &&
               (symbol == null || model.GetSymbol(identifier).IsEqualTo(symbol));
    }

    // reach[a, b]: the local function a uses b, directly or through other local functions
    private static bool[,] FindReach(bool[] isFunction, int[][] uses)
    {
        var count = isFunction.Length;
        var reach = new bool[count, count];
        for (var from = 0; from < count; from++)
        {
            if (!isFunction[from])
            {
                continue;
            }

            Stack<int> pending = new();
            pending.Push(from);
            while (pending.Count > 0)
            {
                foreach (var next in uses[pending.Pop()])
                {
                    if (isFunction[next] && !reach[from, next])
                    {
                        reach[from, next] = true;
                        pending.Push(next);
                    }
                }
            }
        }

        return reach;
    }
}

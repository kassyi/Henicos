using System.Globalization;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Writes the statements of one split kernel into a <see cref="KernelGenerationState"/>: the locals, one split
/// statement at a time (<see cref="FuzzSplitStatementShapes"/>), ordinary assignments between them and the final
/// checksum folds.
/// </summary>
internal sealed class FuzzSplitKernelBuilder(KernelGenerationState state, FuzzSplitForms forms)
{
    private static readonly string[] s_uniformConditions =
    [
        "BlockDimension.X > 0",
        "(BlockDimension.X & 1) == 0",
        "GridDimension.X >= 1",
        "BlockDimension.X < 32",
        "GridDimension.X == 0",
    ];

    private readonly List<ScalarLocalVariable> _ints = [];
    private ScalarLocalVariable? _uint;
    private ScalarLocalVariable? _long;
    private bool _hasArray;

    /// <summary>Gets the target of the statement being written.</summary>
    internal string A { get; private set; } = string.Empty;

    /// <summary>Gets the variable the statement being written both writes and reads around its split point.</summary>
    internal string B { get; private set; } = string.Empty;

    /// <summary>Gets a third variable of the statement being written; <c>A</c>, <c>B</c> and <c>C</c> are
    /// distinct.</summary>
    internal string C { get; private set; } = string.Empty;

    /// <summary>Gets the uint local.</summary>
    internal string UInt =>
        _uint?.Name ?? throw new InvalidOperationException("The prologue declares the uint local.");

    /// <summary>Gets the long local.</summary>
    internal string Long =>
        _long?.Name ?? throw new InvalidOperationException("The prologue declares the long local.");

    /// <summary>Gets the int locals.</summary>
    internal IReadOnlyList<ScalarLocalVariable> Ints => _ints;

    /// <summary>Gets the random source of the kernel.</summary>
    internal FuzzRandom Rng => state.Rng;

    /// <summary>
    /// Declares the scalar locals, four extra int locals, the struct local and, unless #1554 is avoided, the stack
    /// array.
    /// </summary>
    internal void EmitPrologue()
    {
        ScalarFuzzGenerator.InitializeScalarLocals(state);
        for (int i = 0; i < 4; i++)
        {
            string name = string.Create(CultureInfo.InvariantCulture, $"s{i}");
            string init = i switch
            {
                0 => "(inA[gid] ^ inC[gid])",
                1 => "(inB[gid] - inA[gid])",
                2 => string.Create(CultureInfo.InvariantCulture, $"(inC[gid] + {Rng.NextRange(1, 99)})"),
                _ => "(inB[gid] * 3)",
            };
            state.Statements.Add($"int {name} = {init};");
            state.Locals.Add(new ScalarLocalVariable(name, FuzzTypeInfo.Get(FuzzScalarKind.Int32)));
        }

        foreach (var local in state.Locals)
        {
            switch (local.Type.Kind)
            {
                case FuzzScalarKind.Int32:
                    _ints.Add(local);
                    break;
                case FuzzScalarKind.UInt32:
                    _uint ??= local;
                    break;
                case FuzzScalarKind.Int64:
                    _long ??= local;
                    break;
            }
        }

        state.Statements.Add(
            "FuzzStructL1 sv = new FuzzStructL1 { A = inA[gid], B = (short)inB[gid], Tag = (byte)inC[gid] };");
        _hasArray = !state.IssueFilter.IsAvoided(FuzzSplitKernelEmitter.StackArrayAcrossSplitIssue);
        if (_hasArray)
        {
            state.Statements.Add("int[] arr = new int[4];");
            state.Statements.Add("arr[0] = inA[gid];");
            state.Statements.Add("arr[1] = inB[gid];");
            state.Statements.Add("arr[2] = inC[gid];");
            state.Statements.Add($"arr[3] = {_ints[0].Name};");
        }
    }

    /// <summary>Writes one split statement of a kind in <c>forms</c>, skipping the shapes avoided for open
    /// issues.</summary>
    internal void EmitSplitStatement()
    {
        var enabled = new List<FuzzSplitShape[]>(4);
        AddIfEnabled(enabled, FuzzSplitForms.Shuffle, FuzzSplitStatementShapes.Shuffle);
        AddIfEnabled(enabled, FuzzSplitForms.Barrier, FuzzSplitStatementShapes.Barrier);
        AddIfEnabled(enabled, FuzzSplitForms.Iterator, FuzzSplitStatementShapes.Iterator);
        AddIfEnabled(enabled, FuzzSplitForms.SuspendingCall, FuzzSplitStatementShapes.SuspendingCall);

        var shapes = Rng.Choose(enabled);
        bool avoidArgumentOrder = state.IssueFilter.IsAvoided(FuzzSplitKernelEmitter.SplitArgumentOrderIssue);
        var allowed = new List<FuzzSplitShape>(shapes.Length);
        foreach (var shape in shapes)
        {
            if (!(shape.ArgumentOrderIssue && avoidArgumentOrder) && (!shape.NeedsArray || _hasArray))
            {
                allowed.Add(shape);
            }
        }

        PickVariables();
        Rng.Choose(allowed).Emit(this);
    }

    /// <summary>Writes an ordinary assignment with no split point, so that the split points see varied
    /// values.</summary>
    internal void EmitPlainStatement()
    {
        var target = Rng.Choose(state.Locals);
        if (target.Type.IsEnum || target.Type.Kind == FuzzScalarKind.Boolean || Rng.NextBool())
        {
            ScalarFuzzGenerator.EmitAssignmentStatement(state, target);
        }
        else
        {
            ScalarFuzzGenerator.EmitCompoundAssignmentStatement(state, target);
        }
    }

    /// <summary>Folds every local, the struct fields and the array elements into the checksum.</summary>
    internal void EmitFinalFolds()
    {
        FuzzChecksumEmitter.EmitChecksumFoldAll(state);
        FuzzChecksumEmitter.EmitChecksumFoldExpression(state, "((ulong)(long)sv.A)");
        FuzzChecksumEmitter.EmitChecksumFoldExpression(state, "((ulong)(long)sv.B)");
        if (!_hasArray)
        {
            return;
        }

        for (int i = 0; i < 4; i++)
        {
            FuzzChecksumEmitter.EmitChecksumFoldExpression(
                state,
                string.Create(CultureInfo.InvariantCulture, $"((ulong)(long)arr[{i}])"));
        }
    }

    /// <summary>Adds a statement to the kernel.</summary>
    internal void Add(string statement) => state.Statements.Add(statement);

    /// <summary>Registers an int local that a shape declared, so that later statements and the checksum use
    /// it.</summary>
    internal void AddIntLocal(string name) =>
        state.Locals.Add(new ScalarLocalVariable(name, FuzzTypeInfo.Get(FuzzScalarKind.Int32)));

    /// <summary>Returns a fresh local name with <paramref name="prefix"/>.</summary>
    internal string NewName(string prefix) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}{++state.TempCounter}");

    /// <summary>Returns a small positive literal.</summary>
    internal string K() => Rng.NextRange(1, 9).ToString(CultureInfo.InvariantCulture);

    /// <summary>Returns an int binary operator that cannot fail.</summary>
    internal string Op() => Rng.NextBelow(5) switch
    {
        0 => "+",
        1 => "-",
        2 => "^",
        3 => "|",
        _ => "&",
    };

    /// <summary>Returns a condition that every thread of the launch evaluates alike.</summary>
    internal string Uniform() => Rng.Choose(s_uniformConditions);

    /// <summary>Returns an int expression with no split point; it may write locals (interference).</summary>
    internal string Expr()
    {
        var intType = FuzzTypeInfo.Get(FuzzScalarKind.Int32);
        string expression = FuzzScalarExpressionEmitter.GenerateExpression(state, intType, depth: 1, maxDepth: 2);
        return FuzzScalarExpressionEmitter.EnsureType(expression, intType);
    }

    /// <summary>Returns a shuffle of the int <paramref name="value"/> that reads the calling lane.</summary>
    internal string Shuffle(string value)
    {
        const string Mask = FuzzSplitOracle.FullMaskLiteral;
        return Rng.NextBelow(4) switch
        {
            0 => $"Warp.ShuffleSync({Mask}, ({value}), {FuzzSplitOracle.SelfLaneExpression})",
            1 => $"Warp.ShuffleXorSync({Mask}, ({value}), 0)",
            2 => $"Warp.ShuffleUpSync({Mask}, ({value}), 0u)",
            _ => $"Warp.ShuffleDownSync({Mask}, ({value}), 0u)",
        };
    }

    private void AddIfEnabled(List<FuzzSplitShape[]> enabled, FuzzSplitForms form, FuzzSplitShape[] shapes)
    {
        if ((forms & form) != 0)
        {
            enabled.Add(shapes);
        }
    }

    private void PickVariables()
    {
        int a = Rng.NextBelow(_ints.Count);
        int b = (a + 1 + Rng.NextBelow(_ints.Count - 1)) % _ints.Count;
        int c = a;
        while (c == a || c == b)
        {
            c = Rng.NextBelow(_ints.Count);
        }

        A = _ints[a].Name;
        B = _ints[b].Name;
        C = _ints[c].Name;
    }
}

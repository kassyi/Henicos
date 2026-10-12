namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// A lightweight value type wrapper that constructs and manages a lazy expression tree of signed GPU BigInteger arithmetic.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Deferred sign resolution allows robust evaluation of operations where resultant signs depend on
/// runtime operand magnitudes (e.g. addition of different signs or subtraction of larger numbers).
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> As a readonly struct wrapping an immutable AST node, chaining operators creates lightweight expression trees without allocating device buffers until evaluated.
/// </para>
/// </remarks>
public readonly struct GpuBigIntExpr
{
    /// <summary>
    /// Gets the root AST node of this signed expression tree.
    /// </summary>
    public IGpuBigIntExpression Node { get; }

    internal GpuBigIntExpr(IGpuBigIntExpression node)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
    }

    /// <summary>
    /// Implicitly wraps a <see cref="GpuBigInt"/> as a signed expression leaf node.
    /// </summary>
    /// <param name="b">The signed big integer instance.</param>
    public static implicit operator GpuBigIntExpr(GpuBigInt b)
        => new(new GpuBigIntLeafExpression(b));

    /// <summary>
    /// Constructs a lazy multiplication expression between two signed big integer expressions.
    /// </summary>
    /// <param name="left">The left signed expression operand.</param>
    /// <param name="right">The right signed expression operand.</param>
    /// <returns>A new <see cref="GpuBigIntExpr"/> representing the multiplication AST node.</returns>
    public static GpuBigIntExpr operator *(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new SignedMulExpression(left.Node, right.Node));

    /// <summary>
    /// Constructs a lazy division expression between two signed big integer expressions.
    /// </summary>
    /// <param name="left">The dividend signed expression operand.</param>
    /// <param name="right">The divisor signed expression operand.</param>
    /// <returns>A new <see cref="GpuBigIntExpr"/> representing the division AST node.</returns>
    public static GpuBigIntExpr operator /(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new SignedDivExpression(left.Node, right.Node));

    /// <summary>
    /// Constructs a lazy addition expression with deferred sign resolution between two signed big integer expressions.
    /// </summary>
    /// <param name="left">The left signed expression operand.</param>
    /// <param name="right">The right signed expression operand.</param>
    /// <returns>A new <see cref="GpuBigIntExpr"/> representing the addition AST node.</returns>
    public static GpuBigIntExpr operator +(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new SignedAddExpression(left.Node, right.Node));

    /// <summary>
    /// Constructs a lazy subtraction expression with deferred sign resolution between two signed big integer expressions.
    /// </summary>
    /// <param name="left">The minuend signed expression operand.</param>
    /// <param name="right">The subtrahend signed expression operand.</param>
    /// <returns>A new <see cref="GpuBigIntExpr"/> representing the subtraction AST node.</returns>
    public static GpuBigIntExpr operator -(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new SignedSubExpression(left.Node, right.Node));

    /// <summary>
    /// Constructs a lazy negation expression for the specified signed big integer expression.
    /// </summary>
    /// <param name="left">The signed expression operand to negate.</param>
    /// <returns>A new <see cref="GpuBigIntExpr"/> representing the negation AST node.</returns>
    public static GpuBigIntExpr operator -(GpuBigIntExpr left)
        => new(new SignedNegateExpression(left.Node));

    /// <summary>
    /// Constructs a lazy bitwise NOT expression for the specified signed big integer expression.
    /// </summary>
    /// <param name="left">The signed expression operand.</param>
    /// <returns>A new <see cref="GpuBigIntExpr"/> representing the bitwise NOT AST node.</returns>
    public static GpuBigIntExpr operator ~(GpuBigIntExpr left)
        => new(new SignedBitwiseNotExpression(left.Node));

    /// <summary>
    /// Constructs a lazy expression that resolves to the maximum of two signed big integer expressions.
    /// </summary>
    /// <param name="left">The first signed expression operand.</param>
    /// <param name="right">The second signed expression operand.</param>
    /// <returns>A new <see cref="GpuBigIntExpr"/> representing the Max AST node.</returns>
    public static GpuBigIntExpr Max(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new SignedMaxExpression(left.Node, right.Node));

    /// <summary>
    /// Constructs a lazy expression that resolves to the minimum of two signed big integer expressions.
    /// </summary>
    /// <param name="left">The first signed expression operand.</param>
    /// <param name="right">The second signed expression operand.</param>
    /// <returns>A new <see cref="GpuBigIntExpr"/> representing the Min AST node.</returns>
    public static GpuBigIntExpr Min(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new SignedMinExpression(left.Node, right.Node));

    /// <summary>
    /// Evaluates this signed expression into the provided destination buffer.
    /// </summary>
    /// <param name="destination">The pre-allocated GPU buffer to store the evaluated magnitude.</param>
    /// <returns>A new <see cref="GpuBigInt"/> instance wrapping the destination buffer with the evaluated sign.</returns>
    public GpuBigInt Evaluate(GpuBigUInt destination) => Node.Evaluate(destination.Workspace, destination);

    /// <summary>
    /// Evaluates this signed expression into a newly rented temporary GPU buffer.
    /// </summary>
    /// <param name="workspace">The GPU workspace used for temporary buffer allocation.</param>
    /// <returns>A new <see cref="GpuBigInt"/> instance wrapping the temporary buffer with the evaluated sign.</returns>
    public GpuBigInt Evaluate(GpuWorkspace workspace) => Node.Evaluate(workspace);

    /// <summary>
    /// Compiles this expression tree into an executable CUDA Graph, eliminating launch overhead for repeated executions.
    /// The caller is responsible for disposing the returned graph.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="targetResult">An optional pre-allocated buffer for the final result.</param>
    /// <returns>An executable graph instance that can be launched repeatedly.</returns>
    public GpuBigIntExecutableGraph CompileToGraph(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        workspace.Provider.BeginCapture(workspace.Stream);

        GpuBigInt result;
        try
        {
            result = Node.Evaluate(workspace, targetResult);
        }
        catch
        {
            workspace.Provider.EndCapture(workspace.Stream);
            throw;
        }

        var graph = workspace.Provider.EndCapture(workspace.Stream);
        var graphExec = workspace.Provider.InstantiateGraph(graph);
        return new GpuBigIntExecutableGraph(workspace, graph, graphExec, result);
    }

    /// <summary>
    /// Evaluates this expression tree by dynamically compiling it into a CUDA Graph, launching it, and destroying it immediately.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="targetResult">An optional pre-allocated buffer for the final result.</param>
    /// <returns>A new <see cref="GpuBigInt"/> instance wrapping the evaluated result.</returns>
    public GpuBigInt EvaluateWithCudaGraph(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        using var executableGraph = CompileToGraph(workspace, targetResult);
        executableGraph.Execute();
        return executableGraph.Result;
    }
}

/// <summary>
/// Represents a compiled, executable CUDA Graph for a GpuBigInt expression tree.
/// </summary>
public readonly struct GpuBigIntExecutableGraph : IDisposable
{
    private readonly GpuWorkspace _workspace;
    private readonly IntPtr _graph;
    private readonly IntPtr _graphExec;

    /// <summary>
    /// Gets the buffer holding the result of the graph execution. 
    /// This buffer is only valid and updated after <see cref="Execute"/> is called.
    /// </summary>
    public GpuBigInt Result { get; }

    internal GpuBigIntExecutableGraph(GpuWorkspace workspace, IntPtr graph, IntPtr graphExec, GpuBigInt result)
    {
        _workspace = workspace;
        _graph = graph;
        _graphExec = graphExec;
        Result = result;
    }

    /// <summary>
    /// Launches the executable graph on the GPU and waits for completion.
    /// </summary>
    public void Execute()
    {
        _workspace.Provider.LaunchGraph(_graphExec, _workspace.Stream);
        _workspace.Provider.SynchronizeStream(_workspace.Stream);
    }

    /// <summary>
    /// Destroys the underlying CUDA Graph and executable graph instances.
    /// </summary>
    public void Dispose()
    {
        if (_workspace != null && _graph != IntPtr.Zero)
        {
            _workspace.Provider.DestroyGraph(_graph, _graphExec);
        }
    }
}

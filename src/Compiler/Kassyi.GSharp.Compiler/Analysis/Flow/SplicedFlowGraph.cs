namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The control flow graph of a kernel or device function body with the bodies that run inside it spliced in where they
/// run (<see cref="SplicedFlowBuilder"/>). Node 0 is the start, before the entry block of the body.
/// </summary>
/// <param name="Nodes">The nodes, by index.</param>
/// <param name="Exit">The last node of the exit block of the body.</param>
internal sealed record SplicedFlowGraph(ImmutableArray<SplicedFlowNode> Nodes, int Exit)
{
    /// <summary>The successors of each node, by index.</summary>
    public ImmutableArray<int>[] GetSuccessors()
    {
        var successors = new ImmutableArray<int>[Nodes.Length];
        for (var i = 0; i < successors.Length; i++)
        {
            successors[i] = Nodes[i].Successors.ToImmutable();
        }

        return successors;
    }
}

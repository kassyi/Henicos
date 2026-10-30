namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Finds the locals that are live right after a point where the CPU dialects split the state machine of a kernel or
/// device function, on the spliced flow graph of its body (GSH0039, GSH0041, GSH0072; A-21, #668).
/// </summary>
/// <remarks>
/// <para>
/// The graph and the split points are <see cref="SplicedFlowBuilder"/>'s; the reads, writes, splits and pins of each
/// node are <see cref="LivenessRecorder"/>'s. A local is live after a split when some path from the split reads it
/// before writing it. Declaring it, assigning it (rebinding a <c>ref</c> local), passing it as an <c>out</c> argument or
/// naming it as an element of a deconstruction writes it; any other use reads it, also a write through a <c>ref</c>
/// local, a span or a pointer. An assignment whose target is not the local itself (<c>span[i] = v</c>, <c>*p = v</c>,
/// compound, deconstruction, increment) evaluates its value first and its target last, as the lowering splits it (a
/// split in the value comes before the use of the target).
/// </para>
/// <para>
/// The liveness is the classic backward fixed point over the nodes of the graph, one bit per local. A carrier (a
/// delegate local) that is live after a split makes the locals its delegates capture live there too; a pinned local is
/// live after every split reachable from its pin (a forward fixed point).
/// </para>
/// <para>
/// The blocks of a <c>finally</c> region have no successor edges in the Roslyn graph. That is harmless today: a barrier
/// in the scope of a <c>using</c> is GSH0003 and <c>try</c> is GSH0038, so no split point lies in such a region.
/// </para>
/// <para>
/// GSH0072 (an iterator local) follows the same liveness: compared with the text order it reports a local used again
/// on the back edge of a loop, and no longer reports one whose later use is on a branch that the barrier does not reach.
/// </para>
/// </remarks>
internal static class CrossBarrierLiveness
{
    /// <summary>
    /// For each local of <paramref name="tracked"/>, whether it is live right after a split point of
    /// <paramref name="graph"/>, whose events <paramref name="recorder"/> recorded.
    /// </summary>
    public static bool[] FindLiveAcrossSplit(SplicedFlowGraph graph, LivenessRecorder recorder,
        IReadOnlyList<ILocalSymbol> tracked)
    {
        var crossing = FindCrossing(graph, recorder);
        AddCarriedCaptures(recorder, crossing);
        if (recorder.HasPins)
        {
            AddPinnedAcrossSplit(graph, recorder, crossing);
        }

        var result = new bool[tracked.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = recorder.IndexOf(tracked[i]) is var index and >= 0 && IsSet(crossing, index);
        }

        return result;
    }

    // The backward fixed point of the live locals, then the locals live after each split
    private static ulong[] FindCrossing(SplicedFlowGraph graph, LivenessRecorder recorder)
    {
        var words = (recorder.LocalCount + 63) / 64;
        var count = graph.Nodes.Length;
        var liveIn = new ulong[count][];
        for (var i = 0; i < liveIn.Length; i++)
        {
            liveIn[i] = new ulong[words];
        }

        var live = new ulong[words];
        bool changed;
        do
        {
            changed = false;
            for (var i = count - 1; i >= 0; i--)
            {
                LiveOut(graph.Nodes[i], liveIn, live);
                ApplyBackward(recorder.EventsOf(i), live, crossing: null);
                if (!live.AsSpan().SequenceEqual(liveIn[i]))
                {
                    live.AsSpan().CopyTo(liveIn[i]);
                    changed = true;
                }
            }
        } while (changed);

        var crossing = new ulong[words];
        for (var i = 0; i < count; i++)
        {
            LiveOut(graph.Nodes[i], liveIn, live);
            ApplyBackward(recorder.EventsOf(i), live, crossing);
        }

        return crossing;
    }

    // A carrier live after a split keeps the captures of its delegates live there
    private static void AddCarriedCaptures(LivenessRecorder recorder, ulong[] crossing)
    {
        foreach (var pair in recorder.CarrierCaptures)
        {
            if (!IsSet(crossing, pair.Key))
            {
                continue;
            }

            foreach (var captured in pair.Value)
            {
                crossing[captured / 64] |= 1UL << (captured % 64);
            }
        }
    }

    // The forward fixed point of the pinned locals: a local pinned on some path to a split is added to crossing
    private static void AddPinnedAcrossSplit(SplicedFlowGraph graph, LivenessRecorder recorder, ulong[] crossing)
    {
        var pinnedIn = new ulong[graph.Nodes.Length][];
        for (var i = 0; i < pinnedIn.Length; i++)
        {
            pinnedIn[i] = new ulong[crossing.Length];
        }

        var pinned = new ulong[crossing.Length];
        bool changed;
        do
        {
            changed = false;
            foreach (var node in graph.Nodes)
            {
                pinnedIn[node.Index].AsSpan().CopyTo(pinned);
                ApplyForward(recorder.EventsOf(node.Index), pinned, crossing);
                foreach (var successor in node.Successors)
                {
                    changed |= Or(pinnedIn[successor], pinned);
                }
            }
        } while (changed);
    }

    // Runs events forward from the locals pinned at the start of their node; adds to crossing those pinned at a split
    private static void ApplyForward(ImmutableArray<LivenessEvent>.Builder events, ulong[] pinned, ulong[] crossing)
    {
        foreach (var flowEvent in events)
        {
            switch (flowEvent.Kind)
            {
                case LivenessEventKind.Pin:
                    pinned[flowEvent.Local / 64] |= 1UL << (flowEvent.Local % 64);
                    break;
                case LivenessEventKind.Split:
                    Or(crossing, pinned);
                    break;
            }
        }
    }

    // target |= bits; whether target changed
    private static bool Or(ulong[] target, ulong[] bits)
    {
        var changed = false;
        for (var word = 0; word < target.Length; word++)
        {
            var merged = target[word] | bits[word];
            changed |= merged != target[word];
            target[word] = merged;
        }

        return changed;
    }

    // The union of the live-in sets of the successors of node, into live
    private static void LiveOut(SplicedFlowNode node, ulong[][] liveIn, ulong[] live)
    {
        Array.Clear(live, 0, live.Length);
        foreach (var successor in node.Successors)
        {
            Or(live, liveIn[successor]);
        }
    }

    // Runs events backward from the locals live at the end of their node; adds to crossing the locals live after a split
    private static void ApplyBackward(ImmutableArray<LivenessEvent>.Builder events, ulong[] live, ulong[]? crossing)
    {
        for (var i = events.Count - 1; i >= 0; i--)
        {
            var flowEvent = events[i];
            switch (flowEvent.Kind)
            {
                case LivenessEventKind.Read:
                    live[flowEvent.Local / 64] |= 1UL << (flowEvent.Local % 64);
                    break;
                case LivenessEventKind.Write:
                    live[flowEvent.Local / 64] &= ~(1UL << (flowEvent.Local % 64));
                    break;
                case LivenessEventKind.Split when crossing != null:
                    Or(crossing, live);
                    break;
            }
        }
    }

    private static bool IsSet(ulong[] bits, int index) => (bits[index / 64] & (1UL << (index % 64))) != 0;
}

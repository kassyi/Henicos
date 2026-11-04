using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Tracks CFG flow capture targets and their captured reads across conditional branches.
/// </summary>
internal sealed class FlowCaptureTable
{
    private readonly Dictionary<CaptureId, CapturedFlowTarget> _flowCaptures = new();

    /// <summary>
    /// Registers or updates a flow capture operation for the capture ID, accumulating reads and parameter targets.
    /// </summary>
    public void Register(
        IFlowCaptureOperation capture,
        Func<ISymbol, int> indexResolver,
        Func<ISymbol, bool> isDeclaredInFrame)
    {
        var trackedParam = ParameterChanges.RootParameter(capture.Value, out var isWhole, out var isRefField, out var property);
        var parameter = trackedParam is { } p && LivenessRecorder.IsTrackedParameter(p) ? p : null;

        if (_flowCaptures.TryGetValue(capture.Id, out var flowCapture))
        {
            MergeBranch(flowCapture, capture.Value, parameter, isRefField, property, indexResolver, isDeclaredInFrame);
        }
        else
        {
            RegisterFirst(capture, parameter, isWhole, isRefField, property, indexResolver, isDeclaredInFrame);
        }
    }

    /// <summary>Registers the first occurrence of a flow capture for a capture ID.</summary>
    private void RegisterFirst(
        IFlowCaptureOperation capture,
        IParameterSymbol? parameter,
        bool isWhole,
        bool isRefField,
        IPropertySymbol? property,
        Func<ISymbol, int> indexResolver,
        Func<ISymbol, bool> isDeclaredInFrame)
    {
        var wholeSymbol = capture.Value switch
        {
            ILocalReferenceOperation { Local: var local } when !isDeclaredInFrame(local) => (ISymbol)local,
            IParameterReferenceOperation { Parameter: var param } when LivenessRecorder.IsTrackedParameter(param) => param,
            _ => null
        };
        var flowCapture = new CapturedFlowTarget(wholeSymbol);
        if (parameter is not null)
        {
            flowCapture.ParameterTargets.Add(new CapturedParameterTarget(indexResolver(parameter), isWhole, isRefField, property));
        }

        CollectCapturedReads(capture.Value, flowCapture.Reads, isDeclaredInFrame);
        _flowCaptures.Add(capture.Id, flowCapture);
    }

    /// <summary>Merges an additional branch into an existing flow capture target.</summary>
    private static void MergeBranch(
        CapturedFlowTarget flowCapture,
        IOperation capturedValue,
        IParameterSymbol? parameter,
        bool isRefField,
        IPropertySymbol? property,
        Func<ISymbol, int> indexResolver,
        Func<ISymbol, bool> isDeclaredInFrame)
    {
        // Multi-branch capture: demote whole variable status and accumulate reads
        flowCapture.WholeSymbol = null;
        for (var i = 0; i < flowCapture.ParameterTargets.Count; i++)
        {
            flowCapture.ParameterTargets[i].IsWhole = false;
        }

        if (parameter is not null)
        {
            flowCapture.ParameterTargets.Add(new CapturedParameterTarget(indexResolver(parameter), isWhole: false, isRefField, property));
        }

        CollectCapturedReads(capturedValue, flowCapture.Reads, isDeclaredInFrame);
    }

    /// <summary>
    /// Gets the captured symbols read by a flow capture reference, unless it is a whole-variable overwrite.
    /// </summary>
    public bool TryGetReads(IFlowCaptureReferenceOperation reference, out HashSet<ISymbol>? reads)
    {
        if (_flowCaptures.TryGetValue(reference.Id, out var flowCapture) &&
            !IsWholeVariableOverwrite(reference, flowCapture.WholeSymbol))
        {
            reads = flowCapture.Reads;
            return true;
        }

        reads = null;
        return false;
    }

    /// <summary>
    /// Collects the tracked parameters changed by the use of a flow capture reference.
    /// </summary>
    public void CollectParameterChanges(
        IFlowCaptureReferenceOperation reference,
        Action<int, SyntaxNode> reportChange)
    {
        if (!_flowCaptures.TryGetValue(reference.Id, out var flowCapture))
        {
            return;
        }

        foreach (var captured in flowCapture.ParameterTargets)
        {
            if (captured.Property is { } property
                ? ParameterChanges.MayChangeThroughCapturedProperty(reference, property)
                : captured.IsRefField
                    ? ParameterChanges.MayChangeCapturedRefField(reference)
                    : ParameterChanges.MayChange(reference))
            {
                reportChange(captured.Parameter, reference.Syntax);
            }
        }
    }

    /// <summary>Gets the local that reference names as a whole through a flow capture.</summary>
    public ILocalSymbol? TryGetWholeLocal(IOperation reference) =>
        reference is IFlowCaptureReferenceOperation capture &&
        _flowCaptures.TryGetValue(capture.Id, out var flowCapture)
            ? flowCapture.WholeSymbol as ILocalSymbol
            : null;

    /// <summary>Gets the tracked parameter index that reference names as a whole through a flow capture.</summary>
    public int IndexOfWholeParameter(IOperation reference, Func<ISymbol, int> indexResolver)
    {
        if (reference is IParameterReferenceOperation { Parameter: var parameter } &&
            LivenessRecorder.IsTrackedParameter(parameter))
        {
            return indexResolver(parameter);
        }

        if (reference is IFlowCaptureReferenceOperation capture &&
            _flowCaptures.TryGetValue(capture.Id, out var flowCapture) &&
            flowCapture.ParameterTargets.Count == 1 &&
            flowCapture.ParameterTargets[0].IsWhole)
        {
            return flowCapture.ParameterTargets[0].Parameter;
        }

        return -1;
    }

    /// <summary>Whether reference overwrites wholeSymbol as a whole in a simple assignment or deconstruction.</summary>
    private static bool IsWholeVariableOverwrite(IFlowCaptureReferenceOperation reference, ISymbol? wholeSymbol) =>
        wholeSymbol switch
        {
            ILocalSymbol local =>
                (reference.Parent is ISimpleAssignmentOperation assignment &&
                 assignment.Target == reference &&
                 (assignment.IsRef || !local.IsRef)) ||
                (!local.IsRef &&
                 reference.Parent is ITupleOperation tuple &&
                 ParameterChanges.IsDeconstructionTarget(tuple)),
            IParameterSymbol =>
                (reference.Parent is ISimpleAssignmentOperation assignment && assignment.Target == reference) ||
                (reference.Parent is ITupleOperation tuple && ParameterChanges.IsDeconstructionTarget(tuple)),
            _ => false
        };

    /// <summary>Collects the locals and tracked parameters read inside a captured expression.</summary>
    private static void CollectCapturedReads(
        IOperation operation,
        HashSet<ISymbol> reads,
        Func<ISymbol, bool> isDeclaredInFrame)
    {
        switch (operation)
        {
            case IFlowAnonymousFunctionOperation:
                return;
            case ILocalReferenceOperation { Local: var local } when !isDeclaredInFrame(local):
                reads.Add(local);
                return;
            case IParameterReferenceOperation { Parameter: var parameter } when LivenessRecorder.IsTrackedParameter(parameter):
                reads.Add(parameter);
                return;
        }

        foreach (var child in operation.ChildOperations)
        {
            CollectCapturedReads(child, reads, isDeclaredInFrame);
        }
    }

    /// <summary>A captured flow target with accumulated reads and parameter targets.</summary>
    private sealed class CapturedFlowTarget(ISymbol? wholeSymbol)
    {
        public ISymbol? WholeSymbol { get; set; } = wholeSymbol;

        public HashSet<ISymbol> Reads { get; } = new(SymbolEqualityComparer.Default);

        public ImmutableArray<CapturedParameterTarget>.Builder ParameterTargets { get; } = ImmutableArray.CreateBuilder<CapturedParameterTarget>();
    }

    /// <summary>A tracked parameter target reached through a flow capture.</summary>
    private sealed class CapturedParameterTarget(
        int parameter,
        bool isWhole,
        bool isRefField,
        IPropertySymbol? property)
    {
        public int Parameter { get; } = parameter;

        public bool IsWhole { get; set; } = isWhole;

        public bool IsRefField { get; } = isRefField;

        public IPropertySymbol? Property { get; } = property;
    }
}

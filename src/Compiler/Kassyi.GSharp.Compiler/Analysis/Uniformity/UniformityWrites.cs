using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The walk of <see cref="UniformityRecorder"/> over what an operation writes: it gives the locals and parameters a
/// value index before the graph is solved and marks them written at the node.
/// </summary>
internal static class UniformityWrites
{
    /// <summary>
    /// The locals and parameters that <paramref name="operation"/> writes, written at <paramref name="node"/>.
    /// </summary>
    public static void AllocateTargets(UniformityValues values, UniformityNode node, IOperation operation,
        ControlFlowGraph graph)
    {
        switch (operation)
        {
            case IDeconstructionAssignmentOperation deconstruction:
                AllocateTargets(values, node, deconstruction.Target, graph);
                break;
            case IAssignmentOperation assignment:
                AllocateTargets(values, node, assignment.Target, graph);
                break;
            case IIncrementOrDecrementOperation increment:
                AllocateTargets(values, node, increment.Target, graph);
                break;
            case IInvocationOperation invocation:
                AllocateArguments(values, node, invocation, graph);
                break;
            case IDeclarationExpressionOperation declaration:
                AllocateTargets(values, node, declaration.Expression, graph);
                break;
            case ITupleOperation tuple:
                AllocateElements(values, node, tuple, graph);
                break;
            case ILocalReferenceOperation or IParameterReferenceOperation or IInstanceReferenceOperation:
                AddNamedWrite(values, node, operation);
                break;
            case IFieldReferenceOperation or IPropertyReferenceOperation:
                AddMemberWrite(values, node, operation, graph);
                break;
        }
    }

    // A local, a parameter or the <c>this</c> of a struct member is written
    private static void AddNamedWrite(UniformityValues values, UniformityNode node, IOperation operation)
    {
        if (GetRootSymbol(operation) is { } symbol)
        {
            AddWritten(node, values.ValueOf(symbol));
        }
    }

    // A member of a struct is written (p.X, p.Inner.Y): the struct root is written, a flow capture with the locals a ref
    // conditional lets it refer to
    private static void AddMemberWrite(UniformityValues values, UniformityNode node, IOperation operation,
        ControlFlowGraph graph)
    {
        if (GetStructRoot(operation) is not { } root)
        {
            return;
        }

        if (root is IFlowCaptureReferenceOperation capture)
        {
            AddWritten(node, values.CaptureRootOf(capture, graph));
            foreach (var alias in values.GetRootAliases(capture))
            {
                AddWritten(node, values.ValueOf(alias));
            }
        }
        else
        {
            AddNamedWrite(values, node, root);
        }
    }

    /// <summary>The value <paramref name="value"/> written at <paramref name="node"/>, once.</summary>
    public static void AddWritten(UniformityNode node, int value)
    {
        if (!node.Written.Contains(value))
        {
            node.Written.Add(value);
        }
    }

    /// <summary>
    /// Whether <paramref name="invocation"/> calls a member of a struct that G# defines that may write its receiver
    /// (not <c>readonly</c>).
    /// </summary>
    public static bool WritesReceiver(IInvocationOperation invocation) =>
        invocation is
        {
            TargetMethod: { IsStatic: false, IsReadOnly: false, ContainingType: { IsReadOnly: false } type },
            Instance: not null
        } &&
        GlobalCppNames.IsEmittedUserType(type);

    private static void AllocateArguments(UniformityValues values, UniformityNode node, IInvocationOperation invocation,
        ControlFlowGraph graph)
    {
        if (WritesReceiver(invocation) && invocation.Instance is { } receiver)
        {
            AllocateTargets(values, node, receiver, graph);
        }

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out)
            {
                AllocateTargets(values, node, argument.Value, graph);
            }
        }
    }

    private static void AllocateElements(UniformityValues values, UniformityNode node, ITupleOperation tuple,
        ControlFlowGraph graph)
    {
        foreach (var element in tuple.Elements)
        {
            AllocateTargets(values, node, element, graph);
        }
    }

    /// <summary>
    /// The local, parameter, <c>this</c> (by its type) or flow capture of struct type whose member target writes (p.X,
    /// p.Inner.Y, X in a member of the struct, the capture that an object initializer or a with expression fills), or
    /// null for any other target.
    /// </summary>
    public static IOperation? GetStructRoot(IOperation target)
    {
        var current = target;
        while (true)
        {
            var instance = current switch
            {
                IFieldReferenceOperation { Instance: { } fieldOwner } => fieldOwner,
                IPropertyReferenceOperation { Instance: { } propertyOwner } => propertyOwner,
                _ => null
            };

            if (instance is not { Type: { IsValueType: true, IsRefLikeType: false } })
            {
                return null;
            }

            if (instance is ILocalReferenceOperation or IParameterReferenceOperation or IInstanceReferenceOperation or
                IFlowCaptureReferenceOperation)
            {
                return instance;
            }

            current = instance;
        }
    }

    /// <summary>
    /// The local, parameter or struct type of <c>this</c> that <paramref name="root"/> names, or null for a flow
    /// capture.
    /// </summary>
    public static ISymbol? GetRootSymbol(IOperation root) => root switch
    {
        ILocalReferenceOperation local => local.Local,
        IParameterReferenceOperation parameter => parameter.Parameter,
        IInstanceReferenceOperation { Type: { } self } => self,
        _ => null
    };
}

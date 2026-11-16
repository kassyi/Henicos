namespace Kassyi.GSharp.Compiler.Transpiler.Frames;

/// <summary>Decides the frame of a CPU state machine from its lowered form.</summary>
internal static class LoweredFrameBuilder
{
    /// <summary>
    /// The frame of the native state machine of <paramref name="plan"/>: a slot per hoisted local, split as
    /// <see cref="ResumeLocalEmitter.Separate"/> splits them, with the C++ name of each.
    /// </summary>
    public static StateMachineFrame BuildNative(
        LoweredStateMachine lowered,
        in MethodEmissionPlan plan,
        ITranspilerDialect dialect)
    {
        var semanticModel = plan.SemanticModel;
        var kernelName = plan.Declaration.Identifier.ValueText;
        var copiedParameters = plan.GetCopiedParameters(lowered);
        var warpSiteCount = lowered.WarpSiteCount;

        var callFrameCallees = ImmutableArray.CreateBuilder<IMethodSymbol>(lowered.CallSites.Count);
        foreach (var callSite in lowered.CallSites)
        {
            var callee = semanticModel.GetSymbol(callSite) as IMethodSymbol
                         ?? throw new InvalidOperationException(
                             "A call that may suspend does not resolve to a device function.");
            callFrameCallees.Add(callee);
        }

        var slots = ImmutableArray.CreateBuilder<FrameSlot>(lowered.ContextFields.Count);
        foreach (var field in lowered.ContextFields)
        {
            var type = HoistedFieldTypeResolver.Resolve(field, semanticModel, kernelName);
            var key = LoweringIdentity.ForDeclaration(field, semanticModel);
            var name = CppNameResolver.Resolve(field, semanticModel, dialect).ToString();
            var inFrame = ResumeLocalEmitter.StaysInFrame(field, type, key);

            FrameSlotKind kind;
            ITypeSymbol? elementType = null;
            var length = 0;
            Location? unsupportedLocation = null;

            if (inFrame && field.Parent is VariableDeclarationSyntax d && RefLocalLiveness.IsRefType(d.Type))
            {
                kind = FrameSlotKind.HeldRef;
            }
            else if (inFrame && type.IsSharedMemoryType())
            {
                kind = FrameSlotKind.SharedMemory;
            }
            else if (inFrame)
            {
                kind = FrameSlotKind.Field;
            }
            else
            {
                var local = ResumeLocals.Describe(field, type, semanticModel);
                switch (local.Kind)
                {
                    case ResumeLocalKind.Bound:
                        kind = FrameSlotKind.Redeclared;
                        break;
                    case ResumeLocalKind.StackAlloc:
                        kind = FrameSlotKind.RedeclaredStack;
                        elementType = local.ElementType;
                        length = local.Length;
                        break;
                    case ResumeLocalKind.Unsupported:
                        kind = FrameSlotKind.Unsupported;
                        unsupportedLocation = semanticModel.GetReportLocation(local.Unsupported ?? (SyntaxNode)field);
                        break;
                    case ResumeLocalKind.Initialized:
                        // Only a SharedMemory<T> is initialized again, and it stays in the native frame
                        throw new InvalidOperationException(
                            $"The native frame of '{kernelName}' declares the shared memory '{name}' again.");
                    default:
                        throw new InvalidOperationException($"Unknown resume local kind {local.Kind}.");
                }
            }

            slots.Add(new FrameSlot(kind, type, name, elementType, length, unsupportedLocation, Managed: null));
        }

        return new StateMachineFrame(copiedParameters, slots.MoveToImmutable(), callFrameCallees.MoveToImmutable(),
            warpSiteCount);
    }
}

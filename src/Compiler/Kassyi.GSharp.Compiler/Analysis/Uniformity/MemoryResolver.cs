using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using static Kassyi.GSharp.Compiler.Analysis.MemoryOperations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The walk of <see cref="MemoryUniformity"/> from a location or reference, through the locals that refer to memory,
/// to the memories it reaches.
/// </summary>
internal static class MemoryResolver
{
    // The memories a location reaches, through the locals that refer to memory; all of them for a ref local bound to a
    // location that is not followed
    public static ImmutableArray<int> Resolve(MemoryValues values, MemoryReferences references, IOperation location)
    {
        location = Unwrap(location);
        if (references.TryGetResolved(location, out var resolved))
        {
            return resolved;
        }

        var memories = new HashSet<int>();
        var all = false;
        CollectLocation(values, references, location, memories,
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default), ref all);
        resolved = all ? values.GetAllMemory() : ImmutableArray.CreateRange(memories);
        references.AddResolved(location, resolved);
        return resolved;
    }

    // The memories a reference to memory (an array, span or SharedMemory<T>) reaches
    public static ImmutableArray<int> ResolveReference(MemoryValues values, MemoryReferences references,
        IOperation reference)
    {
        reference = Unwrap(reference);
        if (references.TryGetResolved(reference, out var resolved))
        {
            return resolved;
        }

        var memories = new HashSet<int>();
        var all = false;
        CollectReference(values, references, reference, memories,
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default), ref all);
        resolved = all ? values.GetAllMemory() : ImmutableArray.CreateRange(memories);
        references.AddResolved(reference, resolved);
        return resolved;
    }

    private static void CollectLocation(MemoryValues values, MemoryReferences references, IOperation location,
        HashSet<int> memories, HashSet<ILocalSymbol> visited, ref bool all)
    {
        switch (Unwrap(location))
        {
            case IArrayElementReferenceOperation array:
                CollectReference(values, references, array.ArrayReference, memories, visited, ref all);
                break;
            case IPropertyReferenceOperation { Property.IsIndexer: true, Instance: { } instance }:
                CollectReference(values, references, instance, memories, visited, ref all);
                break;
            case ILocalReferenceOperation { Local: { IsRef: true } refLocal }:
                all |= references.HasUnknownTarget(refLocal);
                CollectSources(values, references, refLocal, memories, visited, isLocation: true, ref all);
                break;
            case IParameterReferenceOperation { Parameter: var parameter }
                when values.TryGetIndex(parameter, out var index):
                memories.Add(index);
                break;
            case IFlowCaptureReferenceOperation capture:
                CollectCaptureSources(values, references, capture, memories, visited, ref all);
                break;
            case IInvocationOperation invocation:
                foreach (var argument in invocation.Arguments)
                {
                    var value = Unwrap(argument.Value);
                    if (IsLocation(value))
                    {
                        CollectLocation(values, references, value, memories, visited, ref all);
                    }
                    else
                    {
                        CollectReference(values, references, value, memories, visited, ref all);
                    }
                }

                break;
        }
    }

    private static void CollectReference(MemoryValues values, MemoryReferences references, IOperation reference,
        HashSet<int> memories, HashSet<ILocalSymbol> visited, ref bool all)
    {
        if (Unwrap(reference) is IFlowCaptureReferenceOperation capture)
        {
            CollectCaptureSources(values, references, capture, memories, visited, ref all);
            return;
        }

        if (GetName(reference) is not { } name)
        {
            return;
        }

        if (values.TryGetIndex(name, out var index))
        {
            memories.Add(index);
        }

        if (name is ILocalSymbol local)
        {
            CollectSources(values, references, local, memories, visited, isLocation: false, ref all);
        }
    }

    // A flow capture refers to the references and locations captured into it (a capture of a capture is followed in
    // turn)
    private static void CollectCaptureSources(MemoryValues values, MemoryReferences references,
        IFlowCaptureReferenceOperation capture, HashSet<int> memories, HashSet<ILocalSymbol> visited, ref bool all)
    {
        if (!references.TryGetCaptureSources(capture.Id, out var sources) || !references.EnterCapture(capture.Id))
        {
            return;
        }

        foreach (var source in sources)
        {
            if (IsLocation(ContainingFieldLocation(source)))
            {
                CollectLocation(values, references, ContainingFieldLocation(source), memories, visited, ref all);
            }
            else
            {
                CollectReference(values, references, source, memories, visited, ref all);
            }
        }

        references.ExitCapture(capture.Id);
    }

    private static void CollectSources(MemoryValues values, MemoryReferences references, ILocalSymbol local,
        HashSet<int> memories, HashSet<ILocalSymbol> visited, bool isLocation, ref bool all)
    {
        if (!visited.Add(local) || !references.TryGetSources(local, out var sources))
        {
            return;
        }

        foreach (var source in sources)
        {
            if (isLocation)
            {
                CollectLocation(values, references, source, memories, visited, ref all);
            }
            else
            {
                CollectReference(values, references, source, memories, visited, ref all);
            }
        }
    }
}

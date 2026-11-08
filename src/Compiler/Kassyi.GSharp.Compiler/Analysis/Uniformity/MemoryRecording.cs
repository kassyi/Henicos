using Microsoft.CodeAnalysis.Operations;
using static Kassyi.GSharp.Compiler.Analysis.MemoryOperations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The walks of <see cref="MemoryUniformity"/> while the graph is recorded: what a local refers to, what a call may
/// write, and the value indices of the memories an operation names.
/// </summary>
internal static class MemoryRecording
{
    // ref int r = ref <location>: a location of memory is followed; a variable of the thread (a local, a by-value
    // parameter, a field of either) is not memory; anything else (a pointer) may be any memory
    public static void Bind(MemoryValues values, MemoryReferences references, ILocalSymbol refLocal, IOperation value)
    {
        value = Unwrap(value);
        if (IsLocation(value))
        {
            AllocateLocation(values, value);
            references.AddSource(refLocal, value);
        }
        else if (value is not (ILocalReferenceOperation or IParameterReferenceOperation or IFieldReferenceOperation))
        {
            references.AddUnknownTarget(refLocal);
        }
    }

    // <local> = <value> for a local of array, span or SharedMemory<T> type: a copy of a reference to memory is the same
    // memory; a SharedMemory<T> made here is a memory of its own; any other array (a stack array) belongs to the thread
    public static void Hold(MemoryValues values, MemoryReferences references, ILocalSymbol local, IOperation value)
    {
        if (GetName(value) is { } name)
        {
            AllocateName(values, name);
            references.AddSource(local, value);
        }
        else if (local.Type.IsSharedMemoryType())
        {
            values.MemoryOf(local);
        }
    }

    // What a call may write: its by-reference locations, the memory of its receiver and arguments, and the fields of a
    // struct argument or receiver, nested structs included
    public static ImmutableArray<MemoryCallTarget> CollectCallTargets(MemoryValues values,
        IInvocationOperation invocation)
    {
        var targets = ImmutableArray.CreateBuilder<MemoryCallTarget>();
        foreach (var argument in invocation.Arguments)
        {
            var value = Unwrap(argument.Value);
            if (argument.Parameter is { RefKind: RefKind.Ref or RefKind.Out } parameter && IsLocation(value))
            {
                AllocateLocation(values, value);
                targets.Add(new MemoryCallTarget(MemoryCallTargetKind.ByReference, value, Ordinal: parameter.Ordinal));
            }
        }

        if (invocation.Instance is { } instance)
        {
            AddReferenceTarget(values, instance, MemoryCallTargetKind.Receiver, ordinal: -1, targets);
        }

        foreach (var argument in invocation.Arguments)
        {
            AddReferenceTarget(values, argument.Value, MemoryCallTargetKind.Argument, argument.Parameter?.Ordinal ?? -1,
                targets);
        }

        return targets.ToImmutable();
    }

    // Gives the memories that a location names directly a value index, before the graph is solved; the locals it
    // reaches were given theirs where they were assigned
    public static void AllocateLocation(MemoryValues values, IOperation location)
    {
        switch (location)
        {
            case IArrayElementReferenceOperation array:
                AllocateReference(values, array.ArrayReference);
                break;
            case IPropertyReferenceOperation { Property.IsIndexer: true, Instance: { } instance }:
                AllocateReference(values, instance);
                break;
            case IParameterReferenceOperation { Parameter: var parameter }:
                values.MemoryOf(parameter);
                break;
            case IInvocationOperation invocation:
                foreach (var argument in invocation.Arguments)
                {
                    var value = Unwrap(argument.Value);
                    if (IsLocation(value))
                    {
                        AllocateLocation(values, value);
                    }
                    else
                    {
                        AllocateReference(values, value);
                    }
                }

                break;
        }
    }

    // A reference to memory passed to a call, or a struct whose fields reach memory
    private static void AddReferenceTarget(MemoryValues values, IOperation reference, MemoryCallTargetKind kind,
        int ordinal, ImmutableArray<MemoryCallTarget>.Builder targets)
    {
        if (GetName(reference) is { } name)
        {
            AllocateName(values, name);
            targets.Add(new MemoryCallTarget(kind, Unwrap(reference), Ordinal: ordinal));
        }
        else if (reference.Type is INamedTypeSymbol { IsValueType: true, IsRefLikeType: false } structType)
        {
            AddFieldTargets(values, structType, targets, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
        }
    }

    // Each field of a struct (and of the structs it holds) that reaches memory; an auto-property is named by the
    // property, as its readers name it
    private static void AddFieldTargets(MemoryValues values, INamedTypeSymbol type,
        ImmutableArray<MemoryCallTarget>.Builder targets, HashSet<ITypeSymbol> visited)
    {
        if (!visited.Add(type))
        {
            return;
        }

        foreach (var member in type.GetMembers())
        {
            if (member is not IFieldSymbol { IsStatic: false } field)
            {
                continue;
            }

            if (IsMemoryType(field.Type))
            {
                var name = field.AssociatedSymbol is IPropertySymbol property ? property : (ISymbol)field;
                targets.Add(new MemoryCallTarget(MemoryCallTargetKind.Field, null, values.MemoryOf(name)));
            }
            else if (field.Type is INamedTypeSymbol { IsValueType: true, IsRefLikeType: false } nested)
            {
                AddFieldTargets(values, nested, targets, visited);
            }
        }
    }

    private static void AllocateReference(MemoryValues values, IOperation reference)
    {
        if (GetName(reference) is { } name)
        {
            AllocateName(values, name);
        }
    }

    // Locals are memories only where Hold made them one
    private static void AllocateName(MemoryValues values, ISymbol name)
    {
        if (name is not ILocalSymbol)
        {
            values.MemoryOf(name);
        }
    }
}

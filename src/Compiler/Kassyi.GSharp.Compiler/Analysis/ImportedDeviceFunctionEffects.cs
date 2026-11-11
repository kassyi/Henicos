namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// What a call of a function without a body in the compilation may write, for <see cref="DeviceFunctionEffects"/>:
/// by the record of its assembly, or by its declaration.
/// </summary>
internal static class ImportedDeviceFunctionEffects
{
    // [WHY] A [GpuDeviceFunction] of another assembly is classified by what its assembly recorded (#618 would otherwise
    // take a ref parameter for the only write, and miss the memory it writes); without a record, as writing memory. A
    // constructor or a computed property of a struct of another assembly whose record brings its body runs that body,
    // which the record does not classify: it may write memory (#538 review m2)
    internal static DeviceFunctionEffect Classify(IMethodSymbol method, Compilation compilation)
    {
        if (DeviceExportTable.TryFindFunction(method, compilation, out var imported))
        {
            return imported.Record.Traits.Effect switch
            {
                "None" => DeviceFunctionEffect.None,
                "RefParameters" => DeviceFunctionEffect.RefParameters,
                _ => DeviceFunctionEffect.Memory
            };
        }

        return method.HasAttribute(KnownAttributes.GpuDeviceFunction) || HasImportedBody(method, compilation)
            ? DeviceFunctionEffect.Memory
            : ClassifyDeclaration(method);
    }

    // A constructor with a body, or the getter of a computed property, of an emitted struct of another assembly
    private static bool HasImportedBody(IMethodSymbol method, Compilation compilation) =>
        method.ContainingType is { TypeKind: TypeKind.Struct } type &&
        GlobalCppNames.IsEmittedUserType(type) &&
        !type.ContainingNamespace.IsCoreNamespace() &&
        DeviceExportTable.IsOutside(type, compilation) &&
        method switch
        {
            { MethodKind: MethodKind.Constructor } =>
                StructConstructorEmitter.IsEmittedStructConstructor(method, compilation),
            { MethodKind: MethodKind.PropertyGet, AssociatedSymbol: IPropertySymbol property } =>
                StructAbiMembers.HasAccessorFunctions(property),
            _ => false
        };

    private static DeviceFunctionEffect ClassifyDeclaration(IMethodSymbol method)
    {
        if (DeviceIntrinsicEffects.IsMemoryIntrinsic(method))
        {
            return DeviceFunctionEffect.Memory;
        }

        if (method.Parameters
            .AsStackEnumerable()
            .Any(static p => p.RefKind is RefKind.Ref or RefKind.Out))
        {
            return DeviceFunctionEffect.RefParameters;
        }

        return DeviceIntrinsicEffects.IsPureIntrinsic(method) ? DeviceFunctionEffect.None : DeviceFunctionEffect.Memory;
    }
}

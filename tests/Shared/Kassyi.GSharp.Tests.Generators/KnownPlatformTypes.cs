#nullable enable
using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Tests.Generators;

/// <summary>
/// The types HNT0011 recognizes, resolved once per compilation; a missing type turns its check off.
/// </summary>
internal sealed class KnownPlatformTypes(Compilation compilation)
{
    public INamedTypeSymbol? Skip { get; } = compilation.GetTypeByMetadataName("Xunit.Skip");

    public INamedTypeSymbol? SkipException { get; } = compilation.GetTypeByMetadataName("Xunit.SkipException");

    public INamedTypeSymbol? OperatingSystem { get; } = compilation.GetTypeByMetadataName("System.OperatingSystem");

    public INamedTypeSymbol? PlatformId { get; } = compilation.GetTypeByMetadataName("System.PlatformID");

    public object? UnixPlatformIdValue { get; } =
        ResolveEnumValue(compilation.GetTypeByMetadataName("System.PlatformID"), "Unix");

    public INamedTypeSymbol? RuntimeInformation { get; } =
        compilation.GetTypeByMetadataName("System.Runtime.InteropServices.RuntimeInformation");

    public INamedTypeSymbol? OSPlatform { get; } =
        compilation.GetTypeByMetadataName("System.Runtime.InteropServices.OSPlatform");

    public INamedTypeSymbol? SupportedOSPlatformAttribute { get; } =
        compilation.GetTypeByMetadataName("System.Runtime.Versioning.SupportedOSPlatformAttribute");

    public INamedTypeSymbol? PlatformAttribute { get; } =
        compilation.GetTypeByMetadataName("Kassyi.GSharp.Tests.Infrastructure.PlatformAttribute");

    public INamedTypeSymbol? TestPlatform { get; } =
        compilation.GetTypeByMetadataName("Kassyi.GSharp.Tests.Infrastructure.TestPlatform");

    public object? WindowsEnumValue { get; } = ResolveEnumValue(
        compilation.GetTypeByMetadataName("Kassyi.GSharp.Tests.Infrastructure.TestPlatform"), "Windows");

    private static object? ResolveEnumValue(INamedTypeSymbol? enumType, string name)
    {
        if (enumType is null)
        {
            return null;
        }

        foreach (var member in enumType.GetMembers(name))
        {
            if (member is IFieldSymbol { HasConstantValue: true } field)
            {
                return field.ConstantValue;
            }
        }

        return null;
    }
}

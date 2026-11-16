#nullable enable
using Kassyi.GSharp.Compiler.NativeApi;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class GpuOnlyMembersTests
{
    private static readonly Lazy<CSharpCompilation> s_coreSources = new(CreateCoreSourceCompilation);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CoreSources_CompileWithoutErrors()
    {
        var errors = s_coreSources.Value.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString());

        Assert.Empty(errors);
    }

    // Every public member of Core is GPU-only exactly when its body throws GpuOnlyException outside a managed launch,
    // directly or through the Core members it calls: a new intrinsic with a host implementation, or a new GPU-only
    // member the helper does not know, shows up here
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CoreSources_EveryPublicMember_IsGpuOnlyExactlyWhenItThrowsGpuOnlyException()
    {
        var compilation = s_coreSources.Value;
        var throwing = new GpuOnlyThrowFinder(compilation);

#pragma warning disable RS0030 // Category 4: the failure message names each mismatched member for a person to read
        var mismatches = GetPublicMembers(compilation)
            .Where(member => GpuOnlyMembers.IsGpuOnly(member) != throwing.Throws(member))
            .Select(member => $"{member.ToDisplayString()}: GPU-only {GpuOnlyMembers.IsGpuOnly(member)}, " +
                              $"throws {throwing.Throws(member)}")
            .ToArray();
#pragma warning restore RS0030

        Assert.Empty(mismatches);
    }

    // Witnesses that the walk of the previous test reaches every kind of GPU-only member and every way of throwing
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Kassyi.GSharp.Core.ThreadIndex", "get_X", true)] // [CudaIntrinsicPattern] type, guarded throw
    [InlineData("Kassyi.GSharp.Core.Dim3", "get_Y", true)] // always throws
    [InlineData("Kassyi.GSharp.Core.Gpu", "SyncThreads", true)] // [CudaIntrinsic] in the Core sources
    [InlineData("Kassyi.GSharp.Core.Gpu", "AtomicAdd", true)] // generated stub, throws in ManagedAtomics
    [InlineData("Kassyi.GSharp.Core.Gpu", "LaneId", true)] // generated stub, throws in ManagedGpuContext
    [InlineData("Kassyi.GSharp.Core.Warp", "ActiveMask", true)]
    [InlineData("Kassyi.GSharp.Core.Warp", "ShuffleDownSync", true)]
    [InlineData("Kassyi.GSharp.Core.Wmma", "Fill", true)]
    [InlineData("Kassyi.GSharp.Core.Gpu", "GridStrideLoop", true)] // Gpu macro
    [InlineData("Kassyi.GSharp.Core.Gpu", "NotNull", false)] // Gpu macro that works on the host
    [InlineData("Kassyi.GSharp.Core.Gpu+Hint", "Unroll", false)]
    [InlineData("Kassyi.GSharp.Core.SharedMemory`1", "get_Item", true)]
    [InlineData("Kassyi.GSharp.Core.SharedMemory`1", ".ctor", false)] // an empty buffer on the host
    [InlineData("Kassyi.GSharp.Core.SharedMemory`1", "FindDynamic", false)]
    public void CoreMember_ByKind_IsGpuOnlyAsItThrows(string typeName, string memberName, bool expected)
    {
        var compilation = s_coreSources.Value;
        var type = compilation.GetTypeByMetadataName(typeName);
        Assert.NotNull(type);
        var methods = type.GetMembers(memberName).OfType<IMethodSymbol>().ToArray();
        Assert.NotEmpty(methods);
        var throwing = new GpuOnlyThrowFinder(compilation);

        Assert.All(methods, method =>
        {
            Assert.Equal(expected, GpuOnlyMembers.IsGpuOnly(method));
            Assert.Equal(expected, throwing.Throws(method));
        });
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IsGpuOnly_UserTypesWithTheNamesOfCoreTypes_AreNotGpuOnly()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Analyzers/HostIntrinsic/LookalikeCoreTypes.cs")
        );

        Assert.All(
            ["Lookalike.ThreadIndex", "Lookalike.Dim3", "Lookalike.Gpu", "Lookalike.SharedMemory`1"],
            name =>
            {
                var type = compilation.GetTypeByMetadataName(name);
                Assert.NotNull(type);
                Assert.All(type.GetMembers(), static member => Assert.False(GpuOnlyMembers.IsGpuOnly(member)));
            }
        );
    }

    private static CSharpCompilation CreateCoreSourceCompilation()
    {
        var coreDirectory = RepositoryPaths.Combine("src", "Core", "Kassyi.GSharp.Core");
        var sources = Directory.EnumerateFiles(coreDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(Path.GetRelativePath(coreDirectory, path)))
            .Append(RepositoryPaths.Combine("src", "Shared", "Hashing", "Fnv1A64.cs"))
            .Order(StringComparer.Ordinal)
            .Select(path => (Path: path, Source: File.ReadAllText(path)));

        // The stubs that the build of Core generates from the native API YAML (YamlApiGenerator), and the global usings
        // of ImplicitUsings
        var stubs = NativeIntrinsicEmitter.MergeByClass([.. ReadNativeApiDefinitions()])
            .Select(definition => (
                Path: definition.ClassName + "Intrinsics.g.cs",
                Source: NativeIntrinsicEmitter.EmitIntrinsics(definition)
            ));
        var implicitUsings = (
            Path: "ImplicitUsings.g.cs",
            Source: "global using System; global using System.Collections.Generic; global using System.IO; " +
            "global using System.Linq; global using System.Net.Http; global using System.Threading; " +
            "global using System.Threading.Tasks;"
        );

        // The Core assembly stays referenced for the types its build generates otherwise (the vector types); the
        // source declarations take precedence over it
        var compilation = RoslynTestUtility.CreateCompilationAt(sources.Concat(stubs).Append(implicitUsings).ToArray());
        return compilation
            .WithAssemblyName("Kassyi.GSharp.Core.Sources")
            .WithOptions(compilation.Options.WithNullableContextOptions(NullableContextOptions.Enable));
    }

    private static bool IsBuildOutput(string relativePath) =>
        relativePath.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
        relativePath.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static IEnumerable<NativeApiDefinition> ReadNativeApiDefinitions() =>
        Directory.EnumerateFiles(RepositoryPaths.Combine("src", "NativeApi"), "*.yml", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(static path =>
            {
                Assert.True(NativeApiYamlReader.TryRead(File.ReadAllText(path), out var definition, out var error),
                    $"{path}: {error?.Message}");
                return definition;
            });

    // The public and protected methods and accessors of the public types declared in the Core sources, without the
    // members the compiler declares (a default constructor) and without GpuOnlyException, which defines the throw
    private static IEnumerable<IMethodSymbol> GetPublicMembers(Compilation compilation) =>
        GetTypes(compilation.Assembly.GlobalNamespace)
            .Where(static type => IsVisible(type) && type.Name != "GpuOnlyException" &&
                                  type.DeclaringSyntaxReferences.Length > 0)
            .SelectMany(static type => type.GetMembers().OfType<IMethodSymbol>())
            .Where(static method => !method.IsImplicitlyDeclared && method.DeclaredAccessibility is
                Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal);

    private static IEnumerable<INamedTypeSymbol> GetTypes(INamespaceOrTypeSymbol container) =>
        container.GetMembers()
            .OfType<INamespaceOrTypeSymbol>()
            .SelectMany(static member => member is INamedTypeSymbol type
                ? GetTypes(type).Prepend(type)
                : GetTypes(member));

    private static bool IsVisible(INamedTypeSymbol type) =>
        type.DeclaredAccessibility == Accessibility.Public &&
        (type.ContainingType is null || IsVisible(type.ContainingType));

    /// <summary>
    /// Finds the methods of the Core sources whose body throws <c>GpuOnlyException</c>, directly (a call of
    /// <c>GpuOnlyException.Throw</c>, a <c>throw new GpuOnlyException()</c>) or through a method, accessor or
    /// constructor of the Core sources it calls. Every throw in Core is guarded by "outside a managed launch" or
    /// unconditional, so reaching one is the property itself.
    /// </summary>
    private sealed class GpuOnlyThrowFinder(Compilation compilation)
    {
        private readonly Dictionary<IMethodSymbol, bool> _results = new(SymbolEqualityComparer.Default);

        public bool Throws(IMethodSymbol method)
        {
            method = method.OriginalDefinition;
            if (_results.TryGetValue(method, out var known))
            {
                return known;
            }

            // A cycle counts as not throwing until its first member is decided
            _results[method] = false;
            var result = method.DeclaringSyntaxReferences
                .Select(static reference => reference.GetSyntax())
                .Any(BodyThrows);
            _results[method] = result;
            return result;
        }

        private bool BodyThrows(SyntaxNode declaration)
        {
            // A method, accessor or constructor declaration, or the arrow clause of an expression-bodied property
            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            return model.GetOperation(declaration) is { } operation &&
                   operation.DescendantsAndSelf().Any(ThrowsOrCallsThrowing);
        }

        private bool ThrowsOrCallsThrowing(IOperation operation) => operation switch
        {
            IThrowOperation { Exception: IConversionOperation { Operand: var exception } } =>
                IsGpuOnlyException(exception.Type),
            IThrowOperation { Exception: { } exception } => IsGpuOnlyException(exception.Type),
            IInvocationOperation invocation => IsGpuOnlyException(invocation.TargetMethod.ContainingType) ||
                                               CallsThrowing(invocation.TargetMethod),
            IObjectCreationOperation { Constructor: { } constructor } => CallsThrowing(constructor),
            IPropertyReferenceOperation property => CallsThrowing(IsAssigned(property)
                ? property.Property.SetMethod
                : property.Property.GetMethod),
            _ => false
        };

        private bool CallsThrowing(IMethodSymbol? method) =>
            method is not null &&
            SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly) &&
            Throws(method);

        private static bool IsAssigned(IOperation operation) =>
            operation.Parent is IAssignmentOperation assignment && assignment.Target == operation;

        private static bool IsGpuOnlyException(ITypeSymbol? type) => type?.Name == "GpuOnlyException";
    }
}

using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Kassyi.GSharp.Compiler.Layout;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Layout;

/// <summary>
/// <c>StructAbiMembers.Get</c> lists every field that has storage (#621): the number of members, computed properties
/// left out, is the number of instance fields that the C# compiler emits for the struct, the hidden ones
/// (auto-property backing fields, captured primary constructor parameters, field-like events) included.
/// </summary>
/// <remarks>
/// The reference is what the compiler emits, not <c>GetMembers()</c>: Roslyn does not list the field of a field-like
/// event there.
/// </remarks>
[Category(TestCategory.Compiler)]
public class StructAbiMembersTests
{
    private static readonly string s_testCasesDirectory =
        RepositoryPaths.Combine("tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "TestCases");

    // Every test case on its own, against the fields in the metadata of its compilation (method bodies are not
    // compiled, so a body that refers to generated launchers does not matter). A test case whose declarations need
    // another file (the cross-assembly consumers, for example) has no image and is left out.
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Get_TestCaseStructs_ListEveryEmittedInstanceField()
    {
        var checkedFiles = new List<string>();
        var checkedStructs = 0;
        foreach (var relativePath in Directory.GetFiles(s_testCasesDirectory, "*.cs", SearchOption.AllDirectories)
                     .Select(file => Path.GetRelativePath(s_testCasesDirectory, file).Replace(Path.DirectorySeparatorChar, '/'))
                     .Order(StringComparer.Ordinal))
        {
            var (compilation, tree, model) = RoslynTestUtility.GetTestCaseContext(relativePath);
            if (!TryEmitInstanceFieldCounts(compilation, out var emittedFieldCounts))
            {
                continue;
            }

            var structs = tree.GetRoot()
                .DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .Select(declaration => model.GetDeclaredSymbol(declaration))
                .OfType<INamedTypeSymbol>()
                .Where(static type => type.TypeKind == TypeKind.Struct);
            foreach (var structType in structs)
            {
                Assert.True(
                    emittedFieldCounts.TryGetValue(GetMetadataFullName(structType), out var fieldCount),
                    $"{relativePath}: {GetMetadataFullName(structType)} is not in the emitted metadata");
                AssertEveryFieldIsListed(structType, fieldCount);
                checkedStructs++;
            }

            checkedFiles.Add(relativePath);
        }

        Assert.Contains("Diagnostics/StructLayoutEventDiagnostics.cs", checkedFiles);
        Assert.Contains("Layout/HostLayoutCases.cs", checkedFiles);
        Assert.True(checkedStructs > 100, $"only {checkedStructs} structs in {checkedFiles.Count} test cases were checked");
    }

    // The structs compiled into the test assembly (the kernels' structs with fixed buffers, inline arrays,
    // auto-properties and captured primary constructor parameters), against the fields the runtime sees
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Get_SharedCompilationStructs_ListEveryEmittedInstanceField()
    {
        var structs = new List<INamedTypeSymbol>();
        CollectStructs(MegaCompilationCache.Compilation.Assembly.GlobalNamespace, structs);
        var assembly = typeof(StructAbiMembersTests).Assembly;

        var checkedCount = 0;
        foreach (var structType in structs)
        {
            if (assembly.GetType(GetMetadataFullName(structType)) is not { } runtimeType)
            {
                continue;
            }

            var fieldCount = runtimeType.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Length;
            AssertEveryFieldIsListed(structType, fieldCount);
            checkedCount++;
        }

        Assert.Contains(structs, static s => s.Name == "PcSized");
        Assert.True(checkedCount > 100, $"only {checkedCount} structs were found in the test assembly");
    }

    // The event's hidden field is listed under the event's name with the delegate type; the event with add and remove,
    // the static, the extern and the partial event store nothing
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Get_FieldLikeEvent_ListsTheDelegateField()
    {
        var members = GetMembers("WithEvent");

        Assert.Equal(["A", "Changed"], members.Select(static m => m.Name));
        Assert.Equal(StructAbiMemberKind.EventBackingField, members[1].Kind);
        Assert.Equal("System.Action", members[1].Type.ToDisplayString());
        Assert.IsAssignableFrom<IEventSymbol>(members[1].Symbol);

        Assert.Equal(["A"], GetMembers("WithAccessorEvent").Select(static m => m.Name));
        Assert.Equal(["A"], GetMembers("WithStaticEvent").Select(static m => m.Name));
        Assert.Equal(["A"], GetMembers("WithExternEvent").Select(static m => m.Name));
        Assert.Equal(["A"], GetMembers("WithPartialEvent").Select(static m => m.Name));
    }

    // A struct of another assembly: GetMembers() lists neither the hidden field nor accessors Roslyn generated, but the
    // accessors carry [CompilerGenerated]
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Get_FieldLikeEventFromMetadata_ListsTheDelegateField()
    {
        var library = DeviceLibraryBuilder.Build(
            "EventLib",
            null,
            RoslynTestUtility.ReadTestCase("Diagnostics/StructLayoutEventLibrary.cs"));
        var consumer = DeviceLibraryBuilder.CreateConsumer("class Consumer { }", library);

        var withEvent = consumer.GetTypeByMetadataName("StructLayoutEventLibrary.LibraryWithEvent");
        var withAccessors = consumer.GetTypeByMetadataName("StructLayoutEventLibrary.LibraryWithAccessorEvent");

        Assert.NotNull(withEvent);
        Assert.NotNull(withAccessors);
        Assert.True(withEvent.DeclaringSyntaxReferences.IsEmpty);
        Assert.Equal([("A", StructAbiMemberKind.Field), ("Changed", StructAbiMemberKind.EventBackingField)], ListMembers(withEvent));
        Assert.Equal([("A", StructAbiMemberKind.Field)], ListMembers(withAccessors));
    }

    private static List<(string, StructAbiMemberKind)> ListMembers(INamedTypeSymbol structType)
    {
        var members = new List<(string, StructAbiMemberKind)>();
        foreach (var member in StructAbiMembers.Get(structType))
        {
            members.Add((member.Name, member.Kind));
        }

        return members;
    }

    private static List<StructAbiMember> GetMembers(string structName)
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext("Diagnostics/StructLayoutEventDiagnostics.cs");
        var declaration = tree.GetRoot()
            .DescendantNodes()
            .OfType<StructDeclarationSyntax>()
            .First(s => s.Identifier.Text == structName);
        var structType = Assert.IsAssignableFrom<INamedTypeSymbol>(model.GetDeclaredSymbol(declaration));

        var members = new List<StructAbiMember>();
        foreach (var member in StructAbiMembers.Get(structType))
        {
            members.Add(member);
        }

        return members;
    }

    private static void AssertEveryFieldIsListed(INamedTypeSymbol structType, int fieldCount)
    {
        var members = new List<StructAbiMember>();
        foreach (var member in StructAbiMembers.Get(structType))
        {
            members.Add(member);
        }

        Assert.True(
            fieldCount == members.Count,
            $"{structType.ToDisplayString()}: {fieldCount} emitted instance fields, {members.Count} members " +
            $"({string.Join(", ", members.Select(static m => $"{m.Name}:{m.Kind}"))})");
        Assert.True(
            members.All(static m => m.Kind != StructAbiMemberKind.UnknownHiddenField),
            $"{structType.ToDisplayString()} has a compiler-generated field that StructAbiMembers does not know");
    }

    // The number of instance fields of each type in the metadata-only image of the compilation, by full metadata name
    private static bool TryEmitInstanceFieldCounts(Compilation compilation, out Dictionary<string, int> counts)
    {
        counts = new Dictionary<string, int>(StringComparer.Ordinal);
        using var stream = new MemoryStream();
        if (!compilation.Emit(stream, options: new EmitOptions(metadataOnly: true)).Success)
        {
            return false;
        }

        using var peReader = new PEReader(ImmutableArray.Create(stream.ToArray()));
        var reader = peReader.GetMetadataReader();
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            counts[GetMetadataFullName(reader, type)] = type.GetFields()
                .Count(field => (reader.GetFieldDefinition(field).Attributes & FieldAttributes.Static) == 0);
        }

        return true;
    }

    private static string GetMetadataFullName(MetadataReader reader, TypeDefinition type)
    {
        var name = reader.GetString(type.Name);
        var declaringType = type.GetDeclaringType();
        if (!declaringType.IsNil)
        {
            return GetMetadataFullName(reader, reader.GetTypeDefinition(declaringType)) + "+" + name;
        }

        var ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static string GetMetadataFullName(INamedTypeSymbol type)
    {
        if (type.ContainingType is { } containingType)
        {
            return GetMetadataFullName(containingType) + "+" + type.MetadataName;
        }

        return type.ContainingNamespace.IsGlobalNamespace
            ? type.MetadataName
            : type.ContainingNamespace.ToDisplayString() + "." + type.MetadataName;
    }

    private static void CollectStructs(INamespaceOrTypeSymbol container, List<INamedTypeSymbol> structs)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceSymbol ns)
            {
                CollectStructs(ns, structs);
            }
            else if (member is INamedTypeSymbol type)
            {
                if (type.TypeKind == TypeKind.Struct)
                {
                    structs.Add(type);
                }

                CollectStructs(type, structs);
            }
        }
    }
}

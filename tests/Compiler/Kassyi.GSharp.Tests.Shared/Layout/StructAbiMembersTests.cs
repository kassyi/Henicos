using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Kassyi.GSharp.Compiler.Extensions;
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
                     .Select(file => NormalizePath(Path.GetRelativePath(s_testCasesDirectory, file)))
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
        CollectStructs(RoslynTestUtility.SharedCompilation.Assembly.GlobalNamespace, structs);
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
        Assert.Equal(RoslynTestUtility.SharedCompilation.GetTypeByMetadataName("System.Action"), members[1].Type, SymbolEqualityComparer.Default);
        Assert.IsAssignableFrom<IEventSymbol>(members[1].Symbol);
        // Roslyn does not list the event's field, so the member has no storage field to hand over
        Assert.IsAssignableFrom<IFieldSymbol>(members[0].StorageField);
        Assert.Null(members[1].StorageField);

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

    // A property with a backing field and an accessor of its own that uses the field keyword is field-backed: it is
    // listed through its backing field like an auto-property, and its accessors are member functions. A partial
    // property always has bodies, so it is field-backed even when it behaves like an auto-property; the accessors then
    // read and write the same storage (the differential test compares it with an auto-property)
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Get_FieldKeywordProperties_ListsThemAsFieldBackedWithTheirBackingField()
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext("Layout/FieldKeywordPropertyCases.cs");
        var declaration = tree.GetRoot()
            .DescendantNodes()
            .OfType<StructDeclarationSyntax>()
            .First();
        var structType = Assert.IsAssignableFrom<INamedTypeSymbol>(model.GetDeclaredSymbol(declaration));

        AssertFieldKeywordLayout(structType);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Get_PositionalRecordProperties_ListsThemAsAutoPropertiesUnlessRedeclared()
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext("Layout/FieldKeywordPropertyCases.cs");
        var declaration = tree.GetRoot()
            .DescendantNodes()
            .OfType<RecordDeclarationSyntax>()
            .First();
        var structType = Assert.IsAssignableFrom<INamedTypeSymbol>(model.GetDeclaredSymbol(declaration));

        Assert.Equal(
            [("X", StructAbiMemberKind.AutoProperty), ("Y", StructAbiMemberKind.FieldBackedProperty)],
            ListMembers(structType));
    }

    // From metadata, the C# compiler marks only the accessors it implements [CompilerGenerated]
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Get_FieldKeywordPropertiesFromMetadata_ListsThemAsFieldBacked()
    {
        var library = DeviceLibraryBuilder.Build(
            "FieldKeywordLib",
            null,
            RoslynTestUtility.ReadTestCase("Layout/FieldKeywordPropertyCases.cs"));
        var consumer = DeviceLibraryBuilder.CreateConsumer("class Consumer { }", library);
        var structType = consumer.GetTypeByMetadataName(
            "Kassyi.GSharp.Tests.Shared.TestCases.Layout.FieldKeywordLayout"
        );

        Assert.NotNull(structType);
        Assert.True(structType.DeclaringSyntaxReferences.IsEmpty);
        AssertFieldKeywordLayout(structType);
    }

    private static void AssertFieldKeywordLayout(INamedTypeSymbol structType)
    {
        const StructAbiMemberKind FieldBacked = StructAbiMemberKind.FieldBackedProperty;
        Assert.Equal(
            [
                ("A", StructAbiMemberKind.Field),
                ("Auto", StructAbiMemberKind.AutoProperty),
                ("AutoInit", StructAbiMemberKind.AutoProperty),
                ("SetterOnly", FieldBacked),
                ("GetterOnly", FieldBacked),
                ("Both", FieldBacked),
                ("InitOnly", FieldBacked),
                ("ReadOnly", FieldBacked),
                ("Partial", FieldBacked),
            ],
            ListMembers(structType));

        foreach (var member in StructAbiMembers.Get(structType))
        {
            if (member.Kind != FieldBacked)
            {
                continue;
            }

            var property = Assert.IsAssignableFrom<IPropertySymbol>(member.Symbol);
            var storage = Assert.IsAssignableFrom<IFieldSymbol>(member.StorageField);
            // From metadata, Roslyn does not link the backing field to its property; it is found by its name
            Assert.True(StructAbiMembers.TryGetBackingFieldProperty(storage, out var owner), member.Name);
            Assert.True(SymbolEqualityComparer.Default.Equals(property, owner), member.Name);
            Assert.True(StructAbiMembers.IsFieldBackedProperty(property), member.Name);
            Assert.True(StructAbiMembers.HasAccessorFunctions(property), member.Name);
            Assert.False(StructAbiMembers.IsAutoProperty(property), member.Name);
        }

        var auto = structType.GetMembers("Auto").OfType<IPropertySymbol>().Single();
        Assert.True(StructAbiMembers.IsAutoProperty(auto));
        Assert.False(StructAbiMembers.HasAccessorFunctions(auto));
        Assert.False(StructAbiMembers.IsFieldBackedProperty(auto));

        var computed = structType.GetMembers("Computed").OfType<IPropertySymbol>().Single();
        Assert.True(StructAbiMembers.HasAccessorFunctions(computed));
        Assert.False(StructAbiMembers.IsFieldBackedProperty(computed));
        Assert.False(StructAbiMembers.IsAutoProperty(computed));
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
            $"{GetMetadataFullName(structType)}: {fieldCount} emitted instance fields, {members.Count} members " +
            $"({string.Join(", ", members.Select(static m => $"{m.Name}:{m.Kind}"))})");
        Assert.True(
            members.All(static m => m.Kind != StructAbiMemberKind.UnknownHiddenField),
            $"{GetMetadataFullName(structType)} has a compiler-generated field that StructAbiMembers does not know");
    }

    private static string NormalizePath(string relativePath)
    {
        if (Path.DirectorySeparatorChar == '/')
        {
            return relativePath;
        }

        return string.Create(relativePath.Length, relativePath, static (span, src) =>
        {
            for (int i = 0; i < src.Length; i++)
            {
                span[i] = src[i] == '\\' ? '/' : src[i];
            }
        });
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
            : type.ContainingNamespace.GetFullNamespaceName() + "." + type.MetadataName;
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

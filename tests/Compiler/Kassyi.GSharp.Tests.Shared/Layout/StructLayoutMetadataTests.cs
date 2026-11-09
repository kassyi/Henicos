#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Kassyi.GSharp.Compiler.Layout;
using Kassyi.GSharp.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Layout.TypeLayoutTestHelper;

namespace Kassyi.GSharp.Tests.Shared.Layout;

public class StructLayoutMetadataTests
{
    // A struct read from metadata has the layout of the same struct read from source, on both dialects. StructLayout
    // and FieldOffset are pseudo-attributes: from metadata, Roslyn returns neither, and only the ClassLayout and
    // FieldLayout tables carry them (#736)
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void StructLayout_FromMetadata_MatchesTheSameStructFromSource()
    {
        var (source, _, _) = GetContext();
        var (_, metadataNamespace) = LoadFromMetadata("Layout/HostLayoutCases.cs");
        var sourceNamespace = GetLayoutNamespace(source.Assembly.GlobalNamespace);

        var structs = sourceNamespace.GetTypeMembers()
            .Where(static t => t is { TypeKind: TypeKind.Struct, IsGenericType: false })
            .ToList();
        var mismatches = new List<string>();
        foreach (var sourceStruct in structs)
        {
            var metadataStruct = metadataNamespace.GetTypeMembers(sourceStruct.Name).Single();
            Assert.True(metadataStruct.DeclaringSyntaxReferences.IsEmpty);
            CompareSourceAndMetadataLayouts(sourceStruct, metadataStruct, mismatches);
        }

        Assert.Contains(structs, static s => s.Name == "ExplicitUnionStruct");
        Assert.Contains(structs, static s => s.Name == "SizedPacked");
        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
    }

    // A constructed generic struct, and a struct nested in a constructed generic type, have no metadata token of their
    // own, nor do their fields: the reader maps the type to the TypeDef row of its definition and a field to the
    // FieldDef row of its definition (#736). Each case also checks the source layout, so that a layout the two paths
    // get wrong the same way does not pass
    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Layout/HostLayoutCases.cs", "SizedGeneric", null,
        "Sequential pack=0 device=TypeLayout { Size = 16, Alignment = 8 }")]
    [InlineData("Layout/GenericLayoutCases.cs", "PackedGeneric", null, "host=True/9/1 [B@0, V@1]")]
    [InlineData("Layout/GenericLayoutCases.cs", "ExplicitGeneric", null, "host=True/16/8 [B@0, V@8]")]
    [InlineData("Layout/GenericLayoutCases.cs", "GenericOuter", "ExplicitInner", "host=True/24/8 [I@4, V@16]")]
    public void StructLayout_FromMetadata_ConstructedGenericMatchesTheSameStructFromSource(
        string testCase, string genericName, string nestedName, string expectedSourceLayout)
    {
        var (sourceCompilation, _, _) = RoslynTestUtility.GetTestCaseContext(testCase);
        var (consumer, metadataNamespace) = LoadFromMetadata(testCase);
        var sourceStruct = ConstructWithDouble(
            sourceCompilation,
            GetLayoutNamespace(sourceCompilation.Assembly.GlobalNamespace),
            genericName,
            nestedName
        );
        var metadataStruct = ConstructWithDouble(consumer, metadataNamespace, genericName, nestedName);

        Assert.True(metadataStruct.DeclaringSyntaxReferences.IsEmpty);
        Assert.False(SymbolEqualityComparer.Default.Equals(metadataStruct, metadataStruct.OriginalDefinition));
        Assert.Contains(
            expectedSourceLayout,
            DescribeLayout(TypeLayoutCalculator.GetStructLayout(sourceStruct, s_cudaDialect)));
        var mismatches = new List<string>();
        CompareSourceAndMetadataLayouts(sourceStruct, metadataStruct, mismatches);
        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));

        static INamedTypeSymbol ConstructWithDouble(
            Compilation compilation, INamespaceSymbol layoutNamespace, string genericName, string nestedName)
        {
            var constructed = layoutNamespace.GetTypeMembers(genericName, 1).Single()
                .Construct(compilation.GetSpecialType(SpecialType.System_Double));
            return nestedName is null ? constructed : constructed.GetTypeMembers(nestedName).Single();
        }
    }

    // Every member hands over the field that stores it, so the offset of an auto-property of an explicit struct is read
    // from its backing field. From source the member is the property; from metadata Roslyn does not link the backing
    // field to it, so the member is that field itself (#853 review)
    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void StructAbiMember_StorageField_IsTheFieldThatHoldsTheMember(bool fromMetadata)
    {
        var structType = fromMetadata
            ? LoadFromMetadata("Layout/HostLayoutCases.cs").LayoutNamespace
                .GetTypeMembers("ExplicitAutoPropertyStruct").Single()
            : GetStructSymbol("ExplicitAutoPropertyStruct");

        var members = new List<StructAbiMember>();
        foreach (var member in StructAbiMembers.Get(structType))
        {
            members.Add(member);
        }

        Assert.Equal(2, members.Count);
        foreach (var member in members)
        {
            var storage = Assert.IsAssignableFrom<IFieldSymbol>(member.StorageField);
            Assert.True(
                fromMetadata
                    ? SymbolEqualityComparer.Default.Equals(storage, member.Symbol)
                    : SymbolEqualityComparer.Default.Equals(storage.AssociatedSymbol, member.Symbol),
                $"{member.Name}: {storage.Name} does not hold {member.Symbol.Name}"
            );
        }

        Assert.Equal([0, 4], members.Select(static m => StructLayoutReader.GetFieldOffset(m.StorageField)));
    }

    // A ClassLayout row with a PackingSize above 255, or with a ClassSize above int.MaxValue
    // (System.Reflection.Metadata throws BadImageFormatException on reading it), is malformed. As Roslyn's
    // PEModule.GetTypeLayout does, the reader drops such a value, and falls back to the attributes on a row it
    // cannot read, instead of throwing out of the generator or feeding the value into the layout (#853 review)
    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    [InlineData((ushort)512, 0u)]
    [InlineData((ushort)0, uint.MaxValue)]
    public void StructLayout_FromMalformedClassLayoutRow_DropsAnOutOfRangePackAndSize(ushort packingSize, uint size)
    {
        var library = MetadataReference.CreateFromImage(BuildStructWithClassLayoutRow(packingSize, size));
        var consumer = DeviceLibraryBuilder.CreateConsumer("class Consumer { }", library);
        var structType = consumer.GetTypeByMetadataName("Malformed.BadLayout");

        Assert.NotNull(structType);
        StructLayoutReader.Read(structType, out var kind, out var pack, out var readSize);
        Assert.Equal((TypeLayoutKind.Sequential, 0, 0), (kind, pack, readSize));
    }

    // A library with one sequential struct, Malformed.BadLayout, whose ClassLayout row holds the given
    // values, which the C# compiler never emits
    private static ImmutableArray<byte> BuildStructWithClassLayoutRow(ushort packingSize, uint size)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Malformed.dll"),
            metadata.GetOrAddGuid(new Guid("5f1d8a52-3b0e-4c41-9b0b-0c3f6d2e7a11")),
            default,
            default
        );
        metadata.AddAssembly(
            metadata.GetOrAddString("Malformed"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            System.Reflection.AssemblyHashAlgorithm.None
        );
        var runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(4, 0, 0, 0),
            default,
            default,
            default,
            default
        );
        var valueType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("ValueType")
        );
        var firstField = MetadataTokens.FieldDefinitionHandle(1);
        var firstMethod = MetadataTokens.MethodDefinitionHandle(1);
        metadata.AddTypeDefinition(
            default, default, metadata.GetOrAddString("<Module>"), default, firstField, firstMethod);
        var type = metadata.AddTypeDefinition(
            System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Sealed |
            System.Reflection.TypeAttributes.SequentialLayout,
            metadata.GetOrAddString("Malformed"),
            metadata.GetOrAddString("BadLayout"),
            valueType,
            firstField,
            firstMethod
        );
        metadata.AddTypeLayout(type, packingSize, size);

        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder()
        );
        var image = new BlobBuilder();
        peBuilder.Serialize(image);
        return image.ToImmutableArray();
    }

    // The test case compiled into a library, and the Layout namespace of that library as a consumer sees it
    private static (Compilation Consumer, INamespaceSymbol LayoutNamespace) LoadFromMetadata(string testCase)
    {
        var library = DeviceLibraryBuilder.Build("HostLayoutLibrary", null, RoslynTestUtility.ReadTestCase(testCase));
        var consumer = DeviceLibraryBuilder.CreateConsumer("class Consumer { }", library);
        var layoutNamespace = GetLayoutNamespace(consumer.GetAssemblyOrModuleSymbol(library) is IAssemblySymbol assembly
            ? assembly.GlobalNamespace
            : throw new InvalidOperationException("The library is not referenced"));
        return (consumer, layoutNamespace);
    }

    private static INamespaceSymbol GetLayoutNamespace(INamespaceSymbol global) =>
        global.GetNamespaceMembers().Single(static n => n.Name == "Kassyi")
            .GetNamespaceMembers().Single(static n => n.Name == "GSharp")
            .GetNamespaceMembers().Single(static n => n.Name == "Tests")
            .GetNamespaceMembers().Single(static n => n.Name == "Shared")
            .GetNamespaceMembers().Single(static n => n.Name == "TestCases")
            .GetNamespaceMembers().Single(static n => n.Name == "Layout");

    private static void CompareSourceAndMetadataLayouts(
        INamedTypeSymbol sourceStruct, INamedTypeSymbol metadataStruct, List<string> mismatches)
    {
        foreach (var dialect in new[] { s_cudaDialect, s_cpuDialect })
        {
            var expected = DescribeLayout(TypeLayoutCalculator.GetStructLayout(sourceStruct, dialect));
            var actual = DescribeLayout(TypeLayoutCalculator.GetStructLayout(metadataStruct, dialect));
            if (expected != actual)
            {
                mismatches.Add(
                    $"{sourceStruct} ({dialect.GetType().Name}):\n  source   {expected}\n  metadata {actual}");
            }
        }
    }

    private static string DescribeLayout(StructLayoutInfo layout) =>
        $"{layout.Kind} pack={layout.Pack} device={layout.Device} " +
        $"[{string.Join(", ", layout.DeviceFields.Select(static f => $"{FieldName(f)}@{f.Offset}"))}] " +
        $"host={layout.Host.IsKnown}/{layout.Host.Size}/{layout.Host.Alignment} " +
        $"[{string.Join(", ", layout.Host.Fields.Select(static f => $"{FieldName(f)}@{f.Offset}"))}] " +
        $"{layout.Transfer} {layout.Unsupported?.Reason}";

    // From metadata, an auto-property is its backing field (<A>k__BackingField); the offsets are what is compared
    private static string FieldName(FieldLayout field) =>
        field.Member.Name is ['<', .. var rest] && rest.EndsWith(">k__BackingField", StringComparison.Ordinal)
            ? rest[..^">k__BackingField".Length]
            : field.Member.Name;
}

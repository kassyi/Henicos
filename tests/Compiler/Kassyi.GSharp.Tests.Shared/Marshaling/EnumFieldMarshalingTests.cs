using Kassyi.GSharp.Compiler.Layout;
using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Runtime;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Marshaling;

/// <summary>
/// Verifies the marshaling of a struct that has an array field next to enum fields: the shadow struct and both
/// marshal methods copy an enum by value.
/// </summary>
[Category(TestCategory.Compiler)]
public class EnumFieldMarshalingTests : TranspilerTestBase
{
    private static readonly string s_source = RoslynTestUtility.ReadTestCase("Marshaling/EnumFieldMarshalSource.cs");

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public void Generator_StructWithArrayAndEnumFields_GeneratesCompilableMarshalMethods(string dialect)
    {
        var (_, output, diagnostics) = GeneratorTestRunner.RunGenerator(s_source, dialect);

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
    }

    // A field of a private enum type is declared as the underlying type of the enum in the shadow struct and the host
    // mirror, which the launcher can always name: it is not refused (GSH0108), and the generated C# builds (a shadow
    // or a mirror that named the private enum would give CS0122)
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task Generator_StructWithPrivateEnumField_GeneratesALauncherWithoutBuildErrors(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Marshaling/EnumFieldMarshalSource.cs", dialect);

        Assert.Empty(diagnostics.Ranges("GSH0108"));
        var (_, output, generatorDiagnostics) = GeneratorTestRunner.RunGenerator(s_source, dialect);
        Assert.DoesNotContain(generatorDiagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
        var kernels = output.GetTypeByMetadataName("EnumFields.JobKernels") ?? throw new InvalidOperationException();
        var launcher = (MethodDeclarationSyntax)kernels.GetMembers("LaunchRunHidden").Single()
            .DeclaringSyntaxReferences.Single().GetSyntax();
        var body = launcher.Body ?? throw new InvalidOperationException("The launcher has no body.");
        Assert.DoesNotContain(body.Statements, static statement => statement is ThrowStatementSyntax);
    }

    [Theory]
    [InlineData("_byteLevel", SpecialType.System_Byte)]
    [InlineData("_longLevel", SpecialType.System_Int64)]
    [InlineData("_ulongLevel", SpecialType.System_UInt64)]
    [InlineData("_privateMode", SpecialType.System_Int32)]
    [Scenario(TestScenario.HappyPath)]
    public void GetStorageType_PrivateEnumField_IsTheUnderlyingType(string member, SpecialType expected)
    {
        var (abiMember, assembly) = FindStorageProbeMember(member);

        var storage = HostFieldAccess.GetStorageType(abiMember, assembly);

        Assert.Equal(expected, storage.SpecialType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetStorageType_PublicEnumField_IsTheEnum()
    {
        var (abiMember, assembly) = FindStorageProbeMember("PublicMode");

        var storage = HostFieldAccess.GetStorageType(abiMember, assembly);

        Assert.Equal(TypeKind.Enum, storage.TypeKind);
        Assert.Equal("Mode", storage.Name);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetStorageType_PrivateIntField_IsTheField()
    {
        var (abiMember, assembly) = FindStorageProbeMember("_count");

        var storage = HostFieldAccess.GetStorageType(abiMember, assembly);

        Assert.Equal(SpecialType.System_Int32, storage.SpecialType);
    }

    private static (StructAbiMember Member, IAssemblySymbol Assembly) FindStorageProbeMember(string name)
    {
        var compilation = RoslynTestUtility.CreateCompilation(s_source);
        var symbol = compilation.GetTypeByMetadataName("EnumFields.StorageProbe")
            ?? throw new InvalidOperationException("EnumFields.StorageProbe");
        foreach (var member in StructAbiMembers.Get(symbol))
        {
            if (member.Name == name)
            {
                return (member, compilation.Assembly);
            }
        }

        throw new InvalidOperationException($"The struct has no member '{name}'.");
    }

    [Theory]
    [InlineData("Job", "M", nameof(NativeFieldKind.DirectValue))]
    [InlineData("Job", "BM", nameof(NativeFieldKind.DirectValue))]
    [InlineData("Job", "LM", nameof(NativeFieldKind.DirectValue))]
    [InlineData("Job", "P", nameof(NativeFieldKind.DirectValue))]
    [InlineData("Job", "Nested", nameof(NativeFieldKind.UserStruct))]
    [InlineData("Job", "Data", nameof(NativeFieldKind.ArrayProxy))]
    [InlineData("Shapes", "Count", nameof(NativeFieldKind.DirectValue))]
    [InlineData("Shapes", "Direction", nameof(NativeFieldKind.FixedBytesFallback))]
    [InlineData("Shapes", "Buffer", nameof(NativeFieldKind.FixedBuffer))]
    [InlineData("Triple", "_element", nameof(NativeFieldKind.FixedBuffer))]
    [Scenario(TestScenario.HappyPath)]
    public void ClassifyField_MemberOfStruct_ReturnsTheKindTheShadowDeclares(string type, string member,
        string expectedKind)
    {
        var compilation = RoslynTestUtility.CreateCompilation(s_source);
        var symbol = compilation.GetTypeByMetadataName("EnumFields." + type)
            ?? throw new InvalidOperationException(type);
        foreach (var field in StructAbiMembers.Get(symbol))
        {
            if (field.Name == member)
            {
                var kind = NativeStructEmitter.ClassifyField(field, AssemblyDialect.Current);
                Assert.Equal(Enum.Parse<NativeFieldKind>(expectedKind), kind);
                return;
            }
        }

        Assert.Fail($"The struct has no member '{member}'.");
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadEnumFieldsKernel_EnumsOfEveryKind_ReachTheDevice(bool forceManaged)
    {
        var job = new EnumFieldJob
        {
            Data = new int[7],
            Mode = EnumFieldMode.C,
            ByteMode = EnumFieldByteMode.Y,
            LongMode = EnumFieldLongMode.High,
            Property = EnumFieldMode.C,
            Nested = new EnumFieldInner { Mode = EnumFieldMode.B },
            SByteMode = EnumFieldSByteMode.Negative,
            ULongMode = EnumFieldULongMode.Top,
        };

        Run(forceManaged, () => EnumFieldMarshalingKernels.LaunchReadEnumFieldsKernel(job, blockDimX: 1));

        Assert.Equal([300, 200, 1, 300, 1, -1, 1], job.Data);
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteEnumFieldsKernel_EnumsOfEveryKind_ReachTheHost(bool forceManaged)
    {
        var job = new EnumFieldJob { Data = new int[1] };

        Run(forceManaged, () => EnumFieldMarshalingKernels.LaunchWriteEnumFieldsKernel(ref job, blockDimX: 1));

        Assert.Equal(EnumFieldMode.C, job.Mode);
        Assert.Equal(EnumFieldByteMode.Y, job.ByteMode);
        Assert.Equal(EnumFieldLongMode.High, job.LongMode);
        Assert.Equal(EnumFieldMode.B, job.Property);
        Assert.Equal(EnumFieldMode.C, job.Nested.Mode);
        Assert.Equal(EnumFieldSByteMode.Negative, job.SByteMode);
        Assert.Equal(EnumFieldULongMode.Top, job.ULongMode);
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadPrivateEnumFieldsKernel_EnumsBehindTheMirror_ReachTheDevice(bool forceManaged)
    {
        var job = EnumFieldPrivateJob.Create(new int[4]);

        Run(forceManaged, () => EnumFieldPrivateKernels.LaunchReadPrivateEnumFieldsKernel(job, blockDimX: 1));

        Assert.Equal([300, 1, -1, 1], job.Data);
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void WritePrivateEnumFieldsKernel_EnumsBehindTheMirror_ReachTheHost(bool forceManaged)
    {
        var job = EnumFieldPrivateJob.Create(new int[4]);
        Assert.False(job.IsFlipped);

        Run(forceManaged, () => EnumFieldPrivateKernels.LaunchWritePrivateEnumFieldsKernel(ref job, blockDimX: 1));

        Assert.True(job.IsFlipped);
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadPrivateEnumBlockKernel_IdenticalStruct_ReachesTheDevice(bool forceManaged)
    {
        var block = EnumFieldPrivateBlock.Create(count: 5, raised: true);
        var result = new int[1];

        Run(forceManaged, () =>
            EnumFieldPrivateKernels.LaunchReadPrivateEnumBlockKernel(block, result, blockDimX: 1));

        Assert.Equal([205], result);
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadPrivateEnumArrayKernel_StructArray_ReachesTheDevice(bool forceManaged)
    {
        EnumFieldPrivateBlock[] blocks =
        [
            EnumFieldPrivateBlock.Create(0, raised: false),
            EnumFieldPrivateBlock.Create(0, raised: true),
            EnumFieldPrivateBlock.Create(0, raised: false),
        ];
        var result = new int[3];

        Run(forceManaged, () =>
            EnumFieldPrivateKernels.LaunchReadPrivateEnumArrayKernel(blocks, result, blockDimX: 3));

        Assert.Equal([0, 200, 0], result);
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadPrivateEnumNestedKernel_NestedStruct_ReachesTheDevice(bool forceManaged)
    {
        var data = new int[5];
        var outer = new EnumFieldPrivateOuter { Inner = EnumFieldPrivateJob.Create(data), Y = 9 };

        Run(forceManaged, () => EnumFieldPrivateKernels.LaunchReadPrivateEnumNestedKernel(outer, blockDimX: 1));

        Assert.Equal([300, 1, -1, 1, 9], data);
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadPrivateEnumExplicitKernel_NegativeShortAtAnOffset_ReachesTheDevice(bool forceManaged)
    {
        var data = new int[1];
        var value = EnumFieldPrivateExplicit.Create(low: true, count: 5, data);

        Run(forceManaged, () =>
            EnumFieldPrivateKernels.LaunchReadPrivateEnumExplicitKernel(value, blockDimX: 1));

        Assert.Equal([-6995], data);
    }

    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            launch();
        }
    }
}

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

    // GSH0108 refuses a struct whose field has a private enum type, although the field could be copied: #2195 (GSH0108
    // refuses a struct field whose enum type is private, although the field can be copied). The launcher throws and
    // no marshal method is written, so the build stays clean
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task Generator_StructWithPrivateEnumField_RefusesTheLaunchWithoutBuildErrors(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Marshaling/EnumFieldMarshalSource.cs", dialect);

        Assert.Equal([(Analyzer, 63, 34, 63, 43)], diagnostics.Ranges("GSH0108"));
        var (_, output, generatorDiagnostics) = GeneratorTestRunner.RunGenerator(s_source, dialect);
        Assert.DoesNotContain(generatorDiagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        var kernels = output.GetTypeByMetadataName("EnumFields.JobKernels") ?? throw new InvalidOperationException();
        var launcher = (MethodDeclarationSyntax)kernels.GetMembers("LaunchRunHidden").Single()
            .DeclaringSyntaxReferences.Single().GetSyntax();
        var body = launcher.Body ?? throw new InvalidOperationException("The launcher has no body.");
        Assert.Collection(body.Statements, static statement => Assert.IsType<ThrowStatementSyntax>(statement));
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

    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            launch();
        }
    }
}

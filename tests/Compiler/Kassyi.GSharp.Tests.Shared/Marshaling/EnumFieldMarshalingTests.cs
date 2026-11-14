#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Compiler.Layout;
using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Infrastructure;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Marshaling;

/// <summary>
/// Verifies struct marshaling when a struct contains both array fields and enum fields (Issue #1264).
/// </summary>
[Category(TestCategory.Compiler)]
public class EnumFieldMarshalingTests : TranspilerTestBase
{
    private const string ReproSource = """
        using Kassyi.GSharp.Core;

        namespace Issue1264Repro;

        public enum Mode { A, B }
        public enum ByteMode : byte { X = 0, Y = 1 }
        public enum LongMode : long { Low = 0, High = 1 }

        public struct Job
        {
            public int[] Data;
            public Mode M;
            public ByteMode BM;
            public LongMode LM;
        }

        public static partial class JobKernels
        {
            [GpuKernel]
            public static void Run(Job job)
            {
                int tid = ThreadIndex.X;
                job.Data[tid] = (job.M == Mode.B && job.BM == ByteMode.Y && job.LM == LongMode.High) ? 1 : 0;
            }
        }
        """;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ClassifyField_EnumFields_ClassifiedAsDirectValue()
    {
        var compilation = RoslynTestUtility.CreateCompilation(ReproSource);
        var jobType = compilation.GetTypeByMetadataName("Issue1264Repro.Job") ?? throw new InvalidOperationException("Job not found");

        StructAbiMember? modeMember = null;
        StructAbiMember? byteModeMember = null;
        StructAbiMember? longModeMember = null;
        foreach (var m in StructAbiMembers.Get(jobType))
        {
            if (m.Name == "M")
            {
                modeMember = m;
            }

            if (m.Name == "BM")
            {
                byteModeMember = m;
            }

            if (m.Name == "LM")
            {
                longModeMember = m;
            }
        }

        Assert.NotNull(modeMember);
        Assert.Equal(NativeStructEmitter.NativeFieldKind.DirectValue, NativeStructEmitter.ClassifyField(modeMember.Value));

        Assert.NotNull(byteModeMember);
        Assert.Equal(NativeStructEmitter.NativeFieldKind.DirectValue, NativeStructEmitter.ClassifyField(byteModeMember.Value));

        Assert.NotNull(longModeMember);
        Assert.Equal(NativeStructEmitter.NativeFieldKind.DirectValue, NativeStructEmitter.ClassifyField(longModeMember.Value));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public void Generator_StructWithArrayAndEnumFields_CompilesWithoutCS8385(string dialect)
    {
        var (_, output, diags) = GeneratorTestRunner.RunGenerator(ReproSource, dialect);

        // Neither the generator itself nor the compilation of generated source should report errors
        Assert.DoesNotContain(diags, static d => d.Severity == DiagnosticSeverity.Error);

        var compilationErrors = output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.Empty(compilationErrors);

        // Direct value assignment must be generated for all enum fields rather than fixed byte buffers
        var generatedSource = string.Join("\n", output.SyntaxTrees.Select(static t => t.ToString()));
        Assert.Contains("native.M = host.M;", generatedSource);
        Assert.Contains("host.M = native.M;", generatedSource);
        Assert.Contains("native.BM = host.BM;", generatedSource);
        Assert.Contains("host.BM = native.BM;", generatedSource);
        Assert.Contains("native.LM = host.LM;", generatedSource);
        Assert.Contains("host.LM = native.LM;", generatedSource);
        Assert.DoesNotContain("fixed (byte* pField = native.M)", generatedSource);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void RunJob_Execution_TransfersEnumAndArrayValues()
    {
        var jobB = new Issue1264Job { Data = [0, 0, 0, 0], M = Issue1264Mode.B };
        EnumFieldMarshalingKernels.LaunchRunJob(jobB, blockDimX: 4);
        Assert.All(jobB.Data, static val => Assert.Equal(42, val));

        var jobA = new Issue1264Job { Data = [0, 0, 0, 0], M = Issue1264Mode.A };
        EnumFieldMarshalingKernels.LaunchRunJob(jobA, blockDimX: 4);
        Assert.All(jobA.Data, static val => Assert.Equal(7, val));
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void RunByteJob_Execution_TransfersByteEnumAndArrayValues()
    {
        var jobB = new Issue1264ByteJob { Data = [0, 0, 0, 0], M = Issue1264ByteMode.B };
        EnumFieldMarshalingKernels.LaunchRunByteJob(jobB, blockDimX: 4);
        Assert.All(jobB.Data, static val => Assert.Equal(42, val));

        var jobA = new Issue1264ByteJob { Data = [0, 0, 0, 0], M = Issue1264ByteMode.A };
        EnumFieldMarshalingKernels.LaunchRunByteJob(jobA, blockDimX: 4);
        Assert.All(jobA.Data, static val => Assert.Equal(7, val));
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void RunLongJob_Execution_TransfersLongEnumAndArrayValues()
    {
        var jobB = new Issue1264LongJob { Data = [0, 0, 0, 0], M = Issue1264LongMode.B };
        EnumFieldMarshalingKernels.LaunchRunLongJob(jobB, blockDimX: 4);
        Assert.All(jobB.Data, static val => Assert.Equal(42, val));

        var jobA = new Issue1264LongJob { Data = [0, 0, 0, 0], M = Issue1264LongMode.A };
        EnumFieldMarshalingKernels.LaunchRunLongJob(jobA, blockDimX: 4);
        Assert.All(jobA.Data, static val => Assert.Equal(7, val));
    }

#if GSHARP_CPU
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void RunJob_ManagedExecution_MatchesExpected()
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            var job = new Issue1264Job { Data = [0, 0], M = Issue1264Mode.B };
            EnumFieldMarshalingKernels.LaunchRunJob(job, blockDimX: 2);
            Assert.Equal([42, 42], job.Data);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void RunByteJob_ManagedExecution_MatchesExpected()
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            var job = new Issue1264ByteJob { Data = [0, 0], M = Issue1264ByteMode.B };
            EnumFieldMarshalingKernels.LaunchRunByteJob(job, blockDimX: 2);
            Assert.Equal([42, 42], job.Data);
        }
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void RunLongJob_ManagedExecution_MatchesExpected()
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            var job = new Issue1264LongJob { Data = [0, 0], M = Issue1264LongMode.B };
            EnumFieldMarshalingKernels.LaunchRunLongJob(job, blockDimX: 2);
            Assert.Equal([42, 42], job.Data);
        }
    }
#endif
}

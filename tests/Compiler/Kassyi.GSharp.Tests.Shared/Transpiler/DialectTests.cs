using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Compiler.Transpiler.Dialects;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

[Category(TestCategory.Compiler)]
public class DialectTests
{
        [Fact]
        public void Test_CanLoadStandardCpp()
        {
            var dialect = new StandardCppDialect();
            Assert.NotNull(dialect);

            bool result = dialect.TryMapType("System.Int32", out string mappedType);

            Assert.True(result);
            Assert.Equal("int", mappedType);

            Assert.Equal("StandardCppDialect", dialect.Name);
            Assert.False(dialect.IsCuda);
            Assert.True(dialect.EmulatesThreadsWithStateMachine());
            Assert.False(DialectCallSupport.SupportsInlinePtx(dialect));
            Assert.Equal(
                "Gpu.InlinePtx is not supported by the StandardCppDialect dialect",
                DialectCallSupport.GetInlinePtxUnsupportedMessage(dialect.Name));
        }

        [Fact]
        public void Test_CanLoadGSharpCuda()
        {
            var dialect = new GSharpCudaDialect();
            Assert.NotNull(dialect);

            // Backend and capabilities come from GSharpCuda.yml
            Assert.Equal("GSharpCudaDialect", dialect.Name);
            Assert.True(dialect.IsCuda);
            Assert.False(dialect.EmulatesThreadsWithStateMachine());
            Assert.True(DialectCallSupport.SupportsInlinePtx(dialect));
        }

        [Fact]
        public void Test_DialectCallSupport_InlinePtx_DistinguishesSymbolAndDialectSupport()
        {
            var compilation = RoslynTestUtility.CreateCompilation(
                RoslynTestUtility.ReadTestCase("Analysis/ManagedVersionCases.cs"));
            var tree = compilation.SyntaxTrees.Single();
            var model = compilation.GetSemanticModel(tree);

            var invocations = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
            var gpuCall = invocations.First(i => i.ToString() == "Gpu.InlinePtx(\"nop;\")");
            var unqualifiedCall = invocations.First(i => i.ToString() == "InlinePtx(\"nop;\")");
            var userCall = invocations.First(i => i.ToString() == "Lookalike.InlinePtx(\"nop;\")");

            // Symbol-only check (dialect-agnostic)
            Assert.True(DialectCallSupport.IsInlinePtxCall(gpuCall, model));
            Assert.True(DialectCallSupport.IsInlinePtxCall(unqualifiedCall, model));
            Assert.False(DialectCallSupport.IsInlinePtxCall(userCall, model));

            // Dialect-specific support check
            var cuda = new GSharpCudaDialect();
            var cpu = HostDialects.Cpu;

            // CUDA supports inline PTX
            Assert.False(DialectCallSupport.IsUnsupportedInlinePtxCall(gpuCall, model, cuda));
            Assert.False(DialectCallSupport.IsUnsupportedInlinePtxCall(unqualifiedCall, model, cuda));
            Assert.False(DialectCallSupport.IsUnsupportedInlinePtxCall(userCall, model, cuda));

            // CPU does not support inline PTX
            Assert.True(DialectCallSupport.IsUnsupportedInlinePtxCall(gpuCall, model, cpu));
            Assert.True(DialectCallSupport.IsUnsupportedInlinePtxCall(unqualifiedCall, model, cpu));
            Assert.False(DialectCallSupport.IsUnsupportedInlinePtxCall(userCall, model, cpu));
        }
    }

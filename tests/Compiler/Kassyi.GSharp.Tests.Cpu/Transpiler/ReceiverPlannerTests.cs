#nullable enable
#pragma warning disable CS0649, CS8656
using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Unit tests for <see cref="ReceiverParameter"/>, <see cref="ReceiverPlanner"/>, <see cref="ReadOnlyReceivers"/>,
/// <c>EvaluationOrder.BeginReceiverCall</c>, <c>readonly struct</c> receiver signatures, and generic struct
/// <c>GSH0066</c> handling.
/// </summary>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class ReceiverPlannerTests
{
    private const string FixtureSource = """
        #pragma warning disable CS0649, CS8656
        using System.Runtime.CompilerServices;
        using Kassyi.GSharp.Core;

        namespace ReceiverPlannerFixture;

        public struct PlannerStruct
        {
            public int X;

            public PlannerStruct(int x)
            {
                X = x;
                Mutate(1);
            }

            [GpuDeviceFunction]
            public static int StaticHelper(int x) => x + 1;

            [GpuDeviceFunction]
            public int Mutate(int v)
            {
                X += v;
                return X;
            }

            [GpuDeviceFunction]
            public readonly int Read() => X;

            [GpuDeviceFunction]
            public readonly int ReadAfter(ref PlannerStruct other)
            {
                other.X = 500;
                return X;
            }

            [GpuDeviceFunction]
            public readonly int ReadAfterHolder(ref PlannerInlineHolder holder)
            {
                PlannerInlineBuf replacement = default;
                replacement[0] = new PlannerStruct(999);
                holder.AutoBuf = replacement;
                return X;
            }

            [GpuDeviceFunction]
            public int NonReadonlyPure() => X + 1;

            [GpuDeviceFunction]
            public void ImplicitThisMutating() => Mutate(1);

            [GpuDeviceFunction]
            public void ExplicitThisMutating() => this.Mutate(1);

            [GpuDeviceFunction]
            public readonly int ReadOnlyCallsMutating() => Mutate(1);

            [GpuDeviceFunction]
            public readonly int ReadOnlyCallsExplicitMutating() => this.Mutate(1);

            [GpuDeviceFunction]
            public readonly int ReadOnlyCallsReadOnly() => Read();

            public int MutatingGetterCallsMutating => Mutate(1);

            public readonly int ReadOnlyGetterCallsMutating => Mutate(1);

            public int ConstInferredGetterCallsNonReadOnly => NonReadonlyPure();
        }

        public readonly struct PlannerReadOnlyStruct
        {
            public readonly int V;

            public PlannerReadOnlyStruct(int v) => V = v;

            [GpuDeviceFunction]
            public int UnmarkedMethod() => V;

            [GpuDeviceFunction]
            public readonly int MarkedReadOnly() => V;
        }

        public class PlannerClass
        {
            [GpuDeviceFunction]
            public int InstanceMethod(int x) => x;
        }

        public struct PlannerHolder
        {
            public readonly PlannerStruct Ro;
            public PlannerStruct Rw;
            public PlannerStruct Auto { get; set; }

            public PlannerHolder(int x)
            {
                Ro = new PlannerStruct(x);
                Ro.Mutate(1);
                Rw = new PlannerStruct(x);
                Auto = new PlannerStruct(x);
            }

            public int ConstInferredGetterCallsFieldNonReadOnly => Rw.NonReadonlyPure();
        }

        public struct PlannerOuterHolder
        {
            public PlannerHolder Rw;

            public int ConstInferredGetterCallsNestedFieldNonReadOnly => Rw.Rw.NonReadonlyPure();
        }

        public struct PlannerParenThisHolder
        {
            public readonly PlannerStruct Ro;
            public PlannerStruct Rw;

            public PlannerParenThisHolder(int x)
            {
                Ro = new PlannerStruct(x);
                ((this)).Ro.Mutate(5);
                Rw = new PlannerStruct(x);
            }

            [GpuDeviceFunction]
            public int MutateParenThisRw() => ((this)).Rw.Mutate(5);
        }

        [InlineArray(2)]
        public struct PlannerInlineBuf
        {
            private PlannerStruct _e0;
        }

        public struct PlannerInlineHolder
        {
            public PlannerInlineBuf AutoBuf { get; set; }
            public PlannerInlineBuf RwBuf;
        }

        public struct PlannerConstGetterInlineHolder
        {
            public PlannerInlineBuf RwBuf;
            public PlannerInlineMidBuf Mb;
            public PlannerStruct Rw1;
            public PlannerStruct Rw2;
            public bool Flag;

            public int ConstGetterInlineBuf => RwBuf[0].NonReadonlyPure();

            public int ConstGetterInlineFieldChain => Mb[0].Rw.NonReadonlyPure();

            public int ConstGetterRefCond => (Flag ? ref Rw1 : ref Rw2).NonReadonlyPure();

            public int ConstGetterParenInlineBuf => ((RwBuf[0])).NonReadonlyPure();

            public int ConstGetterConstFalseLocalFirst
            {
                get
                {
                    var local = new PlannerStruct(50);
                    return (false ? ref local : ref Rw1).NonReadonlyPure();
                }
            }

            public int ConstGetterConstFalseMemberFirst
            {
                get
                {
                    var local = new PlannerStruct(50);
                    return (false ? ref Rw1 : ref local).NonReadonlyPure();
                }
            }

            public int ConstGetterConstFalseInlineLocalFirst
            {
                get
                {
                    PlannerInlineBuf localBuf = default;
                    return (false ? ref localBuf : ref RwBuf)[0].NonReadonlyPure();
                }
            }

            public int ConstGetterConstFalseInlineMemberFirst
            {
                get
                {
                    PlannerInlineBuf localBuf = default;
                    return (false ? ref RwBuf : ref localBuf)[0].NonReadonlyPure();
                }
            }

            public int ConstGetterRuntimeMixedMemberLocal
            {
                get
                {
                    var local = new PlannerStruct(70);
                    return (Flag ? ref Rw1 : ref local).NonReadonlyPure();
                }
            }
        }

        public struct PlannerPrimaryBufConstGetterHolder(PlannerInlineBuf buf)
        {
            public int ConstGetterPrimaryInlineBuf => buf[0].NonReadonlyPure();
        }

        public unsafe struct PlannerPtrHolder
        {
            public PlannerStruct* Ptr;

            public int ConstGetterPtrNonReadOnly => Ptr->NonReadonlyPure();
        }

        [InlineArray(2)]
        public struct PlannerInlineMidBuf
        {
            private PlannerHolder _e0;
        }

        public interface IPlannerPing
        {
            int Ping();
        }

        public struct PlannerExplicitImpl : IPlannerPing
        {
            [GpuDeviceFunction]
            int IPlannerPing.Ping() => 1;
        }

        public static class PlannerExtensions
        {
            [GpuDeviceFunction]
            public static int ExtMutate(this ref PlannerStruct s, int v) => s.Mutate(v);

            [GpuDeviceFunction]
            public static int ExtRead(this in PlannerStruct s) => s.Read();

            [GpuDeviceFunction]
            public static int ExtReadAfter(this in PlannerStruct s, ref PlannerStruct other)
            {
                other.X = 500;
                return s.X;
            }

            [GpuDeviceFunction]
            public static int ExtByVal(this PlannerStruct s) => s.X;
        }

        public static partial class PlannerCases
        {
            [GpuDeviceFunction]
            private static PlannerStruct Make(int x) => new(x);

            [GpuDeviceFunction]
            private static PlannerInlineBuf MakeBuf(int x)
            {
                PlannerInlineBuf b = default;
                b[0] = new PlannerStruct(x);
                return b;
            }

            [GpuDeviceFunction]
            private static PlannerHolder MakeHolder(int x) => new(x);

            [GpuDeviceFunction]
            private static ref PlannerStruct RefTo(ref PlannerStruct s) => ref s;

            [GpuDeviceFunction]
            private static ref readonly PlannerStruct RefReadOnlyTo(in PlannerStruct s) => ref s;

            [GpuDeviceFunction]
            private static int Bump(ref PlannerStruct s)
            {
                s.X += 100;
                return 1;
            }

            [GpuDeviceFunction]
            private static ref PlannerStruct RefBump(ref PlannerStruct s)
            {
                s.X += 100;
                return ref s;
            }

            [GpuDeviceFunction]
            private static unsafe PlannerStruct* IdPtr(PlannerStruct* p) => p;

            [GpuDeviceFunction]
            private static unsafe int BumpPtr(PlannerStruct* p)
            {
                p->X += 100;
                return 1;
            }

            [GpuDeviceFunction]
            private static unsafe int MovePtr(PlannerStruct** ptrSlot, PlannerStruct* next)
            {
                *ptrSlot = next;
                return 1;
            }

            public static void CallMutatingOnLocal(PlannerStruct s) => s.Mutate(1);
            public static void CallReadOnlyOnLocal(PlannerStruct s) => s.Read();
            public static void CallMutatingOnElement(PlannerStruct[] arr) => arr[0].Mutate(1);
            public static void CallMutatingOnField(PlannerHolder h) => h.Rw.Mutate(1);
            public static void CallMutatingOnRefParam(ref PlannerStruct s) => s.Mutate(1);
            public static void CallMutatingOnRefReturn(ref PlannerStruct s) => RefTo(ref s).Mutate(1);
            public static void CallReadOnlyOnInParam(in PlannerStruct s) => s.Read();
            public static void CallReadOnlyOnRefReadOnlyLocal(ref PlannerStruct s)
            {
                ref readonly var r = ref s;
                r.Read();
            }

            public static void CallReadOnlyOnRefReadOnlyReturn(in PlannerStruct s) => RefReadOnlyTo(in s).Read();
            public static void CallMutatingOnInParam(in PlannerStruct s) => s.Mutate(1);
            public static void CallMutatingOnRefReadOnlyLocal(ref PlannerStruct s)
            {
                ref readonly var r = ref s;
                r.Mutate(1);
            }

            public static void CallMutatingOnReadonlyField(PlannerHolder h) => h.Ro.Mutate(1);
            public static void CallMutatingOnFieldOfInParam(in PlannerHolder h) => h.Rw.Mutate(1);
            public static void CallMutatingOnRefReadOnlyReturn(in PlannerStruct s) => RefReadOnlyTo(in s).Mutate(1);
            public static void CallMutatingOnRvalue() => Make(1).Mutate(1);
            public static void CallReadOnlyOnRvalue() => Make(1).Read();
            public static void CallMutatingOnAutoProperty(PlannerHolder h) => h.Auto.Mutate(1);
            public static void CallParenthesizedMember(PlannerStruct s) => ((s.Mutate))(1);
            public static void CallAssignReadOnly(PlannerStruct s1, PlannerStruct s2) => (s1 = s2).Read();
            public static void CallCondReadOnly(bool c, PlannerStruct s1, PlannerStruct s2) => (c ? s1 : s2).Read();
            public static void CallAssignExtReadAfter(PlannerStruct s1, PlannerStruct s2) => (s1 = s2).ExtReadAfter(ref s1);
            public static void CallCondChainMutating(bool c, PlannerOuterHolder ha, PlannerOuterHolder hb) =>
                (c ? ha : hb).Rw.Rw.Mutate(7);
            public static void CallAssignChainMutating(PlannerOuterHolder ha, PlannerOuterHolder hb) =>
                (ha = hb).Rw.Rw.Mutate(7);
            public static void CallReadOnlyOnRefReadOnlyCond(bool c, in PlannerStruct a, in PlannerStruct b) =>
                (c ? ref a : ref b).Read();
            public static void CallMutatingOnRefReadOnlyCond(bool c, in PlannerStruct a, in PlannerStruct b) =>
                (c ? ref a : ref b).Mutate(1);
            public static void CallMutatingOnMixedRefReadOnlyCond(bool c, ref PlannerStruct a, in PlannerStruct b) =>
                (c ? ref a : ref b).Mutate(1);
            public static void CallMutatingOnRefCond(bool c, ref PlannerStruct a, ref PlannerStruct b) =>
                (c ? ref a : ref b).Mutate(1);
            public static void CallMutatingOnRefReadOnlyCondField(bool c, in PlannerHolder a, in PlannerHolder b) =>
                (c ? ref a : ref b).Rw.Mutate(1);
            public static void CallMutatingOnConstFalseRefCond(ref PlannerStruct s1, ref PlannerStruct s2)
            {
                ref readonly var r = ref s1;
                (false ? ref r : ref s2).Mutate(1);
            }
            public static void CallMutatingOnConstTrueRefCondSecond(ref PlannerStruct s1, ref PlannerStruct s2)
            {
                ref readonly var r = ref s1;
                (true ? ref s2 : ref r).Mutate(1);
            }
            public static void CallMutatingOnConstTrueRefCondReadOnly(ref PlannerStruct s1, ref PlannerStruct s2)
            {
                ref readonly var r = ref s1;
                (true ? ref r : ref s2).Mutate(1);
            }
            public static void CallReadOnlyOnConstFalseRefCond(ref PlannerStruct s1, ref PlannerStruct s2)
            {
                ref readonly var r = ref s1;
                (false ? ref r : ref s2).Read();
            }
            public static void CallMutatingOnInInlineArrayElement(in PlannerInlineBuf b) => b[0].Mutate(1);
            public static void CallMutatingOnLocalInlineArrayElement(PlannerInlineBuf b) => b[0].Mutate(1);
            public static void CallReadOnlyOnInInlineArrayElement(in PlannerInlineBuf b) => b[0].Read();
            public static void CallMutatingOnRvalueInlineArrayElement() => MakeBuf(10)[0].Mutate(1);
            public static void CallMutatingOnCondInlineArrayElement(bool c) => (c ? MakeBuf(10) : MakeBuf(20))[0].Mutate(1);
            public static void CallMutatingOnAssignInlineArrayElement(PlannerInlineBuf b1, PlannerInlineBuf b2) =>
                (b1 = b2)[0].Mutate(1);
            public static void CallMutatingOnAutoPropertyInlineArrayElement(PlannerInlineHolder h) =>
                h.AutoBuf[0].Mutate(1);
            public static void CallMutatingOnRvalueHolderField() => MakeHolder(10).Rw.Mutate(1);
            public static void CallMutatingOnRvalueHolderAuto() => MakeHolder(10).Auto.Mutate(1);
            public static void CallMutatingOnNewHolderField() => new PlannerHolder(10).Rw.Mutate(1);
            public static void CallMutatingOnSwitchHolderField(int k) =>
                (k switch { 1 => MakeHolder(1), _ => MakeHolder(2) }).Rw.Mutate(1);
            public static int CallReadAfterOnAssignInlineArrayElement(PlannerInlineBuf b1, PlannerInlineBuf b2) =>
                (b1 = b2)[0].ReadAfter(ref b1[0]);
            public static int CallExtReadAfterOnAssignInlineArrayElement(PlannerInlineBuf b1, PlannerInlineBuf b2) =>
                (b1 = b2)[0].ExtReadAfter(ref b1[0]);
            public static int CallReadAfterOnAutoPropertyInlineArrayElement(PlannerInlineHolder h) =>
                h.AutoBuf[0].ReadAfterHolder(ref h);
            public static int CallReadAfterOnCondInlineArrayElement(bool cond, PlannerInlineBuf b1, PlannerInlineBuf b2) =>
                (cond ? b1 : b2)[0].ReadAfter(ref b1[0]);
            public static int CallReadAfterOnAssignInlineArrayFieldChain(PlannerInlineMidBuf mb1, PlannerInlineMidBuf mb2) =>
                (mb1 = mb2)[0].Rw.ReadAfter(ref mb1[0].Rw);
            public static void CallMutatingOnConstFalseInlineRefCond(in PlannerInlineBuf bIn, ref PlannerInlineBuf bRw) =>
                (false ? ref bIn : ref bRw)[0].Mutate(1);
            public static void CallMutatingOnConstTrueRwFirstInlineRefCond(ref PlannerInlineBuf bRw, in PlannerInlineBuf bIn) =>
                (true ? ref bRw : ref bIn)[0].Mutate(1);
            public static void CallMutatingOnConstFalseHolderInlineRefCond(in PlannerInlineHolder hIn, ref PlannerInlineHolder hRw) =>
                (false ? ref hIn : ref hRw).RwBuf[0].Mutate(1);
            public static void CallMutatingOnConstFalseWritableInlineRefCond(ref PlannerInlineBuf bRw1, ref PlannerInlineBuf bRw2) =>
                (false ? ref bRw1 : ref bRw2)[0].Mutate(1);

            public static unsafe void CallMutatingOnPointerArrow(PlannerStruct* p) => p->Mutate(5);
            public static unsafe void CallReadOnlyOnPointerArrow(PlannerStruct* p) => p->Read();
            public static unsafe void CallMutatingOnParenthesizedPointerArrow(PlannerStruct* p) => ((p))->Mutate(5);
            public static unsafe void CallMutatingOnCondPointerArrow(bool flag, PlannerStruct* p1, PlannerStruct* p2) =>
                (flag ? p1 : p2)->Mutate(5);
            public static unsafe void CallMutatingOnPrvaluePointerArrow(PlannerStruct s) => IdPtr(&s)->Mutate(5);
            public static unsafe void CallReadOnlyOnPrvaluePointerArrow(PlannerStruct s) => IdPtr(&s)->Read();
            public static void CallParenthesizedReceiver(PlannerStruct s) => ((s)).Mutate(1);
            public static unsafe void CallParenthesizedPointerMember(PlannerStruct* p) => ((p->Mutate))(5);

            [GpuDeviceFunction]
            public static unsafe int WriteThroughPointerHelper(PlannerStruct* p) => p->Mutate(5);

            [GpuDeviceFunction]
            public static unsafe int OrderPointerWriteBeforeDereference(PlannerStruct* p1, PlannerStruct* p2) =>
                p1->Mutate(5) + (*p2).X;

            public static void ExtRef(ref PlannerStruct s) => s.ExtMutate(1);
            public static void ExtInVariable(in PlannerStruct s) => s.ExtRead();
            public static void ExtInRvalue() => Make(1).ExtRead();
            public static void ExtByValue(PlannerStruct s) => s.ExtByVal();

            [GpuDeviceFunction]
            public static int OrderAddress(PlannerStruct s) => s.Mutate(Bump(ref s));

            [GpuDeviceFunction]
            public static int OrderCopy(in PlannerStruct p, ref PlannerStruct q) => p.Mutate(Bump(ref q));

            [GpuDeviceFunction]
            public static int OrderTemporary(PlannerStruct s) => Make(s.X).Mutate(Bump(ref s));

            [GpuDeviceFunction]
            public static int OrderTemporaryInlineArrayElement(PlannerStruct s) =>
                MakeBuf(s.X)[0].Mutate(Bump(ref s));

            [GpuDeviceFunction]
            public static int OrderTemporaryHolderField(PlannerStruct s) =>
                MakeHolder(s.X).Rw.Mutate(Bump(ref s));

            [GpuDeviceFunction]
            public static int OrderReadAfterAssignInlineArrayElement(PlannerInlineBuf b1, PlannerInlineBuf b2) =>
                (b1 = b2)[0].ReadAfter(ref RefBump(ref b1[0]));

            [GpuDeviceFunction]
            public static unsafe int OrderPointerArrow(PlannerStruct* p) => p->Mutate(BumpPtr(p));

            [GpuDeviceFunction]
            public static unsafe int OrderPrvaluePointerArrow(PlannerStruct s) => IdPtr(&s)->Mutate(Bump(ref s));

            [GpuDeviceFunction]
            public static unsafe int OrderPointerReassignArrow(PlannerStruct* p, PlannerStruct* next) =>
                p->Mutate(MovePtr(&p, next));

            [GpuKernel]
            public static void DummyKernel(int[] o) => o[0] = 0;
        }
        """;

    private static readonly Lazy<Compilation> s_testCompilation = new(static () =>
        RoslynTestUtility.CreateCompilation(FixtureSource));

    private static Compilation CompileCaller() => s_testCompilation.Value;

    private static (MethodDeclarationSyntax Method, IMethodSymbol Symbol, SemanticModel Model) FindMethod(
        Compilation compilation,
        string typeName,
        string methodName)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var typeDecl in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (typeDecl.Identifier.ValueText != typeName)
                {
                    continue;
                }

                var method = typeDecl.Members
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(m => m.Identifier.ValueText == methodName);
                if (method != null && model.GetDeclaredSymbol(method) is { } symbol)
                {
                    return (method, symbol, model);
                }
            }
        }

        throw new InvalidOperationException($"Method '{typeName}.{methodName}' not found.");
    }

    private static (InvocationExpressionSyntax Call, IMethodSymbol Callee, SemanticModel Model) FindCall(
        string callerType,
        string callerMethod,
        string calleeName)
    {
        var compilation = CompileCaller();
        var (method, _, model) = FindMethod(compilation, callerType, callerMethod);
        var call = method.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .First(i =>
            {
                var expr = i.Expression;
                while (expr is ParenthesizedExpressionSyntax parenthesized)
                {
                    expr = parenthesized.Expression;
                }

                return expr switch
                {
                    MemberAccessExpressionSyntax { Name.Identifier.ValueText: var n } => n == calleeName,
                    IdentifierNameSyntax { Identifier.ValueText: var n } => n == calleeName,
                    _ => false
                };
            });
        var callee = model.GetSymbolInfo(call).Symbol as IMethodSymbol
                     ?? throw new InvalidOperationException($"Call '{calleeName}' in '{callerType}.{callerMethod}' did not bind.");
        return (call, callee, model);
    }

    private static (InvocationExpressionSyntax Call, IMethodSymbol Callee, SemanticModel Model) FindCallInMember(
        string callerType,
        string memberName,
        string calleeName)
    {
        var compilation = CompileCaller();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var typeDecl in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (typeDecl.Identifier.ValueText != callerType)
                {
                    continue;
                }

                var member = typeDecl.Members.FirstOrDefault(m => m switch
                {
                    PropertyDeclarationSyntax p => p.Identifier.ValueText == memberName,
                    ConstructorDeclarationSyntax => memberName == ".ctor",
                    MethodDeclarationSyntax d => d.Identifier.ValueText == memberName,
                    _ => false
                });
                if (member != null)
                {
                    var call = member.DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .First(i => i.Expression switch
                        {
                            MemberAccessExpressionSyntax { Name.Identifier.ValueText: var n } => n == calleeName,
                            IdentifierNameSyntax { Identifier.ValueText: var n } => n == calleeName,
                            _ => false
                        });
                    var callee = model.GetSymbolInfo(call).Symbol as IMethodSymbol
                                 ?? throw new InvalidOperationException($"Call '{calleeName}' in '{callerType}.{memberName}' did not bind.");
                    return (call, callee, model);
                }
            }
        }

        throw new InvalidOperationException($"Member '{callerType}.{memberName}' not found.");
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("PlannerStruct", "Mutate", nameof(MethodEmissionKind.DeviceFunction), true, false, false)]
    [InlineData("PlannerStruct", "Read", nameof(MethodEmissionKind.DeviceFunction), true, true, false)]
    [InlineData("PlannerStruct", "StaticHelper", nameof(MethodEmissionKind.DeviceFunction), false, false, false)]
    [InlineData("PlannerStruct", "Mutate", nameof(MethodEmissionKind.SuspendingDeviceFunction), false, false, true)]
    [InlineData("PlannerReadOnlyStruct", "UnmarkedMethod", nameof(MethodEmissionKind.DeviceFunction), true, true, false)]
    [InlineData("PlannerReadOnlyStruct", "MarkedReadOnly", nameof(MethodEmissionKind.DeviceFunction), true, true, false)]
    [InlineData("PlannerClass", "InstanceMethod", nameof(MethodEmissionKind.DeviceFunction), false, false, true)]
    [InlineData("PlannerCases", "DummyKernel", nameof(MethodEmissionKind.Kernel), false, false, false)]
    public void ReceiverParameter_Plan_DecidesTypeConstAndUnsupported(
        string typeName,
        string methodName,
        string kindName,
        bool expectedHasParameter,
        bool expectedIsConst,
        bool expectedFails)
    {
        var kind = Enum.Parse<MethodEmissionKind>(kindName);
        var compilation = CompileCaller();
        var (_, symbol, _) = FindMethod(compilation, typeName, methodName);

        var plan = ReceiverParameter.Plan(symbol, kind);

        Assert.Equal(expectedHasParameter, plan.HasParameter);
        Assert.Equal(expectedIsConst, plan.IsConst);
        Assert.Equal(expectedFails, plan.FailsUnsupported);
        if (expectedHasParameter)
        {
            Assert.True(SymbolEqualityComparer.Default.Equals(symbol.ContainingType, plan.Type));
        }
    }

    // T3: every [GpuDeviceFunction] on a readonly struct emits const T* __gsharp_this in C++.
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("UnmarkedMethod")]
    [InlineData("MarkedReadOnly")]
    public void ReadOnlyStruct_EmittedSignature_TakesConstReceiverPointer(string methodName)
    {
        var compilation = CompileCaller();
        var (method, _, model) = FindMethod(compilation, "PlannerReadOnlyStruct", methodName);
        var emissionPlan = MethodEmissionPlan.Create(method, model, isKernel: false);

        var signature = MethodTranspiler.GenerateCudaSignature(emissionPlan, reportFailure: null, new GSharpCudaDialect());
        Assert.NotNull(emissionPlan.Symbol);
        var typeName = GlobalCppNames.GetTypeName(emissionPlan.Symbol.ContainingType);

        Assert.Contains($"(const {typeName}* __gsharp_this", signature, StringComparison.Ordinal);
    }

    // T8: generic struct instance device functions keep GSH0066 because GenericCopies is not wired into GpuKernelGenerator.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task GenericStructInstanceMethod_GeneratorCompilation_ReportsGsh0066()
    {
        var compilation = RoslynTestUtility.CreateCompilation("""
            using Kassyi.GSharp.Core;

            public struct Box<T> where T : unmanaged
            {
                public T Value;

                [GpuDeviceFunction]
                public void Set(T v) { Value = v; }
            }

            public static partial class BoxKernels
            {
                [GpuKernel]
                public static void Use(int[] intOutput, float[] floatOutput)
                {
                    var bi = new Box<int>();
                    bi.Set(42);
                    intOutput[ThreadIndex.X] = bi.Value;

                    var bf = new Box<float>();
                    bf.Set(1.5f);
                    floatOutput[ThreadIndex.X] = bf.Value;
                }
            }
            """);

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, "Cuda");
        var failures = KernelDiagnostics.FindStatedFailures(run);

        Assert.Contains(failures, static f => f.Failure.Contains("GSH0066", StringComparison.Ordinal));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("CallMutatingOnLocal", "Mutate", nameof(ReceiverForm.Address), false, false)]
    [InlineData("CallReadOnlyOnLocal", "Read", nameof(ReceiverForm.Address), true, false)]
    [InlineData("CallMutatingOnElement", "Mutate", nameof(ReceiverForm.Address), false, false)]
    [InlineData("CallMutatingOnField", "Mutate", nameof(ReceiverForm.Address), false, false)]
    [InlineData("CallMutatingOnRefParam", "Mutate", nameof(ReceiverForm.Address), false, false)]
    [InlineData("CallMutatingOnRefReturn", "Mutate", nameof(ReceiverForm.Address), false, false)]
    [InlineData("CallReadOnlyOnInParam", "Read", nameof(ReceiverForm.Address), true, false)]
    [InlineData("CallReadOnlyOnRefReadOnlyLocal", "Read", nameof(ReceiverForm.Address), true, false)]
    [InlineData("CallReadOnlyOnRefReadOnlyReturn", "Read", nameof(ReceiverForm.Address), true, false)]
    [InlineData("CallMutatingOnInParam", "Mutate", nameof(ReceiverForm.Copy), false, false)]
    [InlineData("CallMutatingOnRefReadOnlyLocal", "Mutate", nameof(ReceiverForm.Copy), false, false)]
    [InlineData("CallMutatingOnReadonlyField", "Mutate", nameof(ReceiverForm.Copy), false, false)]
    [InlineData("CallMutatingOnFieldOfInParam", "Mutate", nameof(ReceiverForm.Copy), false, false)]
    [InlineData("CallMutatingOnRefReadOnlyReturn", "Mutate", nameof(ReceiverForm.Copy), false, false)]
    [InlineData("CallMutatingOnRvalue", "Mutate", nameof(ReceiverForm.Temporary), false, false)]
    [InlineData("CallReadOnlyOnRvalue", "Read", nameof(ReceiverForm.Temporary), true, false)]
    [InlineData("CallMutatingOnAutoProperty", "Mutate", nameof(ReceiverForm.Temporary), false, false)]
    [InlineData("CallParenthesizedMember", "Mutate", nameof(ReceiverForm.Address), false, false)]
    [InlineData("CallAssignReadOnly", "Read", nameof(ReceiverForm.Temporary), true, false)]
    [InlineData("CallCondReadOnly", "Read", nameof(ReceiverForm.Temporary), true, false)]
    [InlineData("CallCondChainMutating", "Mutate", nameof(ReceiverForm.Temporary), false, false)]
    [InlineData("CallAssignChainMutating", "Mutate", nameof(ReceiverForm.Temporary), false, false)]
    [InlineData("CallReadOnlyOnRefReadOnlyCond", "Read", nameof(ReceiverForm.Address), true, false)]
    [InlineData("CallMutatingOnRefReadOnlyCond", "Mutate", nameof(ReceiverForm.Copy), false, false)]
    [InlineData("CallMutatingOnMixedRefReadOnlyCond", "Mutate", nameof(ReceiverForm.Copy), false, false)]
    [InlineData("CallMutatingOnRefCond", "Mutate", nameof(ReceiverForm.Address), false, false)]
    [InlineData("CallMutatingOnRefReadOnlyCondField", "Mutate", nameof(ReceiverForm.Copy), false, false)]
    [InlineData("CallMutatingOnConstFalseRefCond", "Mutate", nameof(ReceiverForm.Address), false, false)]
    [InlineData("CallMutatingOnConstTrueRefCondSecond", "Mutate", nameof(ReceiverForm.Address), false, false)]
    [InlineData("CallMutatingOnConstTrueRefCondReadOnly", "Mutate", nameof(ReceiverForm.Copy), false, false)]
    [InlineData("CallReadOnlyOnConstFalseRefCond", "Read", nameof(ReceiverForm.Address), true, false)]
    [InlineData("CallMutatingOnInInlineArrayElement", "Mutate", nameof(ReceiverForm.Copy), false, false)]
    [InlineData("CallMutatingOnLocalInlineArrayElement", "Mutate", nameof(ReceiverForm.Address), false, false)]
    [InlineData("CallReadOnlyOnInInlineArrayElement", "Read", nameof(ReceiverForm.Address), true, false)]
    [InlineData("CallMutatingOnRvalueInlineArrayElement", "Mutate", nameof(ReceiverForm.Temporary), false, false)]
    [InlineData("CallMutatingOnCondInlineArrayElement", "Mutate", nameof(ReceiverForm.Temporary), false, false)]
    [InlineData("CallMutatingOnAssignInlineArrayElement", "Mutate", nameof(ReceiverForm.Temporary), false, false)]
    [InlineData("CallMutatingOnAutoPropertyInlineArrayElement", "Mutate", nameof(ReceiverForm.Temporary), false, false)]
    [InlineData("CallMutatingOnRvalueHolderField", "Mutate", nameof(ReceiverForm.Temporary), false, false)]
    [InlineData("CallMutatingOnRvalueHolderAuto", "Mutate", nameof(ReceiverForm.Temporary), false, false)]
    [InlineData("CallMutatingOnNewHolderField", "Mutate", nameof(ReceiverForm.Temporary), false, false)]
    [InlineData("CallMutatingOnSwitchHolderField", "Mutate", nameof(ReceiverForm.Temporary), false, false)]
    [InlineData("CallMutatingOnConstFalseInlineRefCond", "Mutate", nameof(ReceiverForm.Copy), false, false)]
    [InlineData("CallMutatingOnConstTrueRwFirstInlineRefCond", "Mutate", nameof(ReceiverForm.Copy), false, false)]
    [InlineData("CallMutatingOnConstFalseHolderInlineRefCond", "Mutate", nameof(ReceiverForm.Copy), false, false)]
    [InlineData("CallMutatingOnConstFalseWritableInlineRefCond", "Mutate", nameof(ReceiverForm.Address), false, false)]
    [InlineData("CallMutatingOnPointerArrow", "Mutate", nameof(ReceiverForm.Address), false, false)]
    [InlineData("CallReadOnlyOnPointerArrow", "Read", nameof(ReceiverForm.Address), true, false)]
    [InlineData("CallMutatingOnParenthesizedPointerArrow", "Mutate", nameof(ReceiverForm.Address), false, false)]
    [InlineData("CallMutatingOnCondPointerArrow", "Mutate", nameof(ReceiverForm.Address), false, false)]
    [InlineData("CallMutatingOnPrvaluePointerArrow", "Mutate", nameof(ReceiverForm.Address), false, false)]
    [InlineData("CallReadOnlyOnPrvaluePointerArrow", "Read", nameof(ReceiverForm.Address), true, false)]
    [InlineData("CallParenthesizedReceiver", "Mutate", nameof(ReceiverForm.Address), false, false)]
    [InlineData("CallParenthesizedPointerMember", "Mutate", nameof(ReceiverForm.Address), false, false)]
    public void ReceiverPlanner_InstanceCall_MatchesExpectedForm(
        string callerMethod,
        string calleeName,
        string expectedFormName,
        bool expectedReadOnlyCallee,
        bool expectedIsSelf)
    {
        var expectedForm = Enum.Parse<ReceiverForm>(expectedFormName);
        var (call, callee, model) = FindCall("PlannerCases", callerMethod, calleeName);

        var plan = ReceiverPlanner.PlanInstanceCall(call, callee, model, currentStruct: null, SelfForm.ReceiverParameter);

        Assert.Equal(expectedForm, plan.Form);
        Assert.Equal(expectedReadOnlyCallee, plan.ReadOnlyCallee);
        Assert.Equal(expectedIsSelf, plan.IsSelf);
        if (expectedForm is ReceiverForm.Copy or ReceiverForm.Temporary)
        {
            Assert.NotNull(plan.CopyType);
        }
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("PlannerStruct", "ImplicitThisMutating", "Mutate", nameof(SelfForm.ReceiverParameter), nameof(ReceiverForm.Self), false)]
    [InlineData("PlannerStruct", "ExplicitThisMutating", "Mutate", nameof(SelfForm.ReceiverParameter), nameof(ReceiverForm.Self), false)]
    [InlineData("PlannerStruct", "ReadOnlyCallsMutating", "Mutate", nameof(SelfForm.ReceiverParameter), nameof(ReceiverForm.Copy), true)]
    [InlineData("PlannerStruct", "ReadOnlyCallsReadOnly", "Read", nameof(SelfForm.ReceiverParameter), nameof(ReceiverForm.Self), false)]
    [InlineData("PlannerStruct", "MutatingGetterCallsMutating", "Mutate", nameof(SelfForm.MemberFunction), nameof(ReceiverForm.Self), false)]
    [InlineData("PlannerStruct", "ReadOnlyGetterCallsMutating", "Mutate", nameof(SelfForm.MemberFunction), nameof(ReceiverForm.Copy), true)]
    [InlineData("PlannerStruct", "ConstInferredGetterCallsNonReadOnly", "NonReadonlyPure", nameof(SelfForm.MemberFunction), nameof(ReceiverForm.Copy), true)]
    [InlineData("PlannerHolder", "ConstInferredGetterCallsFieldNonReadOnly", "NonReadonlyPure", nameof(SelfForm.MemberFunction), nameof(ReceiverForm.Copy), false)]
    [InlineData("PlannerOuterHolder", "ConstInferredGetterCallsNestedFieldNonReadOnly", "NonReadonlyPure", nameof(SelfForm.MemberFunction), nameof(ReceiverForm.Copy), false)]
    [InlineData("PlannerConstGetterInlineHolder", "ConstGetterInlineBuf", "NonReadonlyPure", nameof(SelfForm.MemberFunction), nameof(ReceiverForm.Copy), false)]
    [InlineData("PlannerConstGetterInlineHolder", "ConstGetterInlineFieldChain", "NonReadonlyPure", nameof(SelfForm.MemberFunction), nameof(ReceiverForm.Copy), false)]
    [InlineData("PlannerConstGetterInlineHolder", "ConstGetterRefCond", "NonReadonlyPure", nameof(SelfForm.MemberFunction), nameof(ReceiverForm.Copy), false)]
    [InlineData("PlannerConstGetterInlineHolder", "ConstGetterParenInlineBuf", "NonReadonlyPure", nameof(SelfForm.MemberFunction), nameof(ReceiverForm.Copy), false)]
    [InlineData("PlannerConstGetterInlineHolder", "ConstGetterConstFalseLocalFirst", "NonReadonlyPure", nameof(SelfForm.MemberFunction), nameof(ReceiverForm.Copy), false)]
    [InlineData("PlannerConstGetterInlineHolder", "ConstGetterConstFalseMemberFirst", "NonReadonlyPure", nameof(SelfForm.MemberFunction), nameof(ReceiverForm.Address), false)]
    [InlineData("PlannerConstGetterInlineHolder", "ConstGetterConstFalseInlineLocalFirst", "NonReadonlyPure", nameof(SelfForm.MemberFunction), nameof(ReceiverForm.Copy), false)]
    [InlineData("PlannerConstGetterInlineHolder", "ConstGetterConstFalseInlineMemberFirst", "NonReadonlyPure", nameof(SelfForm.MemberFunction), nameof(ReceiverForm.Address), false)]
    [InlineData("PlannerConstGetterInlineHolder", "ConstGetterRuntimeMixedMemberLocal", "NonReadonlyPure", nameof(SelfForm.MemberFunction), nameof(ReceiverForm.Copy), false)]
    [InlineData("PlannerPrimaryBufConstGetterHolder", "ConstGetterPrimaryInlineBuf", "NonReadonlyPure", nameof(SelfForm.MemberFunction), nameof(ReceiverForm.Copy), false)]
    [InlineData("PlannerStruct", ".ctor", "Mutate", nameof(SelfForm.ConstructorSelf), nameof(ReceiverForm.Self), false)]
    [InlineData("PlannerHolder", ".ctor", "Mutate", nameof(SelfForm.ConstructorSelf), nameof(ReceiverForm.Address), false)]
    [InlineData("PlannerParenThisHolder", ".ctor", "Mutate", nameof(SelfForm.ConstructorSelf), nameof(ReceiverForm.Address), false)]
    [InlineData("PlannerParenThisHolder", "MutateParenThisRw", "Mutate", nameof(SelfForm.ReceiverParameter), nameof(ReceiverForm.Address), false)]
    [InlineData("PlannerPtrHolder", "ConstGetterPtrNonReadOnly", "NonReadonlyPure", nameof(SelfForm.MemberFunction), nameof(ReceiverForm.Address), false)]
    public void ReceiverPlanner_SelfAndMemberForms_MatchExpectedForm(
        string callerType,
        string memberName,
        string calleeName,
        string selfFormName,
        string expectedFormName,
        bool expectedCopiedSelf)
    {
        var selfForm = Enum.Parse<SelfForm>(selfFormName);
        var expectedForm = Enum.Parse<ReceiverForm>(expectedFormName);
        var (call, callee, model) = FindCallInMember(callerType, memberName, calleeName);

        var plan = ReceiverPlanner.PlanInstanceCall(call, callee, model, currentStruct: null, selfForm);

        Assert.Equal(expectedForm, plan.Form);
        Assert.Equal(expectedCopiedSelf, plan.IsCopiedSelf);
        Assert.Equal(selfForm, plan.SelfForm);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("ExtRef", "ExtMutate", nameof(ReceiverForm.Address), false)]
    [InlineData("ExtInVariable", "ExtRead", nameof(ReceiverForm.Address), true)]
    [InlineData("ExtInRvalue", "ExtRead", nameof(ReceiverForm.Temporary), true)]
    [InlineData("ExtByValue", "ExtByVal", nameof(ReceiverForm.Value), true)]
    public void ReceiverPlanner_ExtensionCall_MatchesExpectedForm(
        string callerMethod,
        string calleeName,
        string expectedFormName,
        bool expectedReadOnlyCallee)
    {
        var expectedForm = Enum.Parse<ReceiverForm>(expectedFormName);
        var (call, callee, model) = FindCall("PlannerCases", callerMethod, calleeName);
        var receiverExpr = Assert.IsType<MemberAccessExpressionSyntax>(call.Expression).Expression;
        var extension = callee.ReducedFrom ?? callee;

        var plan = ReceiverPlanner.PlanExtensionCall(receiverExpr, extension, model);

        Assert.Equal(expectedForm, plan.Form);
        Assert.Equal(expectedReadOnlyCallee, plan.ReadOnlyCallee);
        Assert.True(plan.IsExtensionReceiver);
    }

    // T5: Address passes &s without spilling when arg mutates s; Copy spills auto __gsharp_seq_0 before the arg.
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void BeginReceiverCall_AddressVsCopyInterference_SpillsOnlyWhenCopied()
    {
        var compilation = CompileCaller();

        var (addrMethod, _, addrModel) = FindMethod(compilation, "PlannerCases", "OrderAddress");
        var addrText = MethodTranspiler.GenerateCudaSource(
            MethodEmissionPlan.Create(addrMethod, addrModel, isKernel: false),
            reportFailure: null,
            new StandardCppDialect(),
            out _);

        var (copyMethod, _, copyModel) = FindMethod(compilation, "PlannerCases", "OrderCopy");
        var copyText = MethodTranspiler.GenerateCudaSource(
            MethodEmissionPlan.Create(copyMethod, copyModel, isKernel: false),
            reportFailure: null,
            new StandardCppDialect(),
            out _);

        Assert.DoesNotContain("__gsharp_seq_", addrText, StringComparison.Ordinal);
        Assert.Contains("__gsharp_seq_0 = ", copyText, StringComparison.Ordinal);
        Assert.Contains("(&__gsharp_seq_0,__gsharp_seq_1)", copyText, StringComparison.Ordinal);
    }

    // T5 (Temporary): rvalue receiver reading s spills auto __gsharp_seq_0 before argument Bump(ref s).
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void BeginReceiverCall_TemporaryInterference_SpillsValueBeforeArgument()
    {
        var compilation = CompileCaller();

        var (tempMethod, _, tempModel) = FindMethod(compilation, "PlannerCases", "OrderTemporary");
        var tempText = MethodTranspiler.GenerateCudaSource(
            MethodEmissionPlan.Create(tempMethod, tempModel, isKernel: false),
            reportFailure: null,
            new StandardCppDialect(),
            out _);

        Assert.Contains("auto __gsharp_seq_0 = ", tempText, StringComparison.Ordinal);
        Assert.Contains("(&__gsharp_seq_0,__gsharp_seq_1)", tempText, StringComparison.Ordinal);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("CallAssignReadOnly", "static_cast<")]
    [InlineData("CallCondReadOnly", "static_cast<")]
    [InlineData("CallAssignExtReadAfter", "static_cast<")]
    [InlineData("CallCondChainMutating", "__gsharp_temp_mutable_address(__gsharp_ty_")]
    [InlineData("CallAssignChainMutating", "__gsharp_temp_mutable_address(__gsharp_ty_")]
    [InlineData("CallMutatingOnRvalueInlineArrayElement", "__gsharp_temp_mutable_address(__gsharp_ty_")]
    [InlineData("CallMutatingOnCondInlineArrayElement", "__gsharp_temp_mutable_address(__gsharp_ty_")]
    [InlineData("CallMutatingOnAssignInlineArrayElement", "__gsharp_temp_mutable_address(__gsharp_ty_")]
    [InlineData("CallMutatingOnAutoPropertyInlineArrayElement", "__gsharp_temp_mutable_address(__gsharp_ty_")]
    [InlineData("CallMutatingOnRvalueHolderField", "__gsharp_temp_mutable_address(__gsharp_ty_")]
    [InlineData("CallMutatingOnRvalueHolderAuto", "__gsharp_temp_mutable_address(__gsharp_ty_")]
    [InlineData("CallMutatingOnNewHolderField", "__gsharp_temp_mutable_address(__gsharp_ty_")]
    [InlineData("CallMutatingOnSwitchHolderField", "__gsharp_temp_mutable_address(__gsharp_ty_")]
    public void AppendReceiver_LvalueTemporaryReceivers_MaterializeValueCopy(string methodName, string expectedSnippet)
    {
        var compilation = CompileCaller();
        var (method, _, model) = FindMethod(compilation, "PlannerCases", methodName);

        var text = MethodTranspiler.GenerateCudaSource(
            MethodEmissionPlan.Create(method, model, isKernel: false),
            reportFailure: null,
            new StandardCppDialect(),
            out _);

        Assert.Contains(expectedSnippet, text, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AppendReceiver_DirectPrvalueReceiver_KeepsUnwrappedTempMutableAddress()
    {
        var compilation = CompileCaller();
        var (method, _, model) = FindMethod(compilation, "PlannerCases", "CallMutatingOnRvalue");

        var text = MethodTranspiler.GenerateCudaSource(
            MethodEmissionPlan.Create(method, model, isKernel: false),
            reportFailure: null,
            new StandardCppDialect(),
            out _);

        Assert.Contains("__gsharp_temp_mutable_address(__gsharp_fn_", text, StringComparison.Ordinal);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("OrderTemporaryInlineArrayElement")]
    [InlineData("OrderTemporaryHolderField")]
    public void BeginReceiverCall_SpilledLvalueTemporary_CopiesIntoSeqAndPassesAddress(string methodName)
    {
        var compilation = CompileCaller();
        var (method, _, model) = FindMethod(compilation, "PlannerCases", methodName);

        var text = MethodTranspiler.GenerateCudaSource(
            MethodEmissionPlan.Create(method, model, isKernel: false),
            reportFailure: null,
            new StandardCppDialect(),
            out _);

        Assert.Contains("auto __gsharp_seq_0 = ", text, StringComparison.Ordinal);
        Assert.Contains("(&__gsharp_seq_0,__gsharp_seq_1)", text, StringComparison.Ordinal);
        Assert.Contains("return __gsharp_fn_ReceiverPlannerFixture__PlannerStruct__Mutate(&__gsharp_seq_0,__gsharp_seq_1);",
            text, StringComparison.Ordinal);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("CallMutatingOnLocalInlineArrayElement", "Mutate", false)]
    [InlineData("CallMutatingOnRvalueInlineArrayElement", "Mutate", true)]
    [InlineData("CallMutatingOnCondInlineArrayElement", "Mutate", true)]
    [InlineData("CallMutatingOnAssignInlineArrayElement", "Mutate", true)]
    [InlineData("CallMutatingOnAutoPropertyInlineArrayElement", "Mutate", true)]
    public void NeedsTemporaryValueCopy_InlineArrayElements_MatchesMutability(
        string callerMethod,
        string calleeName,
        bool expectedNeedsCopy)
    {
        var (call, _, model) = FindCall("PlannerCases", callerMethod, calleeName);
        var receiverExpr = Assert.IsType<MemberAccessExpressionSyntax>(call.Expression).Expression;

        Assert.Equal(expectedNeedsCopy,
            Kassyi.GSharp.Compiler.Transpiler.Helpers.Calls.RefSyntaxHelper.NeedsTemporaryValueCopy(receiverExpr, model));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void ReceiverParameter_ExplicitInterfaceImplementation_KeepsUnsupported()
    {
        var compilation = CompileCaller();
        var (_, symbol, _) = FindMethod(compilation, "PlannerExplicitImpl", "Ping");

        var plan = ReceiverParameter.Plan(symbol, MethodEmissionKind.DeviceFunction);

        Assert.False(plan.HasParameter);
        Assert.True(plan.FailsUnsupported);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void NonNullProof_StructInstanceDeviceFunctionParameter_ProvesNonNullInCallee()
    {
        var compilation = RoslynTestUtility.CreateCompilation("""
            #nullable enable
            using Kassyi.GSharp.Core;

            public struct ArrWorker
            {
                public int Bias;

                [GpuDeviceFunction]
                public int SumArray(int[] arr) => arr[0] + Bias;

                [GpuDeviceFunction]
                public int WrittenArray(int[] arr, int[] other)
                {
                    arr = other;
                    return arr[0];
                }
            }
            """);

        var (sumDecl, sumSym, sumModel) = FindMethod(compilation, "ArrWorker", "SumArray");
        var (_, writtenSym, _) = FindMethod(compilation, "ArrWorker", "WrittenArray");

        Assert.True(NonNullProof.IsCheckedParameter(
            sumSym.Parameters[0], compilation));
        Assert.False(NonNullProof.IsCheckedParameter(
            writtenSym.Parameters[0], compilation));

        var emitted = MethodTranspiler.GenerateCudaSource(
            MethodEmissionPlan.Create(sumDecl, sumModel, isKernel: false),
            reportFailure: null,
            new StandardCppDialect(),
            out _);

        Assert.DoesNotContain("__gsharp_array_at", emitted, StringComparison.Ordinal);
        Assert.Contains("[0]", emitted, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ConstConditionRefConditional_EmitsOnlySelectedBranchWithoutConstPollution()
    {
        var compilation = CompileCaller();
        var (method, _, model) = FindMethod(compilation, "PlannerCases", "CallMutatingOnConstFalseRefCond");

        var emitted = MethodTranspiler.GenerateCudaSource(
            MethodEmissionPlan.Create(method, model, isKernel: false),
            reportFailure: null,
            new StandardCppDialect(),
            out _);

        Assert.Contains("__gsharp_fn_ReceiverPlannerFixture__PlannerStruct__Mutate(&((*&(*", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("?", emitted, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ParenthesizedThisField_TranspileAsByRefArgument_EmitsAddressNotTempAddress()
    {
        var compilation = CompileCaller();
        var (rwMethod, _, rwModel) = FindMethod(compilation, "PlannerParenThisHolder", "MutateParenThisRw");

        var rwEmitted = MethodTranspiler.GenerateCudaSource(
            MethodEmissionPlan.Create(rwMethod, rwModel, isKernel: false),
            reportFailure: null,
            new StandardCppDialect(),
            out _);

        Assert.Contains("&((", rwEmitted, StringComparison.Ordinal);
        Assert.DoesNotContain("__gsharp_temp_address", rwEmitted, StringComparison.Ordinal);

        var (call, _, model) = FindCallInMember("PlannerParenThisHolder", ".ctor", "Mutate");
        var receiverExpr = Assert.IsType<MemberAccessExpressionSyntax>(call.Expression).Expression;
        Assert.True(Kassyi.GSharp.Compiler.Transpiler.Helpers.Calls.RefSyntaxHelper.IsCppLValue(receiverExpr, model));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void InInlineArrayElement_MutatingCall_EmitsDefensiveCopy()
    {
        var compilation = CompileCaller();
        var (method, _, model) = FindMethod(compilation, "PlannerCases", "CallMutatingOnInInlineArrayElement");

        var emitted = MethodTranspiler.GenerateCudaSource(
            MethodEmissionPlan.Create(method, model, isKernel: false),
            reportFailure: null,
            new StandardCppDialect(),
            out _);

        Assert.Contains("__gsharp_temp_mutable_address(", emitted, StringComparison.Ordinal);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("CallReadAfterOnAssignInlineArrayElement")]
    [InlineData("CallExtReadAfterOnAssignInlineArrayElement")]
    [InlineData("CallReadAfterOnAutoPropertyInlineArrayElement")]
    [InlineData("CallReadAfterOnCondInlineArrayElement")]
    [InlineData("CallReadAfterOnAssignInlineArrayFieldChain")]
    public void AppendReceiver_ReadOnlyInlineArrayTemporaryReceivers_EmitTempAddressCopy(string methodName)
    {
        var compilation = CompileCaller();
        var (method, _, model) = FindMethod(compilation, "PlannerCases", methodName);

        var emitted = MethodTranspiler.GenerateCudaSource(
            MethodEmissionPlan.Create(method, model, isKernel: false),
            reportFailure: null,
            new StandardCppDialect(),
            out _);

        Assert.Contains("__gsharp_temp_address<", emitted, StringComparison.Ordinal);
        Assert.Contains("static_cast<", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("&(", emitted, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void BeginReceiverCall_SpilledReadOnlyTemporaryInlineArray_CopiesIntoSeqAndPassesAddress()
    {
        var compilation = CompileCaller();
        var (method, _, model) = FindMethod(compilation, "PlannerCases", "OrderReadAfterAssignInlineArrayElement");

        var emitted = MethodTranspiler.GenerateCudaSource(
            MethodEmissionPlan.Create(method, model, isKernel: false),
            reportFailure: null,
            new StandardCppDialect(),
            out _);

        Assert.Contains("auto __gsharp_seq_0 = ", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("auto&& __gsharp_seq_0", emitted, StringComparison.Ordinal);
        Assert.Contains("(&__gsharp_seq_0,", emitted, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ConstInferredGetter_SubobjectAndRefCondReceivers_EmitExpectedCopyOrAddress()
    {
        var compilation = CompileCaller();
        var holderType = compilation.GetTypeByMetadataName("ReceiverPlannerFixture.PlannerConstGetterInlineHolder")
                         ?? throw new InvalidOperationException("PlannerConstGetterInlineHolder not found.");
        var primaryType = compilation.GetTypeByMetadataName("ReceiverPlannerFixture.PlannerPrimaryBufConstGetterHolder")
                          ?? throw new InvalidOperationException("PlannerPrimaryBufConstGetterHolder not found.");
        var tree = holderType.DeclaringSyntaxReferences[0].SyntaxTree;
        var model = compilation.GetSemanticModel(tree);

        var holderCollector = StructTranspiler.RenderStandalone(holderType, model, new StandardCppDialect(), _ => { });
        var holderPart = Assert.Single(holderCollector.Discovered, static p => p.Type.Name == "PlannerConstGetterInlineHolder");
        Assert.False(holderPart.Failed);

        var primaryCollector = StructTranspiler.RenderStandalone(primaryType, model, new StandardCppDialect(), _ => { });
        var primaryPart = Assert.Single(primaryCollector.Discovered, static p => p.Type.Name == "PlannerPrimaryBufConstGetterHolder");
        Assert.False(primaryPart.Failed);

        Assert.Contains("__gsharp_mb_ConstGetterConstFalseInlineLocalFirst() const", holderPart.Text, StringComparison.Ordinal);
        Assert.Contains("__gsharp_mb_ConstGetterConstFalseInlineMemberFirst() const", holderPart.Text, StringComparison.Ordinal);
        Assert.Contains(
            "__gsharp_temp_mutable_address(__gsharp_ty_ReceiverPlannerFixture__PlannerStruct(__gsharp_mb_RwBuf[0]))",
            holderPart.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "__gsharp_temp_mutable_address(__gsharp_ty_ReceiverPlannerFixture__PlannerStruct(__gsharp_mb_Mb[0].__gsharp_mb_Rw))",
            holderPart.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "__gsharp_temp_mutable_address(__gsharp_ty_ReceiverPlannerFixture__PlannerStruct(((*(__gsharp_mb_Flag?&__gsharp_mb_Rw1:&__gsharp_mb_Rw2)))))",
            holderPart.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "__gsharp_temp_mutable_address(__gsharp_ty_ReceiverPlannerFixture__PlannerStruct(((__gsharp_mb_RwBuf[0]))))",
            holderPart.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "__gsharp_temp_mutable_address(__gsharp_ty_ReceiverPlannerFixture__PlannerStruct(((*&__gsharp_mb_Rw1))))",
            holderPart.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "__gsharp_temp_mutable_address(__gsharp_ty_ReceiverPlannerFixture__PlannerStruct(((*&__gsharp_mb_RwBuf))[0]))",
            holderPart.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "__gsharp_temp_mutable_address(__gsharp_ty_ReceiverPlannerFixture__PlannerStruct(__gsharp_mb_buf[0]))",
            primaryPart.Text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("&__gsharp_mb_RwBuf[0]", holderPart.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("&__gsharp_mb_Mb[0].__gsharp_mb_Rw", holderPart.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("&__gsharp_mb_buf[0]", primaryPart.Text, StringComparison.Ordinal);
        Assert.Contains("&((*&__gsharp_id_0_local))", holderPart.Text, StringComparison.Ordinal);
        Assert.Contains("&((*&__gsharp_id_0_localBuf))[0]", holderPart.Text, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void PointerArrowCalls_EmitDirectPointerWithoutTempMutableAddress()
    {
        var compilation = CompileCaller();

        string[] directMethods =
        [
            "CallMutatingOnPointerArrow",
            "CallMutatingOnParenthesizedPointerArrow",
            "CallMutatingOnCondPointerArrow",
            "CallMutatingOnPrvaluePointerArrow",
            "OrderPointerArrow",
        ];
        foreach (var methodName in directMethods)
        {
            var (method, _, model) = FindMethod(compilation, "PlannerCases", methodName);
            var text = MethodTranspiler.GenerateCudaSource(
                MethodEmissionPlan.Create(method, model, isKernel: false),
                reportFailure: null,
                new StandardCppDialect(),
                out _);

            var bodyIndex = text.IndexOf($"{methodName}(", StringComparison.Ordinal);
            Assert.True(bodyIndex >= 0, $"Method '{methodName}' definition missing from emitted C++.");
            Assert.True(
                text.IndexOf("__gsharp_fn_ReceiverPlannerFixture__PlannerStruct__Mutate(", bodyIndex, StringComparison.Ordinal) >= 0,
                $"Expected PlannerStruct.Mutate call in '{methodName}'.");
            Assert.Equal(-1, text.IndexOf("__gsharp_temp_mutable_address", bodyIndex, StringComparison.Ordinal));
        }

        var (prvalueOrder, _, prvalueModel) = FindMethod(compilation, "PlannerCases", "OrderPrvaluePointerArrow");
        var prvalueText = MethodTranspiler.GenerateCudaSource(
            MethodEmissionPlan.Create(prvalueOrder, prvalueModel, isKernel: false),
            reportFailure: null,
            new StandardCppDialect(),
            out _);
        var prvalueBodyIndex = prvalueText.IndexOf("__gsharp_fn_ReceiverPlannerFixture__PlannerCases__OrderPrvaluePointerArrow(",
            StringComparison.Ordinal);
        Assert.True(prvalueBodyIndex >= 0);
        Assert.Equal(-1, prvalueText.IndexOf("__gsharp_temp_mutable_address", prvalueBodyIndex, StringComparison.Ordinal));
        Assert.Contains("auto __gsharp_seq_0 = __gsharp_fn_", prvalueText, StringComparison.Ordinal);
        Assert.Contains("(__gsharp_seq_0,__gsharp_seq_1)", prvalueText, StringComparison.Ordinal);

        var (reassignOrder, _, reassignModel) = FindMethod(compilation, "PlannerCases", "OrderPointerReassignArrow");
        var reassignText = MethodTranspiler.GenerateCudaSource(
            MethodEmissionPlan.Create(reassignOrder, reassignModel, isKernel: false),
            reportFailure: null,
            new StandardCppDialect(),
            out _);
        var reassignBodyIndex = reassignText.IndexOf("__gsharp_fn_ReceiverPlannerFixture__PlannerCases__OrderPointerReassignArrow(",
            StringComparison.Ordinal);
        Assert.True(reassignBodyIndex >= 0);
        Assert.Equal(-1, reassignText.IndexOf("__gsharp_temp_mutable_address", reassignBodyIndex, StringComparison.Ordinal));
        Assert.Contains("auto __gsharp_seq_0 = ", reassignText, StringComparison.Ordinal);
        Assert.DoesNotContain("auto&& __gsharp_seq_0", reassignText, StringComparison.Ordinal);
        Assert.Contains("(__gsharp_seq_0,__gsharp_seq_1)", reassignText, StringComparison.Ordinal);

        var ptrHolderType = compilation.GetTypeByMetadataName("ReceiverPlannerFixture.PlannerPtrHolder")
                            ?? throw new InvalidOperationException("PlannerPtrHolder not found.");
        var ptrTree = ptrHolderType.DeclaringSyntaxReferences[0].SyntaxTree;
        var ptrCollector = StructTranspiler.RenderStandalone(
            ptrHolderType, compilation.GetSemanticModel(ptrTree), new StandardCppDialect(), _ => { });
        var ptrPart = Assert.Single(ptrCollector.Discovered, static p => p.Type.Name == "PlannerPtrHolder");
        Assert.False(ptrPart.Failed);
        Assert.Contains("__gsharp_mb_ConstGetterPtrNonReadOnly() const", ptrPart.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("__gsharp_temp_mutable_address", ptrPart.Text, StringComparison.Ordinal);
        Assert.Contains("__gsharp_fn_ReceiverPlannerFixture__PlannerStruct__NonReadonlyPure(__gsharp_mb_Ptr)",
            ptrPart.Text, StringComparison.Ordinal);

        var (_, helperSym, _) = FindMethod(compilation, "PlannerCases", "WriteThroughPointerHelper");
        Assert.Equal(Kassyi.GSharp.Compiler.Analysis.DeviceFunctionEffect.Memory,
            Kassyi.GSharp.Compiler.Analysis.DeviceFunctionEffects.Classify(helperSym, compilation));

        var (aliasOrder, _, aliasModel) = FindMethod(compilation, "PlannerCases", "OrderPointerWriteBeforeDereference");
        var aliasBinary = Assert.IsType<BinaryExpressionSyntax>(aliasOrder.ExpressionBody?.Expression);
        Assert.True(Kassyi.GSharp.Compiler.Analysis.ExpressionEffects.Of(aliasBinary.Left, aliasModel).Writes.Memory);
        var aliasText = MethodTranspiler.GenerateCudaSource(
            MethodEmissionPlan.Create(aliasOrder, aliasModel, isKernel: false),
            reportFailure: null,
            new StandardCppDialect(),
            out _);
        Assert.Contains("__gsharp_seq_0 = __gsharp_fn_ReceiverPlannerFixture__PlannerStruct__Mutate(",
            aliasText, StringComparison.Ordinal);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("ReadOnlyCallsMutating")]
    [InlineData("ReadOnlyCallsExplicitMutating")]
    public void ReceiverPlanner_SpilledThisTemporary_SetsIsSelfFalse(string callerMethod)
    {
        var (call, callee, model) = FindCall("PlannerStruct", callerMethod, "Mutate");
        var op = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.Operations.IInvocationOperation>(
            model.GetSafeOperation(call));
        Assert.IsAssignableFrom<Microsoft.CodeAnalysis.Operations.IInstanceReferenceOperation>(op.Instance);

        var spilledExpression = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.IdentifierName("__gsharp_arg_temp_0"),
            SyntaxFactory.IdentifierName("Mutate"));
        var rewrittenCall = call
            .WithExpression(spilledExpression)
            .WithAdditionalAnnotations(new SyntaxAnnotation(
                LoweringAnnotations.OriginalSpanAnnotationKind,
                LoweringAnnotations.FormatOriginalSpanData(call)));

        var plan = ReceiverPlanner.PlanInstanceCall(
            rewrittenCall,
            callee,
            model,
            currentStruct: null,
            SelfForm.ReceiverParameter);

        Assert.Equal(ReceiverForm.Copy, plan.Form);
        Assert.False(plan.IsSelf);
        var receiverIdentifier = Assert.IsType<IdentifierNameSyntax>(plan.Expression);
        Assert.Equal("__gsharp_arg_temp_0", receiverIdentifier.Identifier.ValueText);
    }
}

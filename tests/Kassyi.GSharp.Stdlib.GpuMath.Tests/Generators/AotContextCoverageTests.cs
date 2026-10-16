using System.Reflection;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.Generators;

[TestTarget(ExecutionTarget.GpuExecution)]
public class AotContextCoverageTests
{
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void ExecuteAllAotContextMarshalMethods_ForCoverage()
    {
        CudaCapability.EnsureGpuAvailable();

        using var cudaCtxScope = new GpuKernelLoader.CudaContextScope();

        var assembly = typeof(GpuBigIntMath).Assembly;
        var aotContextTypes = assembly.GetTypes()
            .Where(t => t.Name == "AotContexts" && t.IsClass)
            .ToList();

        int invokedCount = 0;

        foreach (var aotCtx in aotContextTypes)
        {
            var methods = aotCtx.GetMethods(BindingFlags.Public | BindingFlags.Static);
            var marshalMethods = methods.Where(m => m.Name.StartsWith("Marshal_", StringComparison.Ordinal)).ToList();

            foreach (var m in marshalMethods)
            {
                var parameters = m.GetParameters();
                if (parameters.Length < 3) continue;

                var hostType = parameters[0].ParameterType.GetElementType(); // ref T
                var nativeType = parameters[1].ParameterType.GetElementType();

                if (hostType == null || nativeType == null) continue;

                object hostObj = RuntimeHelpers.GetUninitializedObject(hostType);
                object nativeObj = RuntimeHelpers.GetUninitializedObject(nativeType);

                // Set array properties to empty arrays to trigger array paths safely
                foreach (var field in hostType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (field.FieldType.IsArray)
                    {
                        var elemType = field.FieldType.GetElementType();
                        if (elemType != null)
                        {
                            field.SetValue(hostObj, Array.CreateInstance(elemType, 0));
                        }
                    }
                    else if (field.FieldType.Name == "GSharpArrayProxy")
                    {
                        var proxyInstance = RuntimeHelpers.GetUninitializedObject(field.FieldType);
                        var arrProp = field.FieldType.GetProperty("Array");
                        if (arrProp != null)
                        {
                            var elemType = arrProp.PropertyType.GetElementType();
                            if (elemType != null)
                            {
                                arrProp.SetValue(proxyInstance, Array.CreateInstance(elemType, 0));
                            }
                        }
                        field.SetValue(hostObj, proxyInstance);
                    }
                }

                var isDevicePtr = false;
                var allocatedPtrs = new List<IntPtr>();
                IntPtr hStream = IntPtr.Zero;

                object[] args = parameters.Length == 5
                    ? [hostObj, nativeObj, isDevicePtr, allocatedPtrs, hStream]
                    : [hostObj, nativeObj, isDevicePtr, hStream];

                try
                {
                    m.Invoke(null, args);
                    invokedCount++;
                }
                finally
                {
                    foreach (var ptr in allocatedPtrs)
                    {
                        if (ptr != IntPtr.Zero)
                        {
                            CudaDriverApi.cuMemFree_v2(ptr);
                        }
                    }
                }
            }
        }

        Assert.True(invokedCount > 0, "Expected at least one Marshal_ method in AotContexts to be invoked.");
    }
}

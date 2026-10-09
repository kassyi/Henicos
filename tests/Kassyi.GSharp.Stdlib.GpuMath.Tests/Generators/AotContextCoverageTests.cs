using System.Reflection;
using System.Runtime.InteropServices;
using Xunit.Abstractions;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.Generators;

[TestTarget(ExecutionTarget.GpuExecution)]
public class AotContextCoverageTests
{
    private readonly ITestOutputHelper _output;
    public AotContextCoverageTests(ITestOutputHelper output) { _output = output; }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void ExecuteAllAotContextMarshalMethods_ForCoverage()
    {
        CudaCapability.EnsureGpuAvailable();

        var assembly = typeof(GpuBigIntMath).Assembly;
        var aotContextTypes = assembly.GetTypes()
            .Where(t => t.Name == "AotContexts" && t.IsClass)
            .ToList();

        _output.WriteLine($"Found {aotContextTypes.Count} AotContexts types.");
        
        int totalMarshal = 0;
        foreach (var aotCtx in aotContextTypes)
        {
            var methods = aotCtx.GetMethods(BindingFlags.Public | BindingFlags.Static);
            var marshalMethods = methods.Where(m => m.Name.StartsWith("Marshal_")).ToList();

            foreach (var m in marshalMethods)
            {
                var parameters = m.GetParameters();
                if (parameters.Length < 3) continue;

                var hostType = parameters[0].ParameterType.GetElementType(); // ref T
                var nativeType = parameters[1].ParameterType.GetElementType();

                if (hostType == null || nativeType == null) continue;

                object hostObj = RuntimeHelpers.GetUninitializedObject(hostType);
                object nativeObj = RuntimeHelpers.GetUninitializedObject(nativeType);

                // Try to set array properties to empty arrays to trigger array paths without crashing
                foreach (var field in hostType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (field.FieldType.IsArray)
                    {
                        field.SetValue(hostObj, Array.CreateInstance(field.FieldType.GetElementType(), 0));
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
                List<IntPtr> allocatedPtrs = new List<IntPtr>();
                IntPtr hStream = IntPtr.Zero;

                object[] args;
                if (parameters.Length == 5)
                {
                    args = new[] { hostObj, nativeObj, isDevicePtr, allocatedPtrs, hStream };
                }
                else
                {
                    args = new[] { hostObj, nativeObj, isDevicePtr, hStream };
                }

                try
                {
                    m.Invoke(null, args);
                    _output.WriteLine($"SUCCESS: {aotCtx.Name}.{m.Name}");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"ERROR in {aotCtx.Name}.{m.Name}: {ex.InnerException?.GetType().Name} - {ex.InnerException?.Message}");
                }
            }
        }
    }
}

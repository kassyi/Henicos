using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.TestCases.BclIntrinsicMap;

// Maps exactly Math.Abs(double)
[assembly: BclIntrinsicMap("System.Math", "Abs", ["System.Double"], "user_fabs")]
// Maps Math.Round(double) and none of the overloads that take two arguments
[assembly: BclIntrinsicMap("System.Math", "Round", ["System.Double"], "user_round")]
// An attribute of the same shape that is not BclIntrinsicMap maps nothing
[assembly: FakeMap("System.Math", "Sqrt", ["System.Double"], "fake_sqrt")]
// A parameter type that does not exist maps nothing
[assembly: BclIntrinsicMap("System.Math", "Ceiling", ["System.Nope"], "user_ceil")]
// A null parameter type name maps nothing
[assembly: BclIntrinsicMap("System.Math", "Floor", [null], "user_floor")]
// A null second parameter type name maps nothing, although the first one matches
[assembly: BclIntrinsicMap("System.Math", "Max", ["System.Double", null], "user_max")]
// An empty parameter list on BitOperations: no overload of IsPow2 takes no argument
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "IsPow2", [], "user_ispow2_none")]
// A native-sized signed parameter on BitOperations takes the cast of the platform
[assembly: BclIntrinsicMap("System.Numerics.BitOperations", "IsPow2", ["System.IntPtr"], "user_ispow2_nint")]

namespace Kassyi.GSharp.Tests.TestCases.BclIntrinsicMap;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class FakeMapAttribute(string bclTypeFullName, string methodName, string[] parameterTypeFullNames, string cudaIntrinsicName) : Attribute
{
    public string BclTypeFullName { get; } = bclTypeFullName;
    public string MethodName { get; } = methodName;
    public string[] ParameterTypeFullNames { get; } = parameterTypeFullNames;
    public string CudaIntrinsicName { get; } = cudaIntrinsicName;
}

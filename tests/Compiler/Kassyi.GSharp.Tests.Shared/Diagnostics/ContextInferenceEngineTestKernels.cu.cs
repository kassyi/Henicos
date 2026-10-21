using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

public struct ContextInferenceTestStruct 
{ 
    public ContextInferenceTestStruct(int dummy) 
    { 
        Gpu.SyncGrid(); 
    } 

    public static ContextInferenceTestStruct operator +(ContextInferenceTestStruct a, ContextInferenceTestStruct b) 
    { 
        Gpu.SyncGrid(); 
        return default; 
    }

    public int Prop 
    { 
        set 
        { 
            Gpu.SyncGrid(); 
        } 
    }
}

public class ContextInferenceTestClass 
{
    public void TestObjectCreation()
    {
        var x = new ContextInferenceTestStruct(1);
    }

    public void TestBinary()
    {
        var a = default(ContextInferenceTestStruct);
        var b = default(ContextInferenceTestStruct);
        var c = a + b;
    }

    public void TestAssignmentProperty()
    {
        var a = default(ContextInferenceTestStruct);
        a.Prop = 10;
    }
    
    public void TestAssignmentOperator()
    {
        var a = default(ContextInferenceTestStruct);
        var b = default(ContextInferenceTestStruct);
        a += b;
    }
}

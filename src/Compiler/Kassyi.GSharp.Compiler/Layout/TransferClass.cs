namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>How a user struct transfers between the C# host and the native device.</summary>
public enum TransferClass
{
    Identical,
    Convert,
    Unsupported
}

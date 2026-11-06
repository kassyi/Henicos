namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>A mistake in how the tool was called (a wrong option, an unknown revision, a missing file); the exit code is 2.</summary>
internal sealed class UsageException(string message) : Exception(message);

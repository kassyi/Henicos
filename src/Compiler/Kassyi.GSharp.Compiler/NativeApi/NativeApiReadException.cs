namespace Kassyi.GSharp.Compiler.NativeApi;

internal sealed class NativeApiReadException(NativeApiReadErrorKind kind, int line, string message) : Exception(message)
{
    public NativeApiReadError ToError() => new(kind, line, Message);
}

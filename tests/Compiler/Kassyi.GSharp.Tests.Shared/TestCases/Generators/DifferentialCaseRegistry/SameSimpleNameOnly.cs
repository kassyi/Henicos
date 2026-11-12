namespace Other
{
    // Only a type named DifferentialCase in another namespace: the compilation has no case type
    public sealed record DifferentialCase(string Name);

    public class Holder
    {
        internal static readonly DifferentialCase Look = new("Holder.Look");
    }
}

// The helper class Stryker.NET adds to the compilation it mutates, in a namespace named Stryker and 15 random characters.
// MutantSwitchRemoverTests compiles MutantSwitchCases.cs with this file (a mutated compilation) and without it
namespace StrykerAbCdEfGhIjKlMnO
{
    public static class MutantControl
    {
        public static bool IsActive(int id) => false;
    }
}

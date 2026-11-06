// The inputs of MutantSwitchRemoverTests: each <Shape>_Instrumented method is written as Stryker.NET 5.0.0 writes a
// mutated method (ConditionalInstrumentationEngine, IfInstrumentationEngine, MutationStore), and <Shape>_Original is the
// method before Stryker mutated it. The switches bind to Stryker's helper class only in a compilation that also holds
// StrykerMutantControl.cs

using System;

// An IsActive that is not Stryker's: a MutantControl in another namespace
namespace Contoso
{
    public static class MutantControl
    {
        public static bool IsActive(int id) => id > 0;
    }
}

// An IsActive that is not Stryker's: another class in a namespace that starts with Stryker
namespace StrykerLookalike
{
    public static class Toggle
    {
        public static bool IsActive(int id) => id > 0;
    }
}

namespace MutantSwitchCases
{
    public static class MutantSwitchCases
    {
        private static void Accumulate(ref int sum, int value) => sum += value;

        private static void Call(int[] output) => output[0]++;

        private static void Other(int[] output) => output[1]++;

        // (<switch> ? <mutated> : <original>)
        public static int Expression_Instrumented(int a, int b)
        {
            return (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(0) ? a - b : a + b);
        }

        public static int Expression_Original(int a, int b)
        {
            return a + b;
        }

        // Two mutants of one expression: the later one wraps the earlier one
        public static int ChainedExpressions_Instrumented(int a, int b)
        {
            return (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(2) ? a * b : (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(1) ? a - b : a + b));
        }

        public static int ChainedExpressions_Original(int a, int b)
        {
            return a + b;
        }

        // A mutant of a part of the original branch of another mutant
        public static int NestedExpressions_Instrumented(int a, int b, int c)
        {
            return (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(4) ? a - b * c : a + (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(3) ? b / c : b * c));
        }

        public static int NestedExpressions_Original(int a, int b, int c)
        {
            return a + b * c;
        }

        // if (<switch>) { <mutated> } else { <original> }: a removed statement among others
        public static int Statement_Instrumented(int a)
        {
            int sum = 0;
            if (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(5))
            {
                ;
            }
            else
            {
                Accumulate(ref sum, a);
            }
            return sum;
        }

        public static int Statement_Original(int a)
        {
            int sum = 0;
            Accumulate(ref sum, a);
            return sum;
        }

        // Two mutants of one statement
        public static void ChainedStatements_Instrumented(int[] output)
        {
            output[2] = 1;
            if (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(7))
            {
                ;
            }
            else
            {
                if (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(6))
                {
                    Other(output);
                }
                else
                {
                    Call(output);
                }
            }
            output[3] = 1;
        }

        public static void ChainedStatements_Original(int[] output)
        {
            output[2] = 1;
            Call(output);
            output[3] = 1;
        }

        // A statement mutant of an embedded statement
        public static void EmbeddedStatement_Instrumented(int[] output, int a)
        {
            if (a > 0)
                if (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(8))
                {
                    ;
                }
                else
                {
                    Call(output);
                }
        }

        public static void EmbeddedStatement_Original(int[] output, int a)
        {
            if (a > 0)
                Call(output);
        }

        // { if (<switch>) { } else <original block> }: the method body removed
        public static void BodyBlock_Instrumented(int[] output, int a)
        {
            if (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(9))
            {
            }
            else
            {
                output[0] = a;
                output[1] = a + 1;
            }
        }

        public static void BodyBlock_Original(int[] output, int a)
        {
            output[0] = a;
            output[1] = a + 1;
        }

        // A removed block of one statement keeps its braces
        public static void SingleStatementBlock_Instrumented(int[] output, int a)
        {
            if (a > 0)
            {
                if (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(10))
                {
                }
                else
                {
                    output[0] = a;
                }
            }
        }

        public static void SingleStatementBlock_Original(int[] output, int a)
        {
            if (a > 0)
            {
                output[0] = a;
            }
        }

        // Two mutants of one block: the block mutant wraps the statement mutant of its only statement
        public static void ChainedBlock_Instrumented(int[] output, int a)
        {
            if (a > 0)
            {
                if (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(12))
                {
                }
                else
                {
                    if (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(11))
                    {
                        ;
                    }
                    else
                    {
                        Call(output);
                    }
                }
            }
        }

        public static void ChainedBlock_Original(int[] output, int a)
        {
            if (a > 0)
            {
                Call(output);
            }
        }

        // A switch in a lambda and in the condition of a statement switch's original
        public static int Lambda_Instrumented(int a)
        {
            Func<int, int> f = x => (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(13) ? x - a : x + a);
            if (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(15))
            {
                ;
            }
            else
            {
                f((StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(14) ? a - 1 : a + 1));
            }
            return f(a);
        }

        public static int Lambda_Original(int a)
        {
            Func<int, int> f = x => x + a;
            f(a + 1);
            return f(a);
        }

        // Neither IsActive is Stryker's
        public static int ForeignSwitches(int[] output, int a, int b)
        {
            if (StrykerLookalike.Toggle.IsActive(1))
            {
                Call(output);
            }
            else
            {
                Other(output);
            }
            return (Contoso.MutantControl.IsActive(1) ? a - b : a + b);
        }

        public static int NoSwitch(int a, int b)
        {
            if (a > b)
            {
                return a - b;
            }
            return a + b;
        }

        public static int ExpressionBody_Instrumented(int a, int b) => (StrykerAbCdEfGhIjKlMnO.MutantControl.IsActive(16) ? a - b : a + b);

        public static int ExpressionBody_Original(int a, int b) => a + b;
    }
}

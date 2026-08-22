using System;
using System.Collections.Generic;

namespace San9AutoDomestic.Core.Tests
{
    internal sealed class TestCase
    {
        public TestCase(string name, Action body)
        {
            Name = name;
            Body = body;
        }

        public string Name { get; private set; }

        public Action Body { get; private set; }
    }

    internal static class Program
    {
        private static int Main()
        {
            IList<TestCase> tests = CoreTests.All();
            int passed = 0;
            int failed = 0;
            foreach (TestCase test in tests)
            {
                try
                {
                    test.Body();
                    Console.WriteLine("PASS " + test.Name);
                    passed++;
                }
                catch (Exception exception)
                {
                    Console.WriteLine("FAIL " + test.Name);
                    Console.WriteLine("     " + exception);
                    failed++;
                }
            }

            Console.WriteLine();
            Console.WriteLine("Total: " + tests.Count + ", Passed: " + passed + ", Failed: " + failed);
            return failed == 0 ? 0 : 1;
        }
    }
}

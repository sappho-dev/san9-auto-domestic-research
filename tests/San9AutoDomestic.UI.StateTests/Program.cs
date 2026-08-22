using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace San9AutoDomestic.UI.StateTests
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
        [STAThread]
        private static int Main()
        {
            Application.SetUnhandledExceptionMode(
                UnhandledExceptionMode.CatchException);
            IList<TestCase> tests = UiStateTests.All();
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

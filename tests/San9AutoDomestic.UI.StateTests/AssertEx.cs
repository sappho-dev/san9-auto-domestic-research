using System;
using System.Collections.Generic;

namespace San9AutoDomestic.UI.StateTests
{
    internal static class AssertEx
    {
        public static void True(bool condition, string message)
        {
            if (!condition)
            {
                throw new TestFailureException(message);
            }
        }

        public static void False(bool condition, string message)
        {
            True(!condition, message);
        }

        public static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new TestFailureException(
                    message + " Expected: " + Format(expected) + "; actual: " + Format(actual) + ".");
            }
        }

        public static void Contains(string expectedPart, string actual, string message)
        {
            if (actual == null
                || actual.IndexOf(expectedPart, StringComparison.Ordinal) < 0)
            {
                throw new TestFailureException(
                    message + " Expected to find: " + expectedPart + "; actual: " + Format(actual) + ".");
            }
        }

        private static string Format<T>(T value)
        {
            return object.Equals(value, null) ? "<null>" : value.ToString();
        }
    }

    internal sealed class TestFailureException : Exception
    {
        public TestFailureException(string message)
            : base(message)
        {
        }
    }
}

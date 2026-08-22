using System;
using System.Collections.Generic;

namespace San9AutoDomestic.Core.Tests
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

        public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
        {
            List<T> expectedList = new List<T>(expected);
            List<T> actualList = new List<T>(actual);
            if (expectedList.Count != actualList.Count)
            {
                throw new TestFailureException(
                    message + " Sequence lengths differ. Expected " + expectedList.Count + ", actual " + actualList.Count + ".");
            }

            for (int index = 0; index < expectedList.Count; index++)
            {
                if (!EqualityComparer<T>.Default.Equals(expectedList[index], actualList[index]))
                {
                    throw new TestFailureException(
                        message + " Sequences differ at index " + index + ". Expected: "
                        + Format(expectedList[index]) + "; actual: " + Format(actualList[index]) + ".");
                }
            }
        }

        public static TException Throws<TException>(Action action, string message)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException exception)
            {
                return exception;
            }
            catch (Exception exception)
            {
                throw new TestFailureException(
                    message + " Expected " + typeof(TException).Name + " but got " + exception.GetType().Name + ".");
            }

            throw new TestFailureException(message + " Expected " + typeof(TException).Name + " but no exception was thrown.");
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

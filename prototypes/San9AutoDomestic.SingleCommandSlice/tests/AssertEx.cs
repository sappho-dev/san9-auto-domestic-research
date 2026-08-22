using System;
using System.Collections.Generic;

namespace San9AutoDomestic.SingleCommandSlice.SelfTest
{
    internal static class AssertEx
    {
        public static void True(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
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
                throw new InvalidOperationException(
                    message + " Expected=" + expected + ", Actual=" + actual + ".");
            }
        }

        public static void SequenceEqual<T>(
            IEnumerable<T> expected,
            IEnumerable<T> actual,
            string message)
        {
            List<T> expectedList = new List<T>(expected);
            List<T> actualList = new List<T>(actual);
            if (expectedList.Count != actualList.Count)
            {
                throw new InvalidOperationException(
                    message + " Count expected=" + expectedList.Count
                    + ", actual=" + actualList.Count + ".");
            }

            for (int index = 0; index < expectedList.Count; index++)
            {
                if (!EqualityComparer<T>.Default.Equals(expectedList[index], actualList[index]))
                {
                    throw new InvalidOperationException(
                        message + " Difference at index " + index + ".");
                }
            }
        }

        public static T Throws<T>(Action action, string message)
            where T : Exception
        {
            try
            {
                action();
            }
            catch (T exception)
            {
                return exception;
            }

            throw new InvalidOperationException(message);
        }
    }
}

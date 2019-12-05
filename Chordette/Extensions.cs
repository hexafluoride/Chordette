using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Chordette
{
    public static class Extensions
    {
        public static string ToUsefulString(this byte[] arr, bool shorten = false)
        {
            if (arr == null || arr.Length == 0)
                return "(none)";

            var ret = BitConverter.ToString(arr).Replace("-", "").ToLower();

            if (!shorten)
                return ret;

            if (ret.Length < 11)
                return ret;

            return ret.Substring(0, 4) + "..." + ret.Substring(ret.Length - 4, 4);
        }

        public static readonly object GlobalPrintLock = new object();
        private static readonly object PrintLock = new object();

        public static void Print(this byte[] arr)
        {
            var acceptable_colors = new ConsoleColor[]
            {
                ConsoleColor.Red,
                ConsoleColor.Yellow,
                ConsoleColor.White,
                ConsoleColor.Blue,
                ConsoleColor.DarkYellow,
                ConsoleColor.Green,
                ConsoleColor.Cyan
            };

            if (arr == null || arr.Length == 0)
            {
                Console.Write("(none)");
                return;
            }

            var shortened = arr.ToUsefulString(true);
            var first_chars = shortened.Substring(0, shortened.Length - 4);
            var last_chars = shortened.Substring(shortened.Length - 4);
            var hash = BitConverter.ToUInt32(arr, arr.Length - 4) % acceptable_colors.Length;

            lock (PrintLock)
            {
                var prev_color = ConsoleColor.Gray;
                Console.Write(first_chars);
                Console.ForegroundColor = acceptable_colors[hash];
                Console.Write(last_chars);
                Console.ForegroundColor = prev_color;
            }
        }

        public static bool IsIn(this int num, int start, int end, bool start_inclusive = true, bool end_inclusive = true) =>
            new BigInteger(num).ToByteArray().IsIn(new BigInteger(start).ToByteArray(), new BigInteger(end).ToByteArray(), start_inclusive, end_inclusive);

        public static bool IsIn(this byte[] arr, byte[] start, byte[] end, bool start_inclusive = true, bool end_inclusive = true)
        {
            var start_int = new BigInteger(start, true);
            var end_int = new BigInteger(end, true);
            var arr_int = new BigInteger(arr, true);

            bool cond_1, cond_2;
            
            cond_1 = start_inclusive ? (start_int <= arr_int) : (start_int < arr_int);
            cond_2 = end_inclusive ? (arr_int <= end_int) : (arr_int < end_int);

            if (end_int <= start_int)
                return cond_1 || cond_2;
            else
                return cond_1 && cond_2;
        }

        public static bool IsNotIn(this byte[] arr, byte[] start, byte[] end, bool start_inclusive = true, bool end_inclusive = true) =>
            !arr.IsIn(start, end, start_inclusive, end_inclusive);

        public static byte[] ToPaddedArray(this BigInteger integer, int len)
        {
            var short_arr = integer.ToByteArray();
            var new_arr = new byte[len];
            Array.Copy(short_arr, 0, new_arr, 0, Math.Min(len, short_arr.Length));

            return new_arr;
        }

        public static void ForceRemove<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dict, TKey key)
        {
            while (dict.ContainsKey(key))
                dict.TryRemove(key, out TValue _);
        }

        public static IEnumerable<T> ShuffleIterator<T>(this IEnumerable<T> source, Random random)
        {
            var buffer = source.ToList();
            for (int i = 0; i < buffer.Count; i++)
            {
                int j = random.Next(i, buffer.Count);
                yield return buffer[j];

                buffer[j] = buffer[i];
            }
        }
    }
}

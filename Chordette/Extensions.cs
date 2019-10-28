using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Chordette
{
    public static class Extensions
    {
        public static string ToUsefulString(this byte[] arr) =>
            BitConverter.ToString(arr).Replace("-", "").ToLower();

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
    }
}

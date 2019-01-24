﻿// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

//

using Spreads.Collections;
using Spreads.DataTypes;
using Spreads.Native;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Vector = System.Numerics.Vector;

namespace Spreads.Algorithms
{
    /// <summary>
    /// Algorithms to find values in contiguous data (memory region or a data structure with an indexer), e.g. <see cref="Vec{T}"/>.
    /// </summary>
    /// <remarks>
    /// WARNING: Methods in this static class do not perform bound checks and are intended to be used
    /// as building blocks in other parts that calculate bounds correctly and
    /// do performs required checks on external input.
    /// </remarks>
    public static class VectorSearch
    {
        /// <summary>
        /// Performs standard binary search and returns index of the value or its negative binary complement.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BinarySearch<T>(ref T vecStart, int length, T value, KeyComparer<T> comparer)
        {
            unchecked
            {
                int lo = 0;
                int hi = length - 1;
                // If length == 0, hi == -1, and loop will not be entered
                while (lo <= hi)
                {
                    // PERF: `lo` or `hi` will never be negative inside the loop,
                    //       so computing median using uints is safe since we know
                    //       `length <= int.MaxValue`, and indices are >= 0
                    //       and thus cannot overflow an uint.
                    //       Saves one subtraction per loop compared to
                    //       `int i = lo + ((hi - lo) >> 1);`
                    int i = (int)(((uint)hi + (uint)lo) >> 1);

                    int c = comparer.Compare(value, Unsafe.Add(ref vecStart, i));
                    if (c == 0)
                    {
                        return i;
                    }
                    else if (c > 0)
                    {
                        lo = i + 1;
                    }
                    else
                    {
                        hi = i - 1;
                    }
                }

                // If none found, then a negative number that is the bitwise complement
                // of the index of the next element that is larger than or, if there is
                // no larger element, the bitwise complement of `length`, which
                // is `lo` at this point.
                return ~lo;
            }
        }

        /// <summary>
        /// Performs standard binary search and returns index of the value or its negative binary complement.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BinarySearch<T, TVec>(ref TVec vec, int start, int length, T value, KeyComparer<T> comparer)
            where TVec : IVector<T>
        {
            Debug.Assert(unchecked((uint)start + (uint)length) <= vec.Length);
            unchecked
            {
                int lo = start;
                int hi = length - 1;
                while (lo <= hi)
                {
                    int i = (int)(((uint)hi + (uint)lo) >> 1);

                    int c = comparer.Compare(value, vec.DangerousGet(i));
                    if (c == 0)
                    {
                        return i;
                    }
                    else if (c > 0)
                    {
                        lo = i + 1;
                    }
                    else
                    {
                        hi = i - 1;
                    }
                }

                return ~lo;
            }
        }

        /// <summary>
        /// Converts a result of a sorted search to result of directional search with direction of <see cref="Lookup"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SearchToLookup(int length, Lookup lookup, int i)
        {
            if (i >= 0)
            {
                if (lookup.IsEqualityOK())
                {
                    return i;
                }

                if (lookup == Lookup.LT)
                {
                    if (i == 0)
                    {
                        return -1;
                    }

                    i--;
                }
                else // depends on if (eqOk) above
                {
                    Debug.Assert(lookup == Lookup.GT);
                    if (i == length - 1)
                    {
                        return ~length;
                    }

                    i++;
                }
            }
            else
            {
                if (lookup == Lookup.EQ)
                {
                    return i;
                }

                i = ~i;

                // LT or LE
                if (((uint)lookup & (uint)Lookup.LT) != 0)
                {
                    // i is idx of element that is larger, nothing here for LE/LT
                    if (i == 0)
                    {
                        return -1;
                    }

                    i--;
                }
                else
                {
                    Debug.Assert(((uint)lookup & (uint)Lookup.GT) != 0);
                    Debug.Assert(i <= length);
                    // if was negative, if it was ~length then there are no more elements for GE/GT
                    if (i == length)
                    {
                        return ~length;
                    }

                    // i is the same, ~i is idx of element that is GT the value
                }
            }

            Debug.Assert(unchecked((uint)i) < unchecked((uint)length));
            return i;
        }

        /// <summary>
        /// Find value using binary search according to the lookup direction.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BinaryLookup<T>(ref T vecStart, int length, T value, Lookup lookup, KeyComparer<T> comparer = default)
        {
            Debug.Assert(length >= 0);

            var i = BinarySearch(ref vecStart, length, value, comparer);

            return SearchToLookup(length, lookup, i);
        }

        /// <summary>
        /// Find value using binary search according to the lookup direction.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BinaryLookup<T, TVec>(ref TVec vec, int start, int length, T value, Lookup lookup, KeyComparer<T> comparer = default)
            where TVec : IVector<T>
        {
            Debug.Assert(unchecked((uint)start + (uint)length) <= vec.Length);

            int i = BinarySearch(ref vec, start, length, value, comparer);

            return SearchToLookup(length, lookup, i);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int InterpolationSearch<T>(ref T vecStart, int length, T value, KeyComparer<T> comparer = default)
        {
            if (typeof(T) == typeof(Timestamp))
            {
                return InterpolationSearch(ref Unsafe.As<T, long>(ref vecStart), length, Unsafe.As<T, long>(ref value));
            }

            if (typeof(T) == typeof(long))
            {
                return InterpolationSearch(ref Unsafe.As<T, long>(ref vecStart), length, Unsafe.As<T, long>(ref value));
            }

            if (typeof(T) == typeof(int))
            {
                return InterpolationSearch(ref Unsafe.As<T, int>(ref vecStart), length, Unsafe.As<T, int>(ref value));
            }

            if (!KeyComparer<T>.Default.IsDiffable)
            {
                ThrowNonDiffable<T>();
            }
            return InterpolationSearchGeneric(ref vecStart, length, value, comparer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int InterpolationSearch<T, TVec>(ref TVec vec, int start, int length, T value, KeyComparer<T> comparer = default)
            where TVec : IVector<T>
        {
            // TODO test if this works and is inlined well

            if (typeof(TVec) == typeof(Vector<Timestamp>))
            {
                return InterpolationSearch(ref Unsafe.As<TVec, Vector<long>>(ref vec), start, length,
                    Unsafe.As<T, long>(ref value));
            }

            if (typeof(T) == typeof(Vector<long>))
            {
                return InterpolationSearch(ref Unsafe.As<TVec, Vector<long>>(ref vec), start, length,
                    Unsafe.As<T, long>(ref value));
            }

            if (typeof(T) == typeof(Vector<int>))
            {
                return InterpolationSearch(ref Unsafe.As<TVec, Vector<int>>(ref vec), start, length,
                    Unsafe.As<T, int>(ref value));
            }

            if (!KeyComparer<T>.Default.IsDiffable)
            {
                ThrowNonDiffable<T>();
            }
            return InterpolationSearchGeneric(ref vec, start, length, value, comparer);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowNonDiffable<T>()
        {
            ThrowHelper.ThrowNotSupportedException("KeyComparer<T>.Default.IsDiffable must be true for interpolation search: T is " + typeof(T).FullName);
        }

        /// <summary>
        /// Exponential
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int InterpolationSearch(ref long vecStart, int length, long value)
        {
            // this is definitive version, all other overloads must do exactly the same operations
            unchecked
            {
                int i;
                int lo = 0;
                int hi = length - 1;
                if (lo < hi)
                {
                    long vlo = vecStart;
                    long vhi = Unsafe.Add(ref vecStart, hi);
                    long vRange = vhi - vlo;

                    Debug.Assert(vRange > 0);

                    // (hi - lo) <= int32.MaxValue
                    // vlo could be zero while value could easily be close to int64.MaxValue (nanos in unix time, we are now between 60 and 61 bit at 60.4)
                    // convert to double here to avoid overflow and for much faster calculations
                    // (only 4 cycles vs 25 cycles https://lemire.me/blog/2017/11/16/fast-exact-integer-divisions-using-floating-point-operations/)
                    double nominator = (hi - lo) * (double)(value - vlo);

                    i = Math.Min(hi, (int)(nominator / vRange));

                    if (i < 0)
                    {
                        i = 0;
                    }

                    var vi = Unsafe.Add(ref vecStart, i);

                    if (value == vi)
                    {
                        goto FOUND;
                    }

                    var offset = 1;

                    if (value < vi)
                    {
                        while ((i -= offset) >= 0)
                        {
                            vi = Unsafe.Add(ref vecStart, i);

                            if (value == vi)
                            {
                                goto FOUND;
                            }

                            if (value > vi)
                            {
                                lo = i + 1;
                                hi = lo + offset;
                                goto BIN_SEARCH;
                            }

                            // x2
                            offset <<= 1;
                        }

                        Debug.Assert(i < 0);

                        // i was decremented by offset and became negative, restore it and search from zero
                        hi = i + offset;

                        Debug.Assert(lo == 0);
                        Debug.Assert(hi > 0);
                    }
                    else
                    {
                        while ((i += offset) <= hi)
                        {
                            vi = Unsafe.Add(ref vecStart, i);

                            if (value == vi)
                            {
                                goto FOUND;
                            }

                            if (value < vi)
                            {
                                hi = i - 1;
                                lo = Math.Max(0, hi - offset);
                                goto BIN_SEARCH;
                            }

                            offset <<= 1;
                        }

                        Debug.Assert(i > hi);

                        // i was incremented by offset and became > hi, restore it and search to hi
                        lo = i - offset;
                    }
                }

            BIN_SEARCH:

                var iBs = BinarySearch(ref Unsafe.Add(ref vecStart, lo), 1 + hi - lo, value, default);

                if (iBs >= 0)
                {
                    return lo + iBs;
                }
                else
                {
                    return ~(lo + ~iBs);
                }

            FOUND:
                return i;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int InterpolationSearch<TVec>(ref TVec vec, int start, int length, long value)
            where TVec : IVector<long>
        {
            // adjusted copy of long
            unchecked
            {
                int i;
                int lo = start;
                int hi = length;
                if (lo < hi)
                {
                    long vlo = vec.DangerousGet(0);
                    long vhi = vec.DangerousGet(hi);
                    long vRange = vhi - vlo;

                    Debug.Assert(vRange > 0);

                    // (hi - lo) <= int32.MaxValue
                    // vlo could be zero while value could easily be close to int64.MaxValue (nanos in unix time, we are now between 60 and 61 bit at 60.4)
                    // convert to double here to avoid overflow and for much faster calculations
                    // (only 4 cycles vs 25 cycles https://lemire.me/blog/2017/11/16/fast-exact-integer-divisions-using-floating-point-operations/)
                    double nominator = (hi - lo) * (double)(value - vlo);

                    i = Math.Max(0, Math.Min(hi, (int)(nominator / vRange)));

                    if (i < 0)
                    {
                        i = 0;
                    }

                    var vi = vec.DangerousGet(i);

                    if (value == vi)
                    {
                        goto FOUND;
                    }

                    var offset = 1;

                    if (value < vi)
                    {
                        while ((i -= offset) >= 0)
                        {
                            vi = vec.DangerousGet(i);

                            if (value == vi)
                            {
                                goto FOUND;
                            }

                            if (value > vi)
                            {
                                lo = i + 1;
                                hi = lo + offset;
                                goto BIN_SEARCH;
                            }

                            // x2
                            offset <<= 1;
                        }

                        Debug.Assert(i < 0);

                        // i was decremented by offset and became negative, restore it and search from zero
                        hi = i + offset;

                        Debug.Assert(lo == 0);
                        Debug.Assert(hi > 0);
                    }
                    else
                    {
                        while ((i += offset) <= hi)
                        {
                            vi = vec.DangerousGet(i);

                            if (value == vi)
                            {
                                goto FOUND;
                            }

                            if (value < vi)
                            {
                                hi = i - 1;
                                lo = Math.Max(0, hi - offset);
                                goto BIN_SEARCH;
                            }

                            offset <<= 1;
                        }

                        Debug.Assert(i > hi);

                        // i was incremented by offset and became > hi, restore it and search to hi
                        lo = i - offset;
                    }
                }

            BIN_SEARCH:

                var iBs = BinarySearch(ref vec, lo, 1 + hi - lo, value, default);

                if (iBs >= 0)
                {
                    return lo + iBs;
                }
                else
                {
                    return ~(lo + ~iBs);
                }

            FOUND:
                return i;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int InterpolationSearch(ref int vecStart, int length, int value)
        {
            // adjusted copy of long
            unchecked
            {
                int i;
                int lo = 0;
                int hi = length - 1;
                if (lo < hi)
                {
                    long vlo = vecStart;
                    long vhi = Unsafe.Add(ref vecStart, hi);
                    long vRange = vhi - vlo;

                    Debug.Assert(vRange > 0);

                    // (hi - lo) <= int32.MaxValue
                    // vlo could be zero while value could easily be close to int64.MaxValue (nanos in unix time, we are now between 60 and 61 bit at 60.4)
                    // convert to double here to avoid overflow and for much faster calculations
                    // (only 4 cycles vs 25 cycles https://lemire.me/blog/2017/11/16/fast-exact-integer-divisions-using-floating-point-operations/)
                    double nominator = (hi - lo) * (double)(value - vlo);

                    i = Math.Max(0, Math.Min(hi, (int)(nominator / vRange)));

                    if (i < 0)
                    {
                        i = 0;
                    }

                    var vi = Unsafe.Add(ref vecStart, i);

                    if (value == vi)
                    {
                        goto FOUND;
                    }

                    var offset = 1;

                    if (value < vi)
                    {
                        while ((i -= offset) >= 0)
                        {
                            vi = Unsafe.Add(ref vecStart, i);

                            if (value == vi)
                            {
                                goto FOUND;
                            }

                            if (value > vi)
                            {
                                lo = i + 1;
                                hi = lo + offset;
                                goto BIN_SEARCH;
                            }

                            // x2
                            offset <<= 1;
                        }

                        Debug.Assert(i < 0);

                        // i was decremented by offset and became negative, restore it and search from zero
                        hi = i + offset;

                        Debug.Assert(lo == 0);
                        Debug.Assert(hi > 0);
                    }
                    else
                    {
                        while ((i += offset) <= hi)
                        {
                            vi = Unsafe.Add(ref vecStart, i);

                            if (value == vi)
                            {
                                goto FOUND;
                            }

                            if (value < vi)
                            {
                                hi = i - 1;
                                lo = Math.Max(0, hi - offset);
                                goto BIN_SEARCH;
                            }

                            offset <<= 1;
                        }

                        Debug.Assert(i > hi);

                        // i was incremented by offset and became > hi, restore it and search to hi
                        lo = i - offset;
                    }
                }

            BIN_SEARCH:

                var iBs = BinarySearch(ref Unsafe.Add(ref vecStart, lo), 1 + hi - lo, value, default);

                if (iBs >= 0)
                {
                    return lo + iBs;
                }
                else
                {
                    return ~(lo + ~iBs);
                }

            FOUND:
                return i;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int InterpolationSearch<TVec>(ref TVec vec, int start, int length, int value)
            where TVec : IVector<int>
        {
            // adjusted copy of long
            unchecked
            {
                int i;
                int lo = start;
                int hi = length;
                if (lo < hi)
                {
                    long vlo = vec.DangerousGet(0);
                    long vhi = vec.DangerousGet(hi);
                    long vRange = vhi - vlo;

                    Debug.Assert(vRange > 0);

                    // (hi - lo) <= int32.MaxValue
                    // vlo could be zero while value could easily be close to int64.MaxValue (nanos in unix time, we are now between 60 and 61 bit at 60.4)
                    // convert to double here to avoid overflow and for much faster calculations
                    // (only 4 cycles vs 25 cycles https://lemire.me/blog/2017/11/16/fast-exact-integer-divisions-using-floating-point-operations/)
                    double nominator = (hi - lo) * (double)(value - vlo);

                    i = Math.Min(hi, (int)(nominator / vRange));

                    if (i < 0)
                    {
                        i = 0;
                    }

                    var vi = vec.DangerousGet(i);

                    if (value == vi)
                    {
                        goto FOUND;
                    }

                    var offset = 1;

                    if (value < vi)
                    {
                        while ((i -= offset) >= 0)
                        {
                            vi = vec.DangerousGet(i);

                            if (value == vi)
                            {
                                goto FOUND;
                            }

                            if (value > vi)
                            {
                                lo = i + 1;
                                hi = lo + offset;
                                goto BIN_SEARCH;
                            }

                            // x2
                            offset <<= 1;
                        }

                        Debug.Assert(i < 0);

                        // i was decremented by offset and became negative, restore it and search from zero
                        hi = i + offset;

                        Debug.Assert(lo == 0);
                        Debug.Assert(hi > 0);
                    }
                    else
                    {
                        while ((i += offset) <= hi)
                        {
                            vi = vec.DangerousGet(i);

                            if (value == vi)
                            {
                                goto FOUND;
                            }

                            if (value < vi)
                            {
                                hi = i - 1;
                                lo = Math.Max(0, hi - offset);
                                goto BIN_SEARCH;
                            }

                            offset <<= 1;
                        }

                        Debug.Assert(i > hi);

                        // i was incremented by offset and became > hi, restore it and search to hi
                        lo = i - offset;
                    }
                }

            BIN_SEARCH:

                var iBs = BinarySearch(ref vec, lo, 1 + hi - lo, value, default);

                if (iBs >= 0)
                {
                    return lo + iBs;
                }
                else
                {
                    return ~(lo + ~iBs);
                }

            FOUND:
                return i;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int InterpolationSearchGeneric<T>(ref T vecStart, int length, T value, KeyComparer<T> comparer = default)
        {
            // adjusted copy of long
            unchecked
            {
                int i;
                int lo = 0;
                int hi = length - 1;
                if (lo < hi)
                {
                    var vlo = Unsafe.Add(ref vecStart, lo);
                    var vhi = Unsafe.Add(ref vecStart, hi);
                    long vRange = comparer.Diff(vhi, vlo);

                    Debug.Assert(vRange > 0);

                    // (hi - lo) <= int32.MaxValue
                    // vlo could be zero while value could easily be close to int64.MaxValue (nanos in unix time, we are now between 60 and 61 bit at 60.4)
                    // convert to double here to avoid overflow and for much faster calculations
                    // (only 4 cycles vs 25 cycles https://lemire.me/blog/2017/11/16/fast-exact-integer-divisions-using-floating-point-operations/)
                    double nominator = (hi - lo) * (double)comparer.Diff(value, vlo);

                    i = Math.Min(hi, (int)(nominator / vRange));

                    if (i < 0)
                    {
                        i = 0;
                    }

                    int c = comparer.Compare(value, Unsafe.Add(ref vecStart, i));

                    if (c == 0)
                    {
                        goto FOUND;
                    }

                    var offset = 1;

                    if (c < 0)
                    {
                        while ((i -= offset) >= 0)
                        {
                            c = comparer.Compare(value, Unsafe.Add(ref vecStart, i));
                            if (c == 0)
                            {
                                goto FOUND;
                            }

                            if (c > 0)
                            {
                                lo = i + 1;
                                hi = lo + offset;
                                goto BIN_SEARCH;
                            }

                            // x2
                            offset <<= 1;
                        }

                        Debug.Assert(i < 0);

                        // i was decremented by offset and became negative, restore it and search from zero
                        hi = i + offset;

                        Debug.Assert(lo == 0);
                        Debug.Assert(hi > 0);
                    }
                    else
                    {
                        while ((i += offset) <= hi)
                        {
                            c = comparer.Compare(value, Unsafe.Add(ref vecStart, i));

                            if (c == 0)
                            {
                                goto FOUND;
                            }

                            if (c < 0)
                            {
                                hi = i - 1;
                                lo = Math.Max(0, hi - offset);
                                goto BIN_SEARCH;
                            }

                            offset <<= 1;
                        }

                        Debug.Assert(i > hi);

                        // i was incremented by offset and became > hi, restore it and search to hi
                        lo = i - offset;
                    }
                }

            BIN_SEARCH:

                var iBs = BinarySearch(ref Unsafe.Add(ref vecStart, lo), 1 + hi - lo, value, default);

                if (iBs >= 0)
                {
                    return lo + iBs;
                }
                else
                {
                    return ~(lo + ~iBs);
                }

            FOUND:
                return i;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int InterpolationSearchGeneric<T, TVec>(ref TVec vec, int start, int length, T value, KeyComparer<T> comparer = default)
             where TVec : IVector<T>
        {
            // adjusted copy of long
            unchecked
            {
                int i;
                int lo = start;
                int hi = length - 1;
                if (lo < hi)
                {
                    var vlo = vec.DangerousGet(lo);
                    var vhi = vec.DangerousGet(hi);
                    long vRange = comparer.Diff(vhi, vlo);

                    Debug.Assert(vRange > 0);

                    // (hi - lo) <= int32.MaxValue
                    // vlo could be zero while value could easily be close to int64.MaxValue (nanos in unix time, we are now between 60 and 61 bit at 60.4)
                    // convert to double here to avoid overflow and for much faster calculations
                    // (only 4 cycles vs 25 cycles https://lemire.me/blog/2017/11/16/fast-exact-integer-divisions-using-floating-point-operations/)
                    double nominator = (hi - lo) * (double)comparer.Diff(value, vlo);

                    i = Math.Min(hi, (int)(nominator / vRange));

                    if (i < 0)
                    {
                        i = 0;
                    }

                    int c = comparer.Compare(value, vec.DangerousGet(i));

                    if (c == 0)
                    {
                        goto FOUND;
                    }

                    var offset = 1;

                    if (c < 0)
                    {
                        while ((i -= offset) >= 0)
                        {
                            c = comparer.Compare(value, vec.DangerousGet(i));
                            if (c == 0)
                            {
                                goto FOUND;
                            }

                            if (c > 0)
                            {
                                lo = i + 1;
                                hi = lo + offset;
                                goto BIN_SEARCH;
                            }

                            // x2
                            offset <<= 1;
                        }

                        Debug.Assert(i < 0);

                        // i was decremented by offset and became negative, restore it and search from zero
                        hi = i + offset;

                        Debug.Assert(lo == 0);
                        Debug.Assert(hi > 0);
                    }
                    else
                    {
                        while ((i += offset) <= hi)
                        {
                            c = comparer.Compare(value, vec.DangerousGet(i));

                            if (c == 0)
                            {
                                goto FOUND;
                            }

                            if (c < 0)
                            {
                                hi = i - 1;
                                lo = Math.Max(0, hi - offset);
                                goto BIN_SEARCH;
                            }

                            offset <<= 1;
                        }

                        Debug.Assert(i > hi);

                        // i was incremented by offset and became > hi, restore it and search to hi
                        lo = i - offset;
                    }
                }

            BIN_SEARCH:

                var iBs = BinarySearch(ref vec, lo, 1 + hi - lo, value, default);

                if (iBs >= 0)
                {
                    return lo + iBs;
                }
                else
                {
                    return ~(lo + ~iBs);
                }

            FOUND:
                return i;
            }
        }

        /// <summary>
        /// Find value using binary search according to the lookup direction.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int InterpolationLookup<T>(ref T vecStart, int length, T value, Lookup lookup,
            KeyComparer<T> comparer = default)
        {
            Debug.Assert(length >= 0);

            var i = InterpolationSearch(ref vecStart, length, value, comparer);

            return SearchToLookup(length, lookup, i);
        }

        /// <summary>
        /// Find value using binary search according to the lookup direction.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int InterpolationLookup<T, TVec>(ref TVec vec, int start, int length, T value, Lookup lookup, KeyComparer<T> comparer = default)
            where TVec : IVector<T>
        {
            Debug.Assert(unchecked((uint)start + (uint)length) <= vec.Length);

            int i = InterpolationSearch(ref vec, start, length, value, comparer);

            return SearchToLookup(length, lookup, i);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int IndexOf<T>(ref T vecStart, T value, int length)
        {
            Debug.Assert(length >= 0);

            if (Vector.IsHardwareAccelerated)
            {
                if (typeof(T) == typeof(byte))
                {
                    return IndexOfVectorized(ref Unsafe.As<T, byte>(ref vecStart), Unsafe.As<T, byte>(ref value), length);
                }
                if (typeof(T) == typeof(sbyte))
                {
                    return IndexOfVectorized(ref Unsafe.As<T, sbyte>(ref vecStart), Unsafe.As<T, sbyte>(ref value), length);
                }
                if (typeof(T) == typeof(ushort))
                {
                    return IndexOfVectorized(ref Unsafe.As<T, ushort>(ref vecStart), Unsafe.As<T, ushort>(ref value), length);
                }
                if (typeof(T) == typeof(short))
                {
                    return IndexOfVectorized(ref Unsafe.As<T, short>(ref vecStart), Unsafe.As<T, short>(ref value), length);
                }
                if (typeof(T) == typeof(uint))
                {
                    return IndexOfVectorized(ref Unsafe.As<T, uint>(ref vecStart), Unsafe.As<T, uint>(ref value), length);
                }
                if (typeof(T) == typeof(int))
                {
                    return IndexOfVectorized(ref Unsafe.As<T, int>(ref vecStart), Unsafe.As<T, int>(ref value), length);
                }
                if (typeof(T) == typeof(ulong))
                {
                    return IndexOfVectorized(ref Unsafe.As<T, ulong>(ref vecStart), Unsafe.As<T, ulong>(ref value), length);
                }
                if (typeof(T) == typeof(long))
                {
                    return IndexOfVectorized(ref Unsafe.As<T, long>(ref vecStart), Unsafe.As<T, long>(ref value), length);
                }
                if (typeof(T) == typeof(float))
                {
                    return IndexOfVectorized(ref Unsafe.As<T, float>(ref vecStart), Unsafe.As<T, float>(ref value), length);
                }
                if (typeof(T) == typeof(double))
                {
                    return IndexOfVectorized(ref Unsafe.As<T, double>(ref vecStart), Unsafe.As<T, double>(ref value), length);
                }

                // non-standard
                // Treat Timestamp as long because it is a single long internally
                if (typeof(T) == typeof(Timestamp))
                {
                    return IndexOfVectorized(ref Unsafe.As<T, long>(ref vecStart), Unsafe.As<T, long>(ref value), length);
                }

                // Treat DateTime as ulong because it is a single ulong internally
                if (typeof(T) == typeof(DateTime))
                {
                    return IndexOfVectorized(ref Unsafe.As<T, ulong>(ref vecStart), Unsafe.As<T, ulong>(ref value), length);
                }
            }

            return IndexOfSimple(ref vecStart, value, length);
        }

        internal static unsafe int IndexOfVectorized<T>(ref T searchSpace, T value, int length) where T : struct
        {
            unchecked
            {
                Debug.Assert(length >= 0);

                IntPtr offset = (IntPtr)0; // Use IntPtr for arithmetic to avoid unnecessary 64->32->64 truncations
                IntPtr nLength = (IntPtr)length;

                if (Vector.IsHardwareAccelerated && length >= System.Numerics.Vector<T>.Count * 2)
                {
                    int unaligned = (int)Unsafe.AsPointer(ref searchSpace) & (System.Numerics.Vector<T>.Count - 1);
                    nLength = (IntPtr)((System.Numerics.Vector<T>.Count - unaligned) & (System.Numerics.Vector<T>.Count - 1));
                }

            SequentialScan:
                while ((byte*)nLength >= (byte*)8)
                {
                    nLength -= 8;

                    if (UnsafeEx.EqualsConstrained(ref value, ref Unsafe.Add(ref searchSpace, offset)))
                        goto Found;
                    if (UnsafeEx.EqualsConstrained(ref value, ref Unsafe.Add(ref searchSpace, offset + 1)))
                        goto Found1;
                    if (UnsafeEx.EqualsConstrained(ref value, ref Unsafe.Add(ref searchSpace, offset + 2)))
                        goto Found2;
                    if (UnsafeEx.EqualsConstrained(ref value, ref Unsafe.Add(ref searchSpace, offset + 3)))
                        goto Found3;
                    if (UnsafeEx.EqualsConstrained(ref value, ref Unsafe.Add(ref searchSpace, offset + 4)))
                        goto Found4;
                    if (UnsafeEx.EqualsConstrained(ref value, ref Unsafe.Add(ref searchSpace, offset + 5)))
                        goto Found5;
                    if (UnsafeEx.EqualsConstrained(ref value, ref Unsafe.Add(ref searchSpace, offset + 6)))
                        goto Found6;
                    if (UnsafeEx.EqualsConstrained(ref value, ref Unsafe.Add(ref searchSpace, offset + 7)))
                        goto Found7;

                    offset += 8;
                }

                if ((byte*)nLength >= (byte*)4)
                {
                    nLength -= 4;

                    if (UnsafeEx.EqualsConstrained(ref value, ref Unsafe.Add(ref searchSpace, offset)))
                        goto Found;
                    if (UnsafeEx.EqualsConstrained(ref value, ref Unsafe.Add(ref searchSpace, offset + 1)))
                        goto Found1;
                    if (UnsafeEx.EqualsConstrained(ref value, ref Unsafe.Add(ref searchSpace, offset + 2)))
                        goto Found2;
                    if (UnsafeEx.EqualsConstrained(ref value, ref Unsafe.Add(ref searchSpace, offset + 3)))
                        goto Found3;

                    offset += 4;
                }

                while ((byte*)nLength > (byte*)0)
                {
                    nLength -= 1;

                    if (UnsafeEx.EqualsConstrained(ref value, ref Unsafe.Add(ref searchSpace, offset)))
                        goto Found;

                    offset += 1;
                }

                if (Vector.IsHardwareAccelerated && ((int)(byte*)offset < length))
                {
                    nLength = (IntPtr)((length - (int)(byte*)offset) & ~(System.Numerics.Vector<T>.Count - 1));

                    // Get comparison Vector
                    System.Numerics.Vector<T> vComparison = new System.Numerics.Vector<T>(value);

                    while ((byte*)nLength > (byte*)offset)
                    {
                        var vMatches = Vector.Equals(vComparison,
                            Unsafe.ReadUnaligned<System.Numerics.Vector<T>>(
                                ref Unsafe.As<T, byte>(ref Unsafe.Add(ref searchSpace, offset))));
                        if (System.Numerics.Vector<T>.Zero.Equals(vMatches))
                        {
                            offset += System.Numerics.Vector<T>.Count;
                            continue;
                        }

                        // Find offset of first match
                        return IndexOfSimple(ref Unsafe.Add(ref searchSpace, offset), value, System.Numerics.Vector<T>.Count);
                        // return (int)(byte*)offset + LocateFirstFoundByte(vMatches);
                    }

                    if ((int)(byte*)offset < length)
                    {
                        nLength = (IntPtr)(length - (int)(byte*)offset);
                        goto SequentialScan;
                    }
                }

                return -1;
            Found: // Workaround for https://github.com/dotnet/coreclr/issues/13549
                return (int)(byte*)offset;
            Found1:
                return (int)(byte*)(offset + 1);
            Found2:
                return (int)(byte*)(offset + 2);
            Found3:
                return (int)(byte*)(offset + 3);
            Found4:
                return (int)(byte*)(offset + 4);
            Found5:
                return (int)(byte*)(offset + 5);
            Found6:
                return (int)(byte*)(offset + 6);
            Found7:
                return (int)(byte*)(offset + 7);
            }
        }

        /// <summary>
        /// Simple implementation using for loop.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoOptimization)]
        internal static int IndexOfSimple<T>(ref T vecStart, T value, int length)
        {
            for (int i = 0; i < length; i++)
            {
                if (UnsafeEx.EqualsConstrained(ref Unsafe.Add(ref vecStart, i), ref value))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
﻿// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Spreads.Serialization;
using Spreads.Serialization.Utf8Json;
using Spreads.Threading;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Spreads.DataTypes
{
    /// <summary>
    /// A Timestamp stored as UTC nanos since Unix epoch.
    /// </summary>
    /// <remarks>
    /// Timestamp is used as a much faster and convenient replacement of <see cref="DateTime"/>
    /// in Spreads, because DateTime is very problematic data structure in .NET.
    ///
    /// <para />
    ///
    /// First, it has <see cref="DateTimeKind"/>
    /// property that has, in our opinion, meaningless and unusable value <see cref="DateTimeKind.Local"/>. When
    /// a value of this kind is serialized it doesn't have any meaning outside the serializer machine.
    /// <see cref="DateTimeKind.Utc"/> and <see cref="DateTimeKind.Unspecified"/> have different binary layout,
    /// but there is probably no meaningful way to treat unspecified kind other than UTC. The kind bits in
    /// <see cref="DateTime"/> add no value but make the same values binary incompatible.
    /// <see cref="Timestamp"/> time is always UTC and time zone information is supposed to be stored
    /// separately.
    ///
    /// <para />
    ///
    /// Second, <see cref="DateTime"/> structure has <see cref="LayoutKind.Auto"/>, even though internally
    /// it is just <see cref="ulong"/> and millions of code lines depend on its binary layout so that
    /// Microsoft is unlikely to change it ever. The auto layout makes <see cref="DateTime"/>  and
    /// every other structure that contains it as a field not blittable. <see cref="Timestamp"/> is blittable
    /// and very fast. See our serialization documentation on why it is a big deal. (TODO link).
    ///
    /// <para />
    ///
    /// <see cref="Timestamp"/> is serialized to JSON as decimal string with seconds, e.g. "1552315408.792075401".
    /// This is one of the formats supported by <see href="https://www.npmjs.com/package/microtime">microtime</see>
    /// package. A value is stored as string to preserve precision, but in JavaScript it could be trivially
    /// converted to a number with plus sign `+"123456789.123456789"`. If you need to keep nanoseconds
    /// precision in JavaScript then split the string by dot and use two values separately.
    /// Deserializer supports decimal as a number as well as a string.
    ///
    /// <para />
    ///
    /// <see cref="TimeService"/> provides unique monotonic timestamps and could work across processes via shared memory.
    ///
    /// <para />
    /// Range:
    /// ```
    /// 2^63: 9,223,372,036,854,780,000
    /// Nanos per day: 86,400,000,000,000 (2^47)
    /// Nanos per year: 31,557,600,000,000,000 (2^55)
    /// 292 years of nanos in 2^63 is ought to be enough for everyone living now and their grand grand grandchildren.
    /// ```
    /// </remarks>
    /// <seealso cref="TimeService"/>
    [StructLayout(LayoutKind.Sequential, Size = Size)]
    [BinarySerialization(Size)]
    [DebuggerDisplay("{" + nameof(ToString) + "()}")]
    [JsonFormatter(typeof(Formatter))]
    public readonly struct Timestamp : IComparable<Timestamp>, IEquatable<Timestamp>
    {
        public const int Size = 8;

        private static readonly long UnixEpochTicks = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).Ticks;
        private readonly long _nanos;

        public Timestamp(long nanos)
        {
            _nanos = nanos;
        }

        public DateTime DateTime => this;

        public long Nanos
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _nanos;
        }

        public long Micros
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _nanos / 1000;
        }

        public long Millis
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _nanos / 1000_000;
        }

        /// <summary>
        /// Returns <see cref="TimeSpan"/> with nanoseconds *rounded up* to ticks.
        /// </summary>
        public TimeSpan TimeSpan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                // Due to TimeService implementation we often have small nanos,
                // but zero means equality. One tick is as small as one nanosecond for
                // most practical purposes when we do not work with nanosecond resolution.
                var ticks = _nanos / 100;
                if (ticks * 100 < _nanos)
                {
                    ticks++;
                }
                return new TimeSpan(ticks);
            }
        }

        public static implicit operator DateTime(Timestamp timestamp)
        {
            return new DateTime(UnixEpochTicks + timestamp._nanos / 100, DateTimeKind.Utc);
        }

        public static implicit operator Timestamp(DateTime dateTime)
        {
            Debug.Assert(dateTime.Kind != DateTimeKind.Local, "DateTime for Timestamp conversion is assumed to be UTC, got Local");
            var value = (dateTime.Ticks - UnixEpochTicks) * 100;
            return new Timestamp(value);
        }

        public static explicit operator long(Timestamp timestamp)
        {
            return timestamp._nanos;
        }

        public static explicit operator Timestamp(long nanos)
        {
            return new Timestamp(nanos);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(Timestamp other)
        {
            // Need to use compare because subtraction will wrap
            // to positive for very large neg numbers, etc.
            if (_nanos < other._nanos) return -1;
            if (_nanos > other._nanos) return 1;
            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Timestamp other)
        {
            return _nanos == other._nanos;
        }

        public override bool Equals(object obj)
        {
            if (obj is null) return false;
            return obj is Timestamp timestamp && Equals(timestamp);
        }

        public override int GetHashCode()
        {
            return _nanos.GetHashCode();
        }

        public override string ToString()
        {
            return ((DateTime)this).ToString("O");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Timestamp operator -(Timestamp x)
        {
            return new Timestamp(-x._nanos);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Timestamp operator -(Timestamp x, Timestamp y)
        {
            return new Timestamp(checked(x._nanos - y._nanos));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Timestamp operator +(Timestamp x, Timestamp y)
        {
            return new Timestamp(checked(x._nanos + y._nanos));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Timestamp x, Timestamp y)
        {
            return x.Equals(y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Timestamp x, Timestamp y)
        {
            return !x.Equals(y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(Timestamp x, Timestamp y)
        {
            return x.CompareTo(y) > 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(Timestamp x, Timestamp y)
        {
            return x.CompareTo(y) < 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(Timestamp x, Timestamp y)
        {
            return x.CompareTo(y) >= 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(Timestamp x, Timestamp y)
        {
            return x.CompareTo(y) <= 0;
        }

        public class Formatter : IJsonFormatter<Timestamp>
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Serialize(ref JsonWriter writer, Timestamp value, IJsonFormatterResolver formatterResolver)
            {
                var asDecimal = (decimal)value.Nanos / 1_000_000_000L;

                // need a string so that JS could strip last 3 digits to get int53 as micros and not loose precision
                writer.WriteQuotation();
                formatterResolver.GetFormatterWithVerify<decimal>().Serialize(ref writer, asDecimal, formatterResolver);
                writer.WriteQuotation();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Timestamp Deserialize(ref JsonReader reader, IJsonFormatterResolver formatterResolver)
            {
                var token = reader.GetCurrentJsonToken();
                if (token == JsonToken.String)
                {
                    reader.AdvanceOffset(1);
                    var asDecimal = formatterResolver.GetFormatterWithVerify<decimal>().Deserialize(ref reader,
                        formatterResolver);
                    // ReSharper disable once RedundantOverflowCheckingContext
                    var timestamp = new Timestamp(checked((long)(asDecimal * 1_000_000_000L)));
                    reader.AdvanceOffset(1);
                    return timestamp;
                }

                if (token == JsonToken.Number)
                {
                    var asDecimal = formatterResolver.GetFormatterWithVerify<decimal>().Deserialize(ref reader,
                        formatterResolver);
                    // ReSharper disable once RedundantOverflowCheckingContext
                    var timestamp = new Timestamp(checked((long)(asDecimal * 1_000_000_000L)));
                    return timestamp;
                }

                ThrowHelper.ThrowInvalidOperationException($"Wrong timestamp token in JSON: {token}");

                return default;
            }
        }
    }
}

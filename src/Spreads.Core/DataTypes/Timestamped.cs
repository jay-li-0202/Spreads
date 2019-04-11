﻿// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Spreads.Serialization;
using Spreads.Serialization.Utf8Json;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Spreads.DataTypes
{
    /// <summary>
    /// A value with a <see cref="Timestamp"/>.
    /// </summary>
    [BinarySerialization(preferBlittable: true)]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    // NB cannot use JsonFormatter attribute, this is hardcoded in DynamicGenericResolverGetFormatterHelper
    public readonly struct Timestamped<T> : IEquatable<Timestamped<T>>
    {
        public readonly Timestamp Timestamp;
        public readonly T Value;

        public Timestamped(Timestamp timestamp, T value)
        {
            Timestamp = timestamp;
            Value = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator T(Timestamped<T> timestamped)
        {
            return timestamped.Value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator (Timestamp Timestamp, T Value) (Timestamped<T> timestamped)
        {
            return (timestamped.Timestamp, timestamped.Value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Timestamped<T>((Timestamp Timestamp, T Value) tuple)
        {
            return new Timestamped<T>(tuple.Item1, tuple.Item2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Timestamped<T> other)
        {
            return Timestamp == other.Timestamp && EqualityComparer<T>.Default.Equals(Value, other.Value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Timestamped<T> x, Timestamped<T> y)
        {
            return x.Equals(y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Timestamped<T> x, Timestamped<T> y)
        {
            return !x.Equals(y);
        }

        public override bool Equals(object obj)
        {
            if (obj is null) return false;
            return obj is Timestamped<T> other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Timestamp.GetHashCode() * 397) ^ EqualityComparer<T>.Default.GetHashCode(Value);
            }
        }

        public override string ToString()
        {
            return $"{Timestamp} - {Value}";
        }
    }

    // NB cannot use JsonFormatter attribute, this is hardcoded in DynamicGenericResolverGetFormatterHelper
    public class TimestampedFormatter<T> : IJsonFormatter<Timestamped<T>>
    {
        public void Serialize(ref JsonWriter writer, Timestamped<T> value, IJsonFormatterResolver formatterResolver)
        {
            writer.WriteBeginArray();

            formatterResolver.GetFormatter<Timestamp>().Serialize(ref writer, value.Timestamp, formatterResolver);

            writer.WriteValueSeparator();

            formatterResolver.GetFormatter<T>().Serialize(ref writer, value.Value, formatterResolver);

            writer.WriteEndArray();
        }

        public Timestamped<T> Deserialize(ref JsonReader reader, IJsonFormatterResolver formatterResolver)
        {
            reader.ReadIsBeginArrayWithVerify();

            var timestamp = formatterResolver.GetFormatter<Timestamp>().Deserialize(ref reader, formatterResolver);

            reader.ReadIsValueSeparatorWithVerify();

            var value = formatterResolver.GetFormatterWithVerify<T>().Deserialize(ref reader, formatterResolver);

            reader.ReadIsEndArrayWithVerify();

            return new Timestamped<T>(timestamp, value);
        }
    }
}

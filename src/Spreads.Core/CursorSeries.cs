﻿// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Spreads.Cursors // TODO kill CursorSeries from Collections project
{
    // TODO (docs) The goal is to have only containers as classes. And even this is questionable e.g. for
    // SMs inside SCM. We need classes for locking and finalization (currently), but should try to remove finalization
    // Instead, SCM should properly dispose its inner chunks, which could be made stucts. Disposal is needed to
    // return buffers. But buffers could be made finalizable or just GCed when not disposed (buffer pools will allocate new ones)
    // Locking could be done via a third buffer which could be an unsafe memory.

    /// <summary>
    /// A lightweight wrapper around a <see cref="ICursorSeries{TKey,TValue,TCursor}"/>
    /// implementing <see cref="IReadOnlySeries{TKey, TValue}"/> interface using the cursor.
    /// </summary>
#pragma warning disable 660, 661

    public struct CursorSeries<TKey, TValue, TCursor> : IReadOnlySeries<TKey, TValue>
#pragma warning restore 660,661
        where TCursor : ICursorSeries<TKey, TValue, TCursor>
    {
        internal readonly TCursor _cursor;

        internal CursorSeries(TCursor cursor)
        {
            _cursor = cursor;
        }

        /// <summary>
        /// Get strongly-typed enumerator.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TCursor GetEnumerator()
        {
            return _cursor.Initialize();
        }

        #region ISeries members

        IDisposable IObservable<KeyValuePair<TKey, TValue>>.Subscribe(IObserver<KeyValuePair<TKey, TValue>> observer)
        {
            throw new NotImplementedException();
        }

        IAsyncEnumerator<KeyValuePair<TKey, TValue>> IAsyncEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
        {
            return _cursor.Initialize();
        }

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
        {
            return _cursor.Initialize();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _cursor.Initialize();
        }

        /// <inheritdoc />
        public KeyComparer<TKey> Comparer => _cursor.Comparer;

        /// <inheritdoc />
        public ICursor<TKey, TValue> GetCursor()
        {
            // Async support. ICursorSeries implementations do not implement MNA
            return new BaseCursorAsync<TKey, TValue, TCursor>(_cursor.Initialize());
        }

        /// <inheritdoc />
        public bool IsIndexed => _cursor.IsIndexed;

        /// <inheritdoc />
        public bool IsReadOnly
        {
            // NB this property is repeatedly called from MNA
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _cursor.IsReadOnly; }
        }

        /// <inheritdoc />
        public Task<bool> Updated
        {
            // NB this property is repeatedly called from MNA
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _cursor.Updated; }
        }

        #endregion ISeries members

        #region IReadOnlySeries members

        /// <inheritdoc />
        public bool IsEmpty
        {
            get
            {
                using (var c = _cursor.Initialize())
                {
                    return !c.MoveFirst();
                }
            }
        }

        /// <inheritdoc />
        public KeyValuePair<TKey, TValue> First
        {
            get
            {
                using (var c = _cursor.Initialize())
                {
                    return c.MoveFirst() ? c.Current : throw new InvalidOperationException("A series is empty.");
                }
            }
        }

        /// <inheritdoc />
        public KeyValuePair<TKey, TValue> Last
        {
            get
            {
                using (var c = _cursor.Initialize())
                {
                    return c.MoveLast() ? c.Current : throw new InvalidOperationException("A series is empty.");
                }
            }
        }

        /// <inheritdoc />
        public TValue GetAt(int idx)
        {
            // NB call to this.NavCursor.Source.GetAt(idx) is recursive (=> SO) and is logically wrong
            if (idx < 0) throw new ArgumentOutOfRangeException(nameof(idx));
            using (var c = _cursor.Initialize())
            {
                if (!c.MoveFirst())
                {
                    throw new KeyNotFoundException();
                }
                for (int i = 0; i < idx - 1; i++)
                {
                    if (!c.MoveNext())
                    {
                        throw new KeyNotFoundException();
                    }
                }
                return c.CurrentValue;
            }
        }

        /// <inheritdoc />
        public bool TryFind(TKey key, Lookup direction, out KeyValuePair<TKey, TValue> value)
        {
            using (var c = _cursor.Initialize())
            {
                if (c.MoveAt(key, direction))
                {
                    value = c.Current;
                    return true;
                }
                value = default(KeyValuePair<TKey, TValue>);
                return false;
            }
        }

        /// <inheritdoc />
        public bool TryGetFirst(out KeyValuePair<TKey, TValue> value)
        {
            using (var c = _cursor.Initialize())
            {
                if (c.MoveFirst())
                {
                    value = c.Current;
                    return true;
                }
                value = default(KeyValuePair<TKey, TValue>);
                return false;
            }
        }

        /// <inheritdoc />
        public bool TryGetLast(out KeyValuePair<TKey, TValue> value)
        {
            using (var c = _cursor.Initialize())
            {
                if (c.MoveLast())
                {
                    value = c.Current;
                    return true;
                }
                value = default(KeyValuePair<TKey, TValue>);
                return false;
            }
        }

        /// <inheritdoc />
        public IEnumerable<TKey> Keys
        {
            get
            {
                using (var c = _cursor.Initialize())
                {
                    while (c.MoveNext())
                    {
                        yield return c.CurrentKey;
                    }
                }
            }
        }

        /// <inheritdoc />
        public IEnumerable<TValue> Values
        {
            get
            {
                using (var c = _cursor.Initialize())
                {
                    while (c.MoveNext())
                    {
                        yield return c.CurrentValue;
                    }
                }
            }
        }

        /// <inheritdoc />
        public TValue this[TKey key]
        {
            get
            {
                if (TryFind(key, Lookup.EQ, out var tmp))
                {
                    return tmp.Value;
                }
                Collections.Generic.ThrowHelper.ThrowKeyNotFoundException();
                return default(TValue);
            }
        }

        #endregion IReadOnlySeries members

        #region Unary Operators

        // UNARY ARITHMETIC

        /// <summary>
        /// Add operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ArithmeticCursor<TKey, TValue, AddOp<TValue>, TCursor>> operator
            +(CursorSeries<TKey, TValue, TCursor> series, TValue constant)
        {
            var cursor = new ArithmeticCursor<TKey, TValue, AddOp<TValue>, TCursor>(series.GetEnumerator(), constant);
            return cursor.Source;
        }

        /// <summary>
        /// Add operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ArithmeticCursor<TKey, TValue, AddOp<TValue>, TCursor>> operator
            +(TValue constant, CursorSeries<TKey, TValue, TCursor> series)
        {
            // Addition is commutative
            var cursor = new ArithmeticCursor<TKey, TValue, AddOp<TValue>, TCursor>(series.GetEnumerator(), constant);
            return cursor.Source;
        }

        /// <summary>
        /// Negate operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ArithmeticCursor<TKey, TValue, NegateOp<TValue>, TCursor>> operator
            -(CursorSeries<TKey, TValue, TCursor> series)
        {
            var cursor = new ArithmeticCursor<TKey, TValue, NegateOp<TValue>, TCursor>(series.GetEnumerator(), default(TValue));
            return cursor.Source;
        }

        /// <summary>
        /// Unary plus operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ArithmeticCursor<TKey, TValue, PlusOp<TValue>, TCursor>> operator
            +(CursorSeries<TKey, TValue, TCursor> series)
        {
            var cursor = new ArithmeticCursor<TKey, TValue, PlusOp<TValue>, TCursor>(series.GetEnumerator(), default(TValue));
            return cursor.Source;
        }

        /// <summary>
        /// Subtract operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ArithmeticCursor<TKey, TValue, SubtractOp<TValue>, TCursor>> operator
            -(CursorSeries<TKey, TValue, TCursor> series, TValue constant)
        {
            var cursor = new ArithmeticCursor<TKey, TValue, SubtractOp<TValue>, TCursor>(series.GetEnumerator(), constant);
            return cursor.Source;
        }

        /// <summary>
        /// Subtract operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ArithmeticCursor<TKey, TValue, SubtractReverseOp<TValue>, TCursor>> operator
            -(TValue constant, CursorSeries<TKey, TValue, TCursor> series)
        {
            var cursor = new ArithmeticCursor<TKey, TValue, SubtractReverseOp<TValue>, TCursor>(series.GetEnumerator(), constant);
            return cursor.Source;
        }

        /// <summary>
        /// Multiply operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ArithmeticCursor<TKey, TValue, MultiplyOp<TValue>, TCursor>> operator
            *(CursorSeries<TKey, TValue, TCursor> series, TValue constant)
        {
            var cursor = new ArithmeticCursor<TKey, TValue, MultiplyOp<TValue>, TCursor>(series.GetEnumerator(), constant);
            return cursor.Source;
        }

        /// <summary>
        /// Multiply operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ArithmeticCursor<TKey, TValue, MultiplyOp<TValue>, TCursor>> operator
            *(TValue constant, CursorSeries<TKey, TValue, TCursor> series)
        {
            // Multiplication is commutative
            var cursor = new ArithmeticCursor<TKey, TValue, MultiplyOp<TValue>, TCursor>(series.GetEnumerator(), constant);
            return cursor.Source;
        }

        /// <summary>
        /// Divide operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ArithmeticCursor<TKey, TValue, DivideOp<TValue>, TCursor>> operator
            /(CursorSeries<TKey, TValue, TCursor> series, TValue constant)
        {
            var cursor = new ArithmeticCursor<TKey, TValue, DivideOp<TValue>, TCursor>(series.GetEnumerator(), constant);
            return cursor.Source;
        }

        /// <summary>
        /// Divide operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ArithmeticCursor<TKey, TValue, DivideReverseOp<TValue>, TCursor>> operator
            /(TValue constant, CursorSeries<TKey, TValue, TCursor> series)
        {
            var cursor = new ArithmeticCursor<TKey, TValue, DivideReverseOp<TValue>, TCursor>(series.GetEnumerator(), constant);
            return cursor.Source;
        }

        /// <summary>
        /// Modulo operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ArithmeticCursor<TKey, TValue, ModuloOp<TValue>, TCursor>> operator
            %(CursorSeries<TKey, TValue, TCursor> series, TValue constant)
        {
            var cursor = new ArithmeticCursor<TKey, TValue, ModuloOp<TValue>, TCursor>(series.GetEnumerator(), constant);
            return cursor.Source;
        }

        /// <summary>
        /// Modulo operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ArithmeticCursor<TKey, TValue, ModuloReverseOp<TValue>, TCursor>> operator
            %(TValue constant, CursorSeries<TKey, TValue, TCursor> series)
        {
            var cursor = new ArithmeticCursor<TKey, TValue, ModuloReverseOp<TValue>, TCursor>(series.GetEnumerator(), constant);
            return cursor.Source;
        }

        // UNARY LOGIC

        /// <summary>
        /// Values equal operator. Use ReferenceEquals or SequenceEquals for other cases.
        /// </summary>
        public static CursorSeries<TKey, bool, ComparisonCursor<TKey, TValue, TCursor>> operator
            ==(CursorSeries<TKey, TValue, TCursor> series, TValue comparand)
        {
            var cursor = new ComparisonCursor<TKey, TValue, TCursor>(series.GetEnumerator(), comparand, EQOp<TValue>.Instance);
            return cursor.Source;
        }

        /// <summary>
        /// Values equal operator. Use ReferenceEquals or SequenceEquals for other cases.
        /// </summary>
        public static CursorSeries<TKey, bool, ComparisonCursor<TKey, TValue, TCursor>> operator
            ==(TValue comparand, CursorSeries<TKey, TValue, TCursor> series)
        {
            var cursor = new ComparisonCursor<TKey, TValue, TCursor>(series.GetEnumerator(), comparand, EQOp<TValue>.Instance);
            return cursor.Source;
        }

        /// <summary>
        /// Values not equal operator. Use !ReferenceEquals or !SequenceEquals for other cases.
        /// </summary>
        public static CursorSeries<TKey, bool, ComparisonCursor<TKey, TValue, TCursor>> operator
            !=(CursorSeries<TKey, TValue, TCursor> series, TValue comparand)
        {
            var cursor = new ComparisonCursor<TKey, TValue, TCursor>(series.GetEnumerator(), comparand, NEQOp<TValue>.Instance);
            return cursor.Source;
        }

        /// <summary>
        /// Values not equal operator. Use !ReferenceEquals or !SequenceEquals for other cases.
        /// </summary>
        public static CursorSeries<TKey, bool, ComparisonCursor<TKey, TValue, TCursor>> operator
            !=(TValue comparand, CursorSeries<TKey, TValue, TCursor> series)
        {
            var cursor = new ComparisonCursor<TKey, TValue, TCursor>(series.GetEnumerator(), comparand, NEQOp<TValue>.Instance);
            return cursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ComparisonCursor<TKey, TValue, TCursor>> operator
            <(CursorSeries<TKey, TValue, TCursor> series, TValue comparand)
        {
            var cursor = new ComparisonCursor<TKey, TValue, TCursor>(series.GetEnumerator(), comparand, LTOp<TValue>.Instance);
            return cursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ComparisonCursor<TKey, TValue, TCursor>> operator
            <(TValue comparand, CursorSeries<TKey, TValue, TCursor> series)
        {
            var cursor = new ComparisonCursor<TKey, TValue, TCursor>(series.GetEnumerator(), comparand, LTReverseOp<TValue>.Instance);
            return cursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ComparisonCursor<TKey, TValue, TCursor>> operator
            >(CursorSeries<TKey, TValue, TCursor> series, TValue comparand)
        {
            var cursor = new ComparisonCursor<TKey, TValue, TCursor>(series.GetEnumerator(), comparand, GTOp<TValue>.Instance);
            return cursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ComparisonCursor<TKey, TValue, TCursor>> operator
            >(TValue comparand, CursorSeries<TKey, TValue, TCursor> series)
        {
            var cursor = new ComparisonCursor<TKey, TValue, TCursor>(series.GetEnumerator(), comparand, GTReverseOp<TValue>.Instance);
            return cursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ComparisonCursor<TKey, TValue, TCursor>> operator
            <=(CursorSeries<TKey, TValue, TCursor> series, TValue comparand)
        {
            var cursor = new ComparisonCursor<TKey, TValue, TCursor>(series.GetEnumerator(), comparand, LEOp<TValue>.Instance);
            return cursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ComparisonCursor<TKey, TValue, TCursor>> operator
            <=(TValue comparand, CursorSeries<TKey, TValue, TCursor> series)
        {
            var cursor = new ComparisonCursor<TKey, TValue, TCursor>(series.GetEnumerator(), comparand, LEReverseOp<TValue>.Instance);
            return cursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ComparisonCursor<TKey, TValue, TCursor>> operator >=(CursorSeries<TKey, TValue, TCursor> series, TValue comparand)
        {
            var cursor = new ComparisonCursor<TKey, TValue, TCursor>(series.GetEnumerator(), comparand, GEOp<TValue>.Instance);
            return cursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ComparisonCursor<TKey, TValue, TCursor>> operator
            >=(TValue comparand, CursorSeries<TKey, TValue, TCursor> series)
        {
            var cursor = new ComparisonCursor<TKey, TValue, TCursor>(series.GetEnumerator(), comparand, GEReverseOp<TValue>.Instance);
            return cursor.Source;
        }

        #endregion Unary Operators

        #region Binary Operators

        // BINARY ARITHMETIC

        /// <summary>
        /// Add operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ZipCursor<TKey, TValue, TValue, TValue, TCursor, TCursor>> operator
            +(CursorSeries<TKey, TValue, TCursor> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            var c1 = series.GetEnumerator();
            var c2 = other.GetEnumerator();
            Func<TKey, TValue, TValue, TValue> selector = AddOp<TValue>.ZipSelector;

            var zipCursor = new ZipCursor<TKey, TValue, TValue, TValue, TCursor, TCursor>(c1, c2, selector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Add operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ZipCursor<TKey, TValue, TValue, TValue, Cursor<TKey, TValue>, TCursor>> operator
            +(BaseSeries<TKey, TValue> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            var c1 = series.GetWrapper();
            var c2 = other.GetEnumerator();
            Func<TKey, TValue, TValue, TValue> selector = AddOp<TValue>.ZipSelector;

            var zipCursor = new ZipCursor<TKey, TValue, TValue, TValue, Cursor<TKey, TValue>, TCursor>(c1, c2, selector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Add operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ZipCursor<TKey, TValue, TValue, TValue, TCursor, Cursor<TKey, TValue>>> operator
            +(CursorSeries<TKey, TValue, TCursor> series, BaseSeries<TKey, TValue> other)
        {
            var c1 = series.GetEnumerator();
            var c2 = other.GetWrapper();
            Func<TKey, TValue, TValue, TValue> selector = AddOp<TValue>.ZipSelector;

            var zipCursor = new ZipCursor<TKey, TValue, TValue, TValue, TCursor, Cursor<TKey, TValue>>(c1, c2, selector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Subtract operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ZipCursor<TKey, TValue, TValue, TValue, TCursor, TCursor>> operator
            -(CursorSeries<TKey, TValue, TCursor> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            var c1 = series.GetEnumerator();
            var c2 = other.GetEnumerator();
            Func<TKey, TValue, TValue, TValue> selector = SubtractOp<TValue>.ZipSelector;

            var zipCursor = new ZipCursor<TKey, TValue, TValue, TValue, TCursor, TCursor>(c1, c2, selector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Subtract operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ZipCursor<TKey, TValue, TValue, TValue, Cursor<TKey, TValue>, TCursor>> operator
            -(BaseSeries<TKey, TValue> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            var c1 = series.GetWrapper();
            var c2 = other.GetEnumerator();
            Func<TKey, TValue, TValue, TValue> selector = SubtractOp<TValue>.ZipSelector;

            var zipCursor = new ZipCursor<TKey, TValue, TValue, TValue, Cursor<TKey, TValue>, TCursor>(c1, c2, selector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Subtract operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ZipCursor<TKey, TValue, TValue, TValue, TCursor, Cursor<TKey, TValue>>> operator
            -(CursorSeries<TKey, TValue, TCursor> series, BaseSeries<TKey, TValue> other)
        {
            var c1 = series.GetEnumerator();
            var c2 = other.GetWrapper();
            Func<TKey, TValue, TValue, TValue> selector = SubtractOp<TValue>.ZipSelector;

            var zipCursor = new ZipCursor<TKey, TValue, TValue, TValue, TCursor, Cursor<TKey, TValue>>(c1, c2, selector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Multiply operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ZipCursor<TKey, TValue, TValue, TValue, TCursor, TCursor>> operator
            *(CursorSeries<TKey, TValue, TCursor> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            var c1 = series.GetEnumerator();
            var c2 = other.GetEnumerator();
            Func<TKey, TValue, TValue, TValue> selector = MultiplyOp<TValue>.ZipSelector;

            var zipCursor = new ZipCursor<TKey, TValue, TValue, TValue, TCursor, TCursor>(c1, c2, selector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Multiply operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ZipCursor<TKey, TValue, TValue, TValue, Cursor<TKey, TValue>, TCursor>> operator
            *(BaseSeries<TKey, TValue> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            var c1 = series.GetWrapper();
            var c2 = other.GetEnumerator();
            Func<TKey, TValue, TValue, TValue> selector = MultiplyOp<TValue>.ZipSelector;

            var zipCursor = new ZipCursor<TKey, TValue, TValue, TValue, Cursor<TKey, TValue>, TCursor>(c1, c2, selector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Multiply operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ZipCursor<TKey, TValue, TValue, TValue, TCursor, Cursor<TKey, TValue>>> operator
            *(CursorSeries<TKey, TValue, TCursor> series, BaseSeries<TKey, TValue> other)
        {
            var c1 = series.GetEnumerator();
            var c2 = other.GetWrapper();
            Func<TKey, TValue, TValue, TValue> selector = MultiplyOp<TValue>.ZipSelector;

            var zipCursor = new ZipCursor<TKey, TValue, TValue, TValue, TCursor, Cursor<TKey, TValue>>(c1, c2, selector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Divide operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ZipCursor<TKey, TValue, TValue, TValue, TCursor, TCursor>> operator
            /(CursorSeries<TKey, TValue, TCursor> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            var c1 = series.GetEnumerator();
            var c2 = other.GetEnumerator();
            Func<TKey, TValue, TValue, TValue> selector = DivideOp<TValue>.ZipSelector;

            var zipCursor = new ZipCursor<TKey, TValue, TValue, TValue, TCursor, TCursor>(c1, c2, selector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Divide operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ZipCursor<TKey, TValue, TValue, TValue, Cursor<TKey, TValue>, TCursor>> operator
            /(BaseSeries<TKey, TValue> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            var c1 = series.GetWrapper();
            var c2 = other.GetEnumerator();
            Func<TKey, TValue, TValue, TValue> selector = DivideOp<TValue>.ZipSelector;

            var zipCursor = new ZipCursor<TKey, TValue, TValue, TValue, Cursor<TKey, TValue>, TCursor>(c1, c2, selector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Divide operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ZipCursor<TKey, TValue, TValue, TValue, TCursor, Cursor<TKey, TValue>>> operator
            /(CursorSeries<TKey, TValue, TCursor> series, BaseSeries<TKey, TValue> other)
        {
            var c1 = series.GetEnumerator();
            var c2 = other.GetWrapper();
            Func<TKey, TValue, TValue, TValue> selector = DivideOp<TValue>.ZipSelector;

            var zipCursor = new ZipCursor<TKey, TValue, TValue, TValue, TCursor, Cursor<TKey, TValue>>(c1, c2, selector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Modulo operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ZipCursor<TKey, TValue, TValue, TValue, TCursor, TCursor>> operator
            %(CursorSeries<TKey, TValue, TCursor> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            var c1 = series.GetEnumerator();
            var c2 = other.GetEnumerator();
            Func<TKey, TValue, TValue, TValue> selector = ModuloOp<TValue>.ZipSelector;

            var zipCursor = new ZipCursor<TKey, TValue, TValue, TValue, TCursor, TCursor>(c1, c2, selector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Modulo operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ZipCursor<TKey, TValue, TValue, TValue, Cursor<TKey, TValue>, TCursor>> operator
            %(BaseSeries<TKey, TValue> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            var c1 = series.GetWrapper();
            var c2 = other.GetEnumerator();
            Func<TKey, TValue, TValue, TValue> selector = ModuloOp<TValue>.ZipSelector;

            var zipCursor = new ZipCursor<TKey, TValue, TValue, TValue, Cursor<TKey, TValue>, TCursor>(c1, c2, selector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Modulo operator.
        /// </summary>
        public static CursorSeries<TKey, TValue, ZipCursor<TKey, TValue, TValue, TValue, TCursor, Cursor<TKey, TValue>>> operator
            %(CursorSeries<TKey, TValue, TCursor> series, BaseSeries<TKey, TValue> other)
        {
            var c1 = series.GetEnumerator();
            var c2 = other.GetWrapper();
            Func<TKey, TValue, TValue, TValue> selector = ModuloOp<TValue>.ZipSelector;

            var zipCursor = new ZipCursor<TKey, TValue, TValue, TValue, TCursor, Cursor<TKey, TValue>>(c1, c2, selector);
            return zipCursor.Source;
        }

        // BINARY LOGIC

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ZipCursor<TKey, TValue, TValue, bool, TCursor, TCursor>> operator
            ==(CursorSeries<TKey, TValue, TCursor> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            var c1 = series.GetEnumerator();
            var c2 = other.GetEnumerator();

            var zipCursor = new ZipCursor<TKey, TValue, TValue, bool, TCursor, TCursor>(c1, c2, EQOp<TValue>.ZipSelector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ZipCursor<TKey, TValue, TValue, bool, TCursor, Cursor<TKey, TValue>>> operator
            ==(CursorSeries<TKey, TValue, TCursor> series, BaseSeries<TKey, TValue> other)
        {
            if (ReferenceEquals(other, null)) throw new ArgumentNullException(nameof(other));
            var c1 = series.GetEnumerator();
            var c2 = other.GetWrapper();

            var zipCursor = new ZipCursor<TKey, TValue, TValue, bool, TCursor, Cursor<TKey, TValue>>(c1, c2, EQOp<TValue>.ZipSelector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ZipCursor<TKey, TValue, TValue, bool, Cursor<TKey, TValue>, TCursor>> operator
            ==(BaseSeries<TKey, TValue> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            if (ReferenceEquals(series, null)) throw new ArgumentNullException(nameof(series));
            var c1 = series.GetWrapper();
            var c2 = other.GetEnumerator();

            var zipCursor = new ZipCursor<TKey, TValue, TValue, bool, Cursor<TKey, TValue>, TCursor>(c1, c2, EQOp<TValue>.ZipSelector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ZipCursor<TKey, TValue, TValue, bool, TCursor, TCursor>> operator
            !=(CursorSeries<TKey, TValue, TCursor> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            var c1 = series.GetEnumerator();
            var c2 = other.GetEnumerator();

            var zipCursor = new ZipCursor<TKey, TValue, TValue, bool, TCursor, TCursor>(c1, c2, NEQOp<TValue>.ZipSelector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ZipCursor<TKey, TValue, TValue, bool, TCursor, Cursor<TKey, TValue>>> operator
            !=(CursorSeries<TKey, TValue, TCursor> series, BaseSeries<TKey, TValue> other)
        {
            if (ReferenceEquals(other, null)) throw new ArgumentNullException(nameof(other));
            var c1 = series.GetEnumerator();
            var c2 = other.GetWrapper();

            var zipCursor = new ZipCursor<TKey, TValue, TValue, bool, TCursor, Cursor<TKey, TValue>>(c1, c2, NEQOp<TValue>.ZipSelector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ZipCursor<TKey, TValue, TValue, bool, Cursor<TKey, TValue>, TCursor>> operator
            !=(BaseSeries<TKey, TValue> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            if (ReferenceEquals(series, null)) throw new ArgumentNullException(nameof(series));
            var c1 = series.GetWrapper();
            var c2 = other.GetEnumerator();

            var zipCursor = new ZipCursor<TKey, TValue, TValue, bool, Cursor<TKey, TValue>, TCursor>(c1, c2, NEQOp<TValue>.ZipSelector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ZipCursor<TKey, TValue, TValue, bool, TCursor, TCursor>> operator
            <=(CursorSeries<TKey, TValue, TCursor> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            var c1 = series.GetEnumerator();
            var c2 = other.GetEnumerator();

            var zipCursor = new ZipCursor<TKey, TValue, TValue, bool, TCursor, TCursor>(c1, c2, LEOp<TValue>.ZipSelector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ZipCursor<TKey, TValue, TValue, bool, TCursor, Cursor<TKey, TValue>>> operator
            <=(CursorSeries<TKey, TValue, TCursor> series, BaseSeries<TKey, TValue> other)
        {
            if (ReferenceEquals(other, null)) throw new ArgumentNullException(nameof(other));
            var c1 = series.GetEnumerator();
            var c2 = other.GetWrapper();

            var zipCursor = new ZipCursor<TKey, TValue, TValue, bool, TCursor, Cursor<TKey, TValue>>(c1, c2, LEOp<TValue>.ZipSelector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ZipCursor<TKey, TValue, TValue, bool, Cursor<TKey, TValue>, TCursor>> operator
            <=(BaseSeries<TKey, TValue> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            if (ReferenceEquals(series, null)) throw new ArgumentNullException(nameof(series));
            var c1 = series.GetWrapper();
            var c2 = other.GetEnumerator();

            var zipCursor = new ZipCursor<TKey, TValue, TValue, bool, Cursor<TKey, TValue>, TCursor>(c1, c2, LEOp<TValue>.ZipSelector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ZipCursor<TKey, TValue, TValue, bool, TCursor, TCursor>> operator
            >=(CursorSeries<TKey, TValue, TCursor> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            var c1 = series.GetEnumerator();
            var c2 = other.GetEnumerator();

            var zipCursor = new ZipCursor<TKey, TValue, TValue, bool, TCursor, TCursor>(c1, c2, GEOp<TValue>.ZipSelector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ZipCursor<TKey, TValue, TValue, bool, TCursor, Cursor<TKey, TValue>>> operator
            >=(CursorSeries<TKey, TValue, TCursor> series, BaseSeries<TKey, TValue> other)
        {
            if (ReferenceEquals(other, null)) throw new ArgumentNullException(nameof(other));
            var c1 = series.GetEnumerator();
            var c2 = other.GetWrapper();

            var zipCursor = new ZipCursor<TKey, TValue, TValue, bool, TCursor, Cursor<TKey, TValue>>(c1, c2, GEOp<TValue>.ZipSelector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ZipCursor<TKey, TValue, TValue, bool, Cursor<TKey, TValue>, TCursor>> operator
            >=(BaseSeries<TKey, TValue> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            if (ReferenceEquals(series, null)) throw new ArgumentNullException(nameof(series));
            var c1 = series.GetWrapper();
            var c2 = other.GetEnumerator();

            var zipCursor = new ZipCursor<TKey, TValue, TValue, bool, Cursor<TKey, TValue>, TCursor>(c1, c2, GEOp<TValue>.ZipSelector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ZipCursor<TKey, TValue, TValue, bool, TCursor, TCursor>> operator
            <(CursorSeries<TKey, TValue, TCursor> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            var c1 = series.GetEnumerator();
            var c2 = other.GetEnumerator();

            var zipCursor = new ZipCursor<TKey, TValue, TValue, bool, TCursor, TCursor>(c1, c2, LTOp<TValue>.ZipSelector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ZipCursor<TKey, TValue, TValue, bool, TCursor, Cursor<TKey, TValue>>> operator
            <(CursorSeries<TKey, TValue, TCursor> series, BaseSeries<TKey, TValue> other)
        {
            if (ReferenceEquals(other, null)) throw new ArgumentNullException(nameof(other));
            var c1 = series.GetEnumerator();
            var c2 = other.GetWrapper();

            var zipCursor = new ZipCursor<TKey, TValue, TValue, bool, TCursor, Cursor<TKey, TValue>>(c1, c2, LTOp<TValue>.ZipSelector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ZipCursor<TKey, TValue, TValue, bool, Cursor<TKey, TValue>, TCursor>> operator
            <(BaseSeries<TKey, TValue> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            if (ReferenceEquals(series, null)) throw new ArgumentNullException(nameof(series));
            var c1 = series.GetWrapper();
            var c2 = other.GetEnumerator();

            var zipCursor = new ZipCursor<TKey, TValue, TValue, bool, Cursor<TKey, TValue>, TCursor>(c1, c2, LTOp<TValue>.ZipSelector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ZipCursor<TKey, TValue, TValue, bool, TCursor, TCursor>> operator
            >(CursorSeries<TKey, TValue, TCursor> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            var c1 = series.GetEnumerator();
            var c2 = other.GetEnumerator();

            var zipCursor = new ZipCursor<TKey, TValue, TValue, bool, TCursor, TCursor>(c1, c2, GTOp<TValue>.ZipSelector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ZipCursor<TKey, TValue, TValue, bool, TCursor, Cursor<TKey, TValue>>> operator
            >(CursorSeries<TKey, TValue, TCursor> series, BaseSeries<TKey, TValue> other)
        {
            if (ReferenceEquals(other, null)) throw new ArgumentNullException(nameof(other));
            var c1 = series.GetEnumerator();
            var c2 = other.GetWrapper();

            var zipCursor = new ZipCursor<TKey, TValue, TValue, bool, TCursor, Cursor<TKey, TValue>>(c1, c2, GTOp<TValue>.ZipSelector);
            return zipCursor.Source;
        }

        /// <summary>
        /// Comparison operator.
        /// </summary>
        public static CursorSeries<TKey, bool, ZipCursor<TKey, TValue, TValue, bool, Cursor<TKey, TValue>, TCursor>> operator
            >(BaseSeries<TKey, TValue> series, CursorSeries<TKey, TValue, TCursor> other)
        {
            if (ReferenceEquals(series, null)) throw new ArgumentNullException(nameof(series));
            var c1 = series.GetWrapper();
            var c2 = other.GetEnumerator();

            var zipCursor = new ZipCursor<TKey, TValue, TValue, bool, Cursor<TKey, TValue>, TCursor>(c1, c2, GTOp<TValue>.ZipSelector);
            return zipCursor.Source;
        }

        #endregion Binary Operators

        #region Implicit cast

        /// <summary>
        /// Implicitly convert specialized <see cref="CursorSeries{TKey,TValue,TCursor}"/> to <see cref="CursorSeries{TKey,TValue,TCursor}"/>
        /// with <see cref="Cursor{TKey,TValue}"/> as <typeparamref name="TCursor"/>.
        /// using <see cref="Cursor{TKey,TValue}"/> wrapper.
        /// </summary>
        public static implicit operator CursorSeries<TKey, TValue, Cursor<TKey, TValue>>(CursorSeries<TKey, TValue, TCursor> series)
        {
            var c = new Cursor<TKey, TValue>(series._cursor);
            return new CursorSeries<TKey, TValue, Cursor<TKey, TValue>>(c);
        }

        #endregion Implicit cast
    }
}
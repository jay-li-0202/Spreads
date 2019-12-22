﻿// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Spreads.Collections.Internal;
using Spreads.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Spreads
{
    public class AppendSeries<TKey, TValue> : Series<TKey, TValue>, IAppendSeries<TKey, TValue>
    {
        private static readonly int DefaultMaxBlockRowCount = Math.Max(Settings.MIN_POOLED_BUFFER_LEN, Settings.LARGE_BUFFER_LIMIT / Math.Max(Unsafe.SizeOf<TKey>(), Unsafe.SizeOf<TValue>()));

        internal WindowOptions? Options;

        #region Public ctors

        public AppendSeries() :
            this(Mutability.AppendOnly)
        {
        }

        public AppendSeries(int capacity)
            : this(Mutability.AppendOnly, KeySorting.Strong, (uint)capacity)
        {
        }

        public AppendSeries(KeyComparer<TKey> comparer)
            : this(Mutability.AppendOnly, KeySorting.Strong, 0, comparer)
        {
        }

        public AppendSeries(int capacity, KeyComparer<TKey> comparer)
            : this(Mutability.AppendOnly, KeySorting.Strong, (uint)capacity, comparer)
        {
        }

        public AppendSeries(KeySorting keySorting)
            : this(Mutability.AppendOnly, keySorting)
        {
        }

        public AppendSeries(KeyComparer<TKey> comparer, KeySorting keySorting)
            : this(Mutability.AppendOnly, keySorting, 0, comparer, default)
        {
        }

        public AppendSeries(int capacity, KeyComparer<TKey> comparer, KeySorting keySorting)
            : this(Mutability.AppendOnly, keySorting, (uint)capacity, comparer, default)
        {
        }

        public AppendSeries(MovingWindowOptions<TKey> movingWindowOptions)
            : this(Mutability.AppendOnly, KeySorting.Strong, 0, default, movingWindowOptions)
        {
        }

        public AppendSeries(KeySorting keySorting, MovingWindowOptions<TKey> movingWindowOptions)
            : this(Mutability.AppendOnly, keySorting, 0, default, movingWindowOptions)
        {
        }

        public AppendSeries(KeyComparer<TKey> comparer, KeySorting keySorting, MovingWindowOptions<TKey> movingWindowOptions)
            : this(Mutability.AppendOnly, keySorting, 0, comparer, movingWindowOptions)
        {
        }

        #endregion Public ctors

        protected AppendSeries(Mutability mutability = Mutability.ReadOnly,
            KeySorting keySorting = KeySorting.Strong,
            uint capacity = 0,
            KeyComparer<TKey> comparer = default,
            MovingWindowOptions<TKey>? movingWindowOptions = default) : base(mutability, keySorting, capacity, comparer, movingWindowOptions)
        {
            if (movingWindowOptions != null)
            {
                Options = new WindowOptions(movingWindowOptions);
            }
        }

        /// <summary>
        /// Thread-unsafe equivalent of <see cref="TryAppend"/>.
        /// 2x faster but concurrent access could corrupt data or
        /// readers could get wrong results.
        /// Use it only when single-threaded access is guaranteed
        /// or from inside an action passed to <see cref="Series{TKey,TValue}.Lock"/> method.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining
#if HAS_AGGR_OPT
                    | MethodImplOptions.AggressiveOptimization
#endif
        )]
        public bool DangerousTryAppend(TKey key, TValue value)
        {
            if (Mutability == Mutability.ReadOnly)
            {
                return false;
            }

            if (!IsDataBlock(out var db, out var ds))
            {
                db = ds.LastValueOrDefault!;
            }

#if BUILTIN_NULLABLE
            Debug.Assert(db != null, "Data source must not be set if empty");
#endif
            var dbRowCount = db.RowCount;
            if (dbRowCount > 0)
            {
                var lastKey = db.DangerousRowKeyRef<TKey>(dbRowCount - 1);

                var c = _comparer.Compare(key, lastKey);
                if (c <= 0 // faster path is c > 0
                    && (c < 0 & KeySorting == KeySorting.Weak)
                    | // no short-circuit && or || here
                    (c == 0 & KeySorting == KeySorting.Strong)
                    )
                {
                    // TODO detect which condition caused that in Append.ThrowCannotAppend
                    return false;
                }
            }

            if (dbRowCount == db.RowCapacity)
            {
                if (!TryGrowCapacity(key, ref db))
                {
                    return false;
                }
            }
            Options?.OnBeforeAppend();
            db.SeriesAppend(db.RowCount, key, value);
            return true;
        }

        internal int MaxBlockRowCount
        {
            get
            {
                if (Options?.MovingWindowOptions != null
                    && Options.MovingWindowOptions is MovingWindowOptions<TKey, TValue> typedMvo
                    && typedMvo.WindowBlockSize > 0)
                {
                    return typedMvo.WindowBlockSize;
                }
                return DefaultMaxBlockRowCount;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining
#if HAS_AGGR_OPT
                     | MethodImplOptions.AggressiveOptimization
#endif
        )]
        private bool TryGrowCapacity(TKey key, [NotNullWhen(true)] ref DataBlock? block)
        {
            ThrowHelper.AssertFailFast(block != null);

            try
            {
                if (block == DataBlock.Empty)
                {
                    block = DataBlock.SeriesCreate(rowLength: 0);
                    Data = block;
                }

                // TODO review: do we want buffers in LOH or not? <= vs <
                // next increment will be 64kb, avoid buffer in LOH

                // ReSharper disable once PossibleNullReferenceException
                if (block.RowCapacity < MaxBlockRowCount)
                {
                    if (block.SeriesIncreaseCapacity<TKey, TValue>() < 0)
                    {
                        block = null;
                        return false;
                    }
                }
                else
                {
                    // refactor switching to source logic to reuse in MutableSeries
                    if (IsDataBlock(out var db, out var ds))
                    {
                        Debug.Assert(ReferenceEquals(block, db));

                        ds = new DataBlockSource<TKey>();
                        ds.AddLast(block.DangerousRowKeyRef<TKey>(0), block);
                        Data = ds;
                    }

                    var minCapacity = block.RowCapacity;
                    var newBlock = DataBlock.SeriesCreate(rowLength: 0);
                    if (newBlock.SeriesIncreaseCapacity<TKey, TValue>(minCapacity) < 0)
                    {
                        block = null;
                        return false;
                    }

                    if (Options?.MovingWindowOptions != null)
                    {
                        // before adding new block try to remove first blocks that
                        // are not used and satisfy MovingWindowOptions
                        TryTrimWindowBlocks();
                    }

                    ds.AddLast(key, newBlock);
                    block = newBlock;
                }

                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
                block = null;
                return false;
            }
        }

        /// <inheritdoc />
#if HAS_AGGR_OPT
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif

        public bool TryAppend(TKey key, TValue value)
        {
            // We do not need to increment version in append as long as DataBlock counter
            // is incremented as the last and volatile operation (_rowCount is volatile).
            AcquireLock();
            try
            {
                return DangerousTryAppend(key, value);
            }
            finally
            {
                ReleaseLock();
            }
        }

        /// <inheritdoc />
#if HAS_AGGR_OPT
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif

        public void Append(TKey key, TValue value)
        {
            if (!TryAppend(key, value))
            {
                ThrowCannotAppend(key, value);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ThrowCannotAppend(TKey key, TValue value)
        {
            if (Mutability == Mutability.ReadOnly)
            {
                ThrowHelper.ThrowInvalidOperationException("Cannot append values to read-only series.");
            }

            if (TryGetValue(key, out _) && KeySorting == KeySorting.Strong)
            {
                ThrowHelper.ThrowArgumentException($"Cannot append [{key}, {value}]. Key already exists.");
            }

            var last = Last;
            if (last.IsPresent)
            {
                var c = Comparer.Compare(key, Last.Present.Key);
                if ((c < 0 && KeySorting != KeySorting.NotSorted)
                    || (c == 0 && KeySorting == KeySorting.Strong))
                {
                    ThrowHelper.ThrowArgumentException($"Cannot append [{key}, {value}]. Key [{key}] would break sorting order {KeySorting}.");
                }
            }

            ThrowHelper.ThrowInvalidOperationException($"Cannot append [{key}, {value}].");
        }

        /// <inheritdoc />
#if HAS_AGGR_OPT
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif

        public bool TryAppendMany<TPairs>(TPairs pairs) where TPairs : IEnumerable<KeyValuePair<TKey, TValue>>
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
#if HAS_AGGR_OPT
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif

        public void AppendMany<TPairs>(TPairs pairs) where TPairs : IEnumerable<KeyValuePair<TKey, TValue>>
        {
            if (!TryAppendMany(pairs))
            {
                ThrowCannotAppend();
            }
            void ThrowCannotAppend()
            {
                if (Mutability == Mutability.ReadOnly)
                {
                    ThrowHelper.ThrowInvalidOperationException("Cannot append values to read-only series.");
                }

                ThrowHelper.ThrowInvalidOperationException($"Cannot append key-value pair");
            }
        }

        /// <inheritdoc />
        public void MarkReadOnly()
        {
            Flags.MarkReadOnly();
        }

        internal class WindowOptions : ISpreadsThreadPoolWorkItem
        {
            public readonly MovingWindowOptions<TKey> MovingWindowOptions;
            public DataBlock? LingeringDataBlock;
            private long _counter;

            public WindowOptions(MovingWindowOptions<TKey> movingWindowOptions)
            {
                MovingWindowOptions = movingWindowOptions ?? throw new ArgumentNullException(nameof(movingWindowOptions));
            }

            public void OnBeforeAppend()
            {
                _counter++;
                if (MovingWindowOptions is MovingWindowOptions<TKey, TValue> typedOpts
                    && typedOpts.OnRemovedHandler != null)
                {
                    SpreadsThreadPool.Default.UnsafeQueueCompletableItem(this, true);
                }
            }

            public void OnBeforeNewBlock()
            {
                if (LingeringDataBlock != null)
                {
                    // TODO handle remaining block items and the block itself
                    // before
                }
                if (MovingWindowOptions != null
                    )
                {
                    _counter++;
                }
            }

            public void Execute()
            {
                // we cannot trust delegates to call them from inside lock
                // also cleaning up items after removal is background job semantically

                ;
            }
        }

        private void TryTrimWindowBlocks()
        {
            var options = Options?.MovingWindowOptions!;
            if (IsDataBlock(out _, out var ds) || options == null)
            {
                throw new InvalidOperationException();
            }

            var bs = ds._blockSeries;

            // find deepest DS level
            DataBlock? bsDb;
            while (!bs.IsDataBlock(out bsDb, out var bsDs))
            {
                bs = bsDs._blockSeries;
            }

            // bsDb is from where we should delete the first data block
            bsDb.SeriesTrimFirstValue<TKey, DataBlock>(out _, out _);

            var firstBlock = ds._blockSeries.First;
        }
    }
}
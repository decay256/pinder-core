using System;
using System.Collections.Generic;

namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Transactional replay adapter for an arbitrary <see cref="Random"/> supplied
    /// through public game-session configuration. Calls are delegated to the original
    /// RNG exactly once and journalled so one required-turn working copy can be
    /// discarded or adopted without advancing the parent's cursor.
    ///
    /// This adapter does not provide independent speculative branches: transaction
    /// forks share the operation-shaped journal and are valid only for replaying the
    /// same required-turn path. Public <c>GameSession.Clone()</c> therefore rejects it.
    /// </summary>
    internal sealed class ForkableRandom : Random
    {
        private readonly Journal _journal;
        private int _cursor;

        private ForkableRandom(Random source)
        {
            _journal = new Journal(source);
        }

        private ForkableRandom(Journal journal, int cursor)
        {
            _journal = journal;
            _cursor = cursor;
        }

        internal static Random Adapt(Random source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source is ForkableRandom || source is CloneableRandom)
                return source;
            return new ForkableRandom(source);
        }

        internal static Random ForkForRequiredTurnTransaction(Random source, string paramName)
        {
            EnsureCanForkForRequiredTurnTransaction(source, paramName);
            if (source is ForkableRandom forkable)
                return new ForkableRandom(forkable._journal, forkable._cursor);
            return ((CloneableRandom)source).Clone();
        }

        internal static Random ForkForIndependentClone(Random source, string paramName)
        {
            EnsureCanForkForIndependentClone(source, paramName);
            return ((CloneableRandom)source).Clone();
        }

        internal static void EnsureCanForkForRequiredTurnTransaction(Random source, string paramName)
        {
            if (source == null) throw new ArgumentNullException(paramName);
            if (source is ForkableRandom || source is CloneableRandom)
                return;
            throw new InvalidOperationException(
                $"GameSession RNG '{paramName}' was not adapted for transactional cloning.");
        }

        internal static void EnsureCanForkForIndependentClone(Random source, string paramName)
        {
            if (source == null) throw new ArgumentNullException(paramName);
            if (source is CloneableRandom)
                return;
            if (source is ForkableRandom)
            {
                throw new InvalidOperationException(
                    $"GameSession RNG '{paramName}' uses an explicit System.Random. " +
                    "Required-turn rollback is supported, but independent speculative clones " +
                    "require CloneableRandom.");
            }

            throw new InvalidOperationException(
                $"GameSession RNG '{paramName}' cannot be independently cloned.");
        }

        public override int Next()
        {
            return ReplayOrRecord(Operation.Next, 0, 0, () => _journal.Source.Next());
        }

        public override int Next(int maxValue)
        {
            return ReplayOrRecord(Operation.NextMax, maxValue, 0, () => _journal.Source.Next(maxValue));
        }

        public override int Next(int minValue, int maxValue)
        {
            return ReplayOrRecord(
                Operation.NextRange,
                minValue,
                maxValue,
                () => _journal.Source.Next(minValue, maxValue));
        }

        public override double NextDouble()
        {
            lock (_journal.Sync)
            {
                var entry = GetExistingOrAppend(
                    Operation.NextDouble,
                    0,
                    0,
                    () => new Entry(Operation.NextDouble, 0, 0, doubleValue: _journal.Source.NextDouble()));
                return entry.DoubleValue;
            }
        }

        public override void NextBytes(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            lock (_journal.Sync)
            {
                var entry = GetExistingOrAppend(
                    Operation.NextBytes,
                    buffer.Length,
                    0,
                    () =>
                    {
                        var generated = new byte[buffer.Length];
                        _journal.Source.NextBytes(generated);
                        return new Entry(Operation.NextBytes, buffer.Length, 0, bytes: generated);
                    });
                Array.Copy(entry.Bytes!, buffer, buffer.Length);
            }
        }

        protected override double Sample()
        {
            return NextDouble();
        }

        private int ReplayOrRecord(Operation operation, int argument1, int argument2, Func<int> generate)
        {
            lock (_journal.Sync)
            {
                var entry = GetExistingOrAppend(
                    operation,
                    argument1,
                    argument2,
                    () => new Entry(operation, argument1, argument2, intValue: generate()));
                return entry.IntValue;
            }
        }

        private Entry GetExistingOrAppend(
            Operation operation,
            int argument1,
            int argument2,
            Func<Entry> generate)
        {
            if (_cursor < _journal.Entries.Count)
            {
                var existing = _journal.Entries[_cursor++];
                if (existing.Operation != operation ||
                    existing.Argument1 != argument1 ||
                    existing.Argument2 != argument2)
                {
                    throw new InvalidOperationException(
                        "A transactional RNG fork diverged from the prepared call sequence. " +
                        "Arbitrary System.Random remains deterministic only when required-turn " +
                        "transaction replays request the same operation shape.");
                }
                return existing;
            }

            var created = generate();
            _journal.Entries.Add(created);
            _cursor++;
            return created;
        }

        private enum Operation
        {
            Next,
            NextMax,
            NextRange,
            NextDouble,
            NextBytes,
        }

        private sealed class Journal
        {
            internal Journal(Random source)
            {
                Source = source;
            }

            internal object Sync { get; } = new object();
            internal Random Source { get; }
            internal List<Entry> Entries { get; } = new List<Entry>();
        }

        private sealed class Entry
        {
            internal Entry(
                Operation operation,
                int argument1,
                int argument2,
                int intValue = 0,
                double doubleValue = 0,
                byte[]? bytes = null)
            {
                Operation = operation;
                Argument1 = argument1;
                Argument2 = argument2;
                IntValue = intValue;
                DoubleValue = doubleValue;
                Bytes = bytes;
            }

            internal Operation Operation { get; }
            internal int Argument1 { get; }
            internal int Argument2 { get; }
            internal int IntValue { get; }
            internal double DoubleValue { get; }
            internal byte[]? Bytes { get; }
        }
    }
}

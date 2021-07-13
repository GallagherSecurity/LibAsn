using System;

#if !NETSTANDARD2_1_OR_GREATER

namespace Gallagher.LibAsn.Shims
{
    // compatibility shims for CoreClr types. We accept these will be slower, and we could take a dependency
    // on System.Memory, but that's often troublesome and we want to be a truly standalone library
    internal static class Ext
    {
        internal static T[] Slice<T>(this T[] array, int offset, int length)
        {
            var result = new T[length];
            Buffer.BlockCopy(array, offset, result, 0, length);
            return result;
        }

        internal static void CopyTo<T>(this T[] array, Span<T> output) => Buffer.BlockCopy(array, 0, output.BackingStore, output.Offset, array.Length);
    }

    internal struct Span<T>
    {
        internal Span(T[] backingStore, int offset, int length)
        {
            BackingStore = backingStore;
            Offset = offset;
            Length = length;
        }

        internal T[] BackingStore { get; }
        internal int Offset { get; }
        internal int Length { get; }

        public static implicit operator Span<T>(T[] backingStore) => new Span<T>(backingStore, 0, backingStore.Length);

        internal T this[int index]
        {
            get => BackingStore[index + Offset];
            set => BackingStore[index + Offset] = value;
        }

        internal Span<T> Slice(int offset, int length) => new Span<T>(BackingStore, Offset + offset, length);

        internal Span<T> Slice(int offset) => new Span<T>(BackingStore, Offset + offset, Length - offset); // slice until the end

        public SpanEnumerator<T> GetEnumerator() => new SpanEnumerator<T>(BackingStore, Offset, Length);
    }

    internal struct SpanEnumerator<T>
    {
        private readonly T[] m_backingStore;
        private readonly int m_endOffset;
        private int m_offset;

        public SpanEnumerator(T[] backingStore, int offset, int length)
        {
            m_backingStore = backingStore;
            m_offset = offset;
            m_endOffset = offset + length;
        }

        public T Current => m_backingStore[m_offset];

        public bool MoveNext()
        {
            var newOffset = m_offset + 1;
            if (newOffset >= m_endOffset)
            {
                return false;
            }
            else
            {
                m_offset = newOffset;
                return true;
            }
        }

        public void Reset() => throw new NotSupportedException("SpanEnumerator can't reset");
    }

    internal struct ReadOnlySpan<T>
    {
        internal ReadOnlySpan(T[] backingStore, int offset, int length)
        {
            BackingStore = backingStore;
            Offset = offset;
            Length = length;
        }

        internal ReadOnlySpan(T[] backingStore)
        {
            BackingStore = backingStore;
            Offset = 0;
            Length = backingStore.Length;
        }

        internal ReadOnlySpan(ArraySegment<T> segment)
        {
            BackingStore = segment.Array;
            Offset = segment.Offset;
            Length = segment.Count;
        }

        internal T[] BackingStore { get; }
        internal int Offset { get; }
        internal int Length { get; }

        public static implicit operator ReadOnlySpan<T>(T[] backingStore) => new ReadOnlySpan<T>(backingStore, 0, backingStore.Length);

        internal T this[int index] => BackingStore[index + Offset];

        internal ReadOnlySpan<T> Slice(int offset, int length) => new ReadOnlySpan<T>(BackingStore, Offset + offset, length);

        internal ReadOnlySpan<T> Slice(int offset) => new ReadOnlySpan<T>(BackingStore, Offset + offset, Length - offset); // slice until the end

        internal void CopyTo(Span<T> target) => Buffer.BlockCopy(BackingStore, Offset, target.BackingStore, target.Offset, Length);

        internal T[] ToArray()
        {
            var output = new T[Length];
            CopyTo(output);
            return output;
        }

        public SpanEnumerator<T> GetEnumerator() => new SpanEnumerator<T>(BackingStore, Offset, Length);
    }

    internal static class MemoryExtensions
    {
        public static bool SequenceEqual<T>(this ReadOnlySpan<T> a, ReadOnlySpan<T> b) where T : IEquatable<T>
        {
            if (a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (!a[i].Equals(b[i]))
                    return false;
            }

            return true;
        }
    }

    internal struct ReadOnlyMemory<T>
    {
        internal ReadOnlyMemory(T[] backingStore, int offset, int length)
        {
            BackingStore = backingStore;
            Offset = offset;
            Length = length;
        }
        internal ReadOnlyMemory(T[] backingStore)
        {
            BackingStore = backingStore;
            Offset = 0;
            Length = backingStore.Length;
        }

        internal T[] BackingStore { get; }
        internal int Offset { get; }
        internal int Length { get; }

        internal ReadOnlySpan<T> Span => new ReadOnlySpan<T>(BackingStore, Offset, Length);

        internal void CopyTo(T[] output) => Buffer.BlockCopy(BackingStore, Offset, output, 0, Length);

        internal ReadOnlyMemory<T> Slice(int offset, int length) => new ReadOnlyMemory<T>(BackingStore, Offset + offset, length);

        internal ReadOnlyMemory<T> Slice(int offset) => new ReadOnlyMemory<T>(BackingStore, Offset + offset, Length - offset); // slice until the end

        internal T[] ToArray()
        {
            var output = new T[Length];
            CopyTo(output);
            return output;
        }
    }
}
#endif
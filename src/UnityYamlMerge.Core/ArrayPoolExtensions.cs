using System.Buffers;
using System.Runtime.CompilerServices;

namespace UnityYamlMerge.Core;

internal static class ArrayPoolExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Handle<T> Rent<T>(this ArrayPool<T> pool, int minimumLength, out T[] array) => new(pool, array = pool.Rent(minimumLength));

    public readonly struct Handle<T> : IDisposable
    {
        private readonly ArrayPool<T> _pool;
        private readonly T[] _array;

        internal Handle(ArrayPool<T> pool, T[] array)
        {
            _pool = pool;
            _array = array;
        }

        /// <inheritdoc/>
        void IDisposable.Dispose() => _pool.Return(_array);
    }
}
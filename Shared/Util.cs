using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Shared
{
    public static class Util
    {
        public static string Hex(this Span<byte> span)
        {
            return span.ToArray().Hex();
        }

        public static string Hex(this IEnumerable<byte> array)
        {
            return string.Join(' ', array.ToArray().Select(x => x.ToString("X2")));
        }

        public static unsafe byte* Ptr(this Span<byte> span)
        {
            fixed (byte* data = span)
            {
                return data;
            }
        }

        public static string TrimNullTerm(this string text)
        {
            return text.TrimEnd('\0');
        }

        public static IMemoryOwner<byte> RentZero(this MemoryPool<byte> pool, int minSize)
        {
            IMemoryOwner<byte> owner = pool.Rent(minSize);
            CryptographicOperations.ZeroMemory(owner.Memory.Span);
            return owner;
        }
    }
}

namespace Sever
{
    namespace Server
    {
        public enum FlipOptions {
            Both,
            Self,
            Others
        }
        public record Time(ushort Minutes, byte Seconds);
    }
}
using System;
using System.Collections.Generic;
using System.Text;

namespace Conquer.Packets
{
    /// <summary>
    /// Managed port of the original CO <c>NetStringPacker</c>. Encodes a list of
    /// strings as <c>[byte count][byte len][ASCII bytes]...</c> — a 1-byte string
    /// count followed by, for each string, a 1-byte length then its ASCII bytes.
    /// Uses Span (no unsafe/memcpy). See <c>0b094c6:src/Redux/Packets/NetStringPacker.cs</c>.
    /// </summary>
    public sealed class NetStringPacker
    {
        private readonly List<string> _values = new List<string>();

        public NetStringPacker() { }

        public NetStringPacker(params string[] values)
        {
            if (values != null)
            {
                foreach (var v in values)
                    AddString(v ?? string.Empty);
            }
        }

        public bool AddString(string value)
        {
            value ??= string.Empty;
            if (value.Length > 255) return false;
            _values.Add(value);
            return true;
        }

        public int Count => _values.Count;

        /// <summary>Total encoded length: 1 (count) + per string (1 length byte + bytes).</summary>
        public int Length
        {
            get
            {
                int len = 1;
                for (int i = 0; i < _values.Count; i++)
                    len += 1 + _values[i].Length;
                return len;
            }
        }

        /// <summary>Encodes into <paramref name="dest"/>; returns bytes written.</summary>
        public int Write(Span<byte> dest)
        {
            int offset = 0;
            dest[offset++] = (byte)_values.Count;
            for (int i = 0; i < _values.Count; i++)
            {
                string value = _values[i];
                dest[offset++] = (byte)value.Length;
                int written = Encoding.ASCII.GetBytes(value, dest.Slice(offset));
                offset += written;
            }
            return offset;
        }

        public byte[] ToArray()
        {
            var buffer = new byte[Length];
            Write(buffer);
            return buffer;
        }
    }
}

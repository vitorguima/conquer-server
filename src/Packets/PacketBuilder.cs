using System;
using System.Buffers.Binary;

namespace Conquer.Packets
{
    /// <summary>
    /// Managed port of the original CO <c>PacketBuilder</c>. The on-wire header is
    /// <c>[ushort length][ushort type]</c> where the length field is the whole-frame
    /// size MINUS the 8-byte "TQServer" seal (verified in
    /// <c>0b094c6:src/Redux/Packets/PacketBuilder.cs</c>: <c>*(ushort*)ptr = size - 8</c>).
    /// </summary>
    public static class PacketBuilder
    {
        /// <summary>
        /// Writes the header into <paramref name="span"/>: length = <c>size - 8</c>
        /// (ushort LE) at offset 0, type (ushort LE) at offset 2.
        /// </summary>
        /// <param name="span">Target packet buffer.</param>
        /// <param name="size">Whole-frame size including the 8-byte seal.</param>
        /// <param name="type">Packet type id.</param>
        public static void AppendHeader(Span<byte> span, ushort size, ushort type)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(span, (ushort)(size - 8));
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), type);
        }
    }
}

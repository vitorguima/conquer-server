// Managed .NET 8 replacement for the native TinyMap.dll
// Reads the .TinyMap binary format produced by the original TinyMap tool.
//
// .TinyMap file format (as written by TinyMap/TinyMap.cs):
//   UInt16 MapID
//   UInt16 MaxX
//   UInt16 MaxY
//   MaxX * MaxY tile entries, each:
//     Byte Flag  (TileFlag: 0 = passable, non-zero = blocked / has special flag)
//
// The design.md spec describes a simplified ushort[] tile store — this implementation
// aligns with the real Redux file format while satisfying the design API.

using System;
using System.Collections.Generic;
using System.IO;

namespace Conquer.Maps
{
    /// <summary>
    /// Reads a .TinyMap binary file and exposes tile passability queries.
    /// </summary>
    public sealed class TinyMap
    {
        private readonly int _width;
        private readonly int _height;
        // Each element stores the raw tile flag byte; 0 = passable.
        private readonly byte[] _tiles;

        /// <summary>List of portals embedded in this map (may be empty).</summary>
        public List<TinyMapPortal> Portals { get; } = new List<TinyMapPortal>();

        public int Width  => _width;
        public int Height => _height;

        private TinyMap(int width, int height, byte[] tiles)
        {
            _width  = width;
            _height = height;
            _tiles  = tiles;
        }

        /// <summary>
        /// Loads a .TinyMap file and returns a <see cref="TinyMap"/> instance.
        /// </summary>
        /// <param name="filePath">Absolute or relative path to the .TinyMap file.</param>
        /// <param name="loadPortals">When true, portal records are read from the file.</param>
        public static TinyMap Load(string filePath, bool loadPortals = false)
        {
            using var br = new BinaryReader(File.OpenRead(filePath));

            // Header: MapID (ignored here, keyed externally), MaxX, MaxY
            br.ReadUInt16();             // MapID
            int width  = br.ReadUInt16();
            int height = br.ReadUInt16();

            var tiles = new byte[width * height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    tiles[y * width + x] = br.ReadByte(); // Flag
                    // Height byte not stored in base format; skip if present.
                    // For M1 we load without height (matches Redux default LoadHeight=false).
                }
            }

            var map = new TinyMap(width, height, tiles);

            if (loadPortals && br.BaseStream.Position < br.BaseStream.Length)
            {
                int portalCount = br.ReadInt32();
                for (int i = 0; i < portalCount; i++)
                {
                    uint portalId = br.ReadUInt32();
                    ushort px     = br.ReadUInt16();
                    ushort py     = br.ReadUInt16();
                    map.Portals.Add(new TinyMapPortal(portalId, px, py));
                }
            }

            return map;
        }

        /// <summary>
        /// Returns true when the tile at (x, y) is passable (flag == 0).
        /// Returns false when out-of-bounds or when the tile has any flag set.
        /// </summary>
        public bool IsPassable(int x, int y)
        {
            if (x < 0 || y < 0 || x >= _width || y >= _height)
                return false;
            return _tiles[y * _width + x] == 0;
        }

        /// <summary>
        /// Returns true when the tile exists and does NOT have the specified flag set.
        /// </summary>
        public bool HasFlag(int x, int y, TileFlag flag)
        {
            if (x < 0 || y < 0 || x >= _width || y >= _height)
                return false;
            return (_tiles[y * _width + x] & (byte)flag) != 0;
        }

        /// <summary>
        /// Sets the specified flag on the tile at (x, y). No-op if out-of-bounds.
        /// </summary>
        public void AddFlag(int x, int y, TileFlag flag)
        {
            if (x < 0 || y < 0 || x >= _width || y >= _height)
                return;
            _tiles[y * _width + x] |= (byte)flag;
        }

        /// <summary>
        /// Clears the specified flag on the tile at (x, y). No-op if out-of-bounds.
        /// </summary>
        public void RemoveFlag(int x, int y, TileFlag flag)
        {
            if (x < 0 || y < 0 || x >= _width || y >= _height)
                return;
            _tiles[y * _width + x] &= (byte)~(byte)flag;
        }
    }

    /// <summary>
    /// Tile accessibility flags matching the original TinyMap TileFlag enum.
    /// </summary>
    [Flags]
    public enum TileFlag : byte
    {
        None    = 0,
        Invalid = 1,   // Tile is impassable terrain
        Monster = 2,   // A monster occupies this tile
        Item    = 4,   // A ground item occupies this tile
        Player  = 8,   // A player occupies this tile
        Portal  = 16,  // Portal tile
    }

    /// <summary>
    /// Portal record embedded in a .TinyMap file.
    /// </summary>
    public sealed class TinyMapPortal
    {
        public uint   PortalId { get; }
        public ushort X        { get; }
        public ushort Y        { get; }

        public TinyMapPortal(uint portalId, ushort x, ushort y)
        {
            PortalId = portalId;
            X = x;
            Y = y;
        }
    }
}

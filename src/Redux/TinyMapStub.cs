// TODO-M1: TinyMap (native DLL) removed — stub classes for compilation only
// Real implementation will be added in task 1.12 (managed DMAP parser)
using System.Collections.Generic;

namespace Redux
{
    /// <summary>
    /// Stub for TinyMap native DLL — provides TileFlag enum and static name used in existing code.
    /// </summary>
    public static class TinyMap
    {
        public enum TileFlag : uint
        {
            None    = 0,
            Monster = 1,
            Item    = 2,
            Player  = 4,
        }
    }

    /// <summary>
    /// Stub portal data — used by GameServer.cs portal handling code.
    /// </summary>
    public class TinyMapPortalStub
    {
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public uint   PortalID { get; set; }
    }

    /// <summary>
    /// Stub map data — used by GameServer.cs and Player.cs ChangeMap.
    /// </summary>
    public class TinyMapDataStub
    {
        public List<TinyMapPortalStub> Portals { get; } = new List<TinyMapPortalStub>();
    }

    /// <summary>
    /// Stub service that replaces TinyMapServer — all methods are no-ops or return true for compilation.
    /// </summary>
    public class TinyMapService
    {
        // Stub map data dictionary — GameServer.cs does: Common.MapService.MapData[mapID].Portals
        public Dictionary<ushort, TinyMapDataStub> MapData { get; } = new Dictionary<ushort, TinyMapDataStub>();

        // Valid(mapId, x, y) — used in Map.cs, Player.cs, PetAI.cs, Commands.cs, Monster.cs, Pet.cs
        public bool Valid(ushort mapId, ushort x, ushort y) => true;

        // Valid(mapId, x, y, newX, newY) — used in Player.cs HandleJump with 5 args
        public bool Valid(ushort mapId, ushort x, ushort y, ushort newX, ushort newY) => true;

        // HasFlag — used in Map.cs, Monster.cs, Pet.cs
        public bool HasFlag(ushort mapId, ushort x, ushort y, TinyMap.TileFlag flag) => false;

        // AddFlag / RemoveFlag — used in GroundItem.cs, Monster.cs, Pet.cs
        public void AddFlag(ushort mapId, ushort x, ushort y, TinyMap.TileFlag flag) { }
        public void RemoveFlag(ushort mapId, ushort x, ushort y, TinyMap.TileFlag flag) { }

        // Overloads with int MapID (Monster.cs passes MapID which may be int/ushort in context)
        public bool Valid(int mapId, ushort x, ushort y) => true;
        public bool HasFlag(int mapId, ushort x, ushort y, TinyMap.TileFlag flag) => false;
        public void AddFlag(int mapId, ushort x, ushort y, TinyMap.TileFlag flag) { }
        public void RemoveFlag(int mapId, ushort x, ushort y, TinyMap.TileFlag flag) { }
    }
}

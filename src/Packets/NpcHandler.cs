using System;
using System.Buffers.Binary;
using Conquer.Network;

namespace Conquer.Packets
{
    /// <summary>
    /// Handles inbound MsgNpc(2031) NPC clicks. World-injected (mirror of
    /// <see cref="ChatHandler"/>/<see cref="WalkHandler"/>): guard the payload, resolve the
    /// clicker's trusted <see cref="Conquer.World.PlayerEntity"/>, read the clicked UID and the
    /// NpcEvent action, and — for Activate(0) only — validate the UID against the player's map
    /// roster (must be an <see cref="Conquer.World.NpcEntity"/>) and send the static v1 dialog
    /// sequence (Avatar + Text + Text + Finish) to the CLICKER only. Pure in-memory, additive.
    /// NEVER disconnects on bad/unknown/non-NPC input (guard + return).
    ///
    /// Payload (2-byte length prefix already stripped; payload[0..1]=typeId 2031):
    /// UID u32 LE @4 (clicked NPC), Data u32 @8 (ignored), Action u16 LE @12 (NpcEvent;
    /// Activate=0), Type u16 @14 (linkback; read-but-unused v1). Min payload = 16.
    /// </summary>
    public sealed class NpcHandler
    {
        private readonly Conquer.World.World _world;

        public NpcHandler(Conquer.World.World world)
        {
            _world = world;
        }

        /// <summary>Guard-first; validate UID + kind; static dialog to the clicker. Never disconnects.</summary>
        public void Handle(ClientSession session, byte[] payload)
        {
            if (payload.Length < 14) return;                                      // length guard (Rule 7)
            if (session.WorldEntity is not Conquer.World.PlayerEntity p) return;  // not in world yet

            // Payload has the 2-byte length prefix stripped → body offset − 2:
            // body UID@4 → payload@2, body Action@12 → payload@10 (mirrors WalkHandler UID@2).
            uint   npcUid = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(2, 4));
            ushort action = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(10, 2));

            var map = _world.GetOrAdd(p.MapId);
            bool found = map.Roster.TryGetValue(npcUid, out var e) && e is Conquer.World.NpcEntity;

            if (action != 0) return;                                              // Activate only
            if (!found || e is not Conquer.World.NpcEntity npc) return;           // validate UID + kind

            byte[][] dialog = NpcDialog.StaticSequence(
                1,                                  // face = placeholder (live-capture)
                $"Hello, I am {npc.Name}.",
                "Welcome, traveler.");
            foreach (var frame in dialog) session.SendGame(frame);                // clicker only
        }
    }
}

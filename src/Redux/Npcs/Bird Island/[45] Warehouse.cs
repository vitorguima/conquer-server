using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Redux.Packets.Game;

namespace Redux.Npcs
{

    // TODO-M1: renamed NPC_45 → NPC_45_BirdIslandWarehouse to avoid duplicate with Market [45]
    public class NPC_45_BirdIslandWarehouse : INpc
    {

        public NPC_45_BirdIslandWarehouse(Game_Server.Player _client)
            : base(_client)
        {
    		ID = 45;	
			Face = 5;    
    	}
    	
        public override void Run(Game_Server.Player _client, ushort _linkback)
        {
            _client.Send(GeneralActionPacket.Create(_client.UID, Enum.DataAction.OpenWindow, 4));
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;

namespace Conquer.Maps
{
    public static class MapRegistry
    {
        private static readonly Dictionary<int, TinyMap> _maps = new();

        public static void Load(string directory)
        {
            if (!Directory.Exists(directory))
                return;
            foreach (var file in Directory.GetFiles(directory, "*.cqmap"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (int.TryParse(name, out int mapId))
                    _maps[mapId] = TinyMap.Load(file);
            }
        }

        public static TinyMap? Get(int mapId)
            => _maps.TryGetValue(mapId, out var m) ? m : null;
    }
}

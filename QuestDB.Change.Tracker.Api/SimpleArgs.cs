using System.Collections.Generic;

namespace QuestDB.Change.Tracker.Api
{
    public class SimpleArgs
    {
        private readonly Dictionary<string, string> _map = new();

        public SimpleArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
                if (args[i].StartsWith("--") && i + 1 < args.Length)
                    _map[args[i]] = args[i + 1];
        }

        public string Get(string key, string def = null)
            => _map.TryGetValue(key, out var v) ? v : def;

        public int GetInt(string key, int def = 0)
            => int.TryParse(Get(key), out var v) ? v : def;
    }
}

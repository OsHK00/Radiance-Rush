using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace RadianceRush
{
    public class FightList
    {
        public List<string> Fights = new List<string>();

        public static string FilePath
        {
            get { return Path.Combine(Path.GetDirectoryName(typeof(FightList).Assembly.Location), "fights.json"); }
        }

        public static FightList Load()
        {
            return JsonConvert.DeserializeObject<FightList>(File.ReadAllText(FilePath)) ?? new FightList();
        }
    }
}

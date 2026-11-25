using System;
using System.Collections.Generic;
using Modding;

namespace RadianceRush
{
    public static class ModMenu
    {
        static readonly string[] NoYes = { "No", "Yes" };

        public static List<IMenuMod.MenuEntry> Build(Settings gs, Func<int> loadedMods)
        {
            var count = new[] { loadedMods().ToString() };
            return new List<IMenuMod.MenuEntry>
            {
                Entry("Enabled", NoYes, v => gs.Enabled = v == 1, () => gs.Enabled ? 1 : 0),
                Entry("Show counter", NoYes, v => gs.ShowCounter = v == 1, () => gs.ShowCounter ? 1 : 0),
                Entry("Auto continue", NoYes, v => gs.AutoContinue = v == 1, () => gs.AutoContinue ? 1 : 0),
                Entry("Carry HP, soul and Baldur", NoYes, v => gs.CarryOver = v == 1, () => gs.CarryOver ? 1 : 0),
                Entry("Loaded mods", count, v => { }, () => { count[0] = loadedMods().ToString(); return 0; })
            };
        }

        static IMenuMod.MenuEntry Entry(string name, string[] values, Action<int> saver, Func<int> loader)
        {
            return new IMenuMod.MenuEntry { Name = name, Values = values, Saver = saver, Loader = loader };
        }
    }
}

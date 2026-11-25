namespace RadianceRush
{
    // HP, soul, lifeblood and Baldur Shell charges :monkblep:
    public class HeroState
    {
        const int BaldurCharges = 4;

        public int Health;
        public int Blue;
        public int Soul;
        public int Blocker;

        public static HeroState Capture()
        {
            var pd = PlayerData.instance;
            if (pd == null) return null;
            return new HeroState { Health = pd.health, Blue = pd.healthBlue, Soul = pd.MPCharge + pd.MPReserve, Blocker = pd.blockerHits };
        }

        public void Restore(HeroController hc)
        {
            var pd = PlayerData.instance;
            SetHealth(hc, pd, Health);
            SetSoul(hc, Soul);
            pd.blockerHits = Blocker;
            RestoreBlue(pd, Blue);
        }

        public void Reset(HeroController hc)
        {
            var pd = PlayerData.instance;
            hc.MaxHealthKeepBlue();
            SetSoul(hc, 0);
            pd.blockerHits = BaldurCharges;
            RestoreBlue(pd, Blue);
        }

        static void SetHealth(HeroController hc, PlayerData pd, int health)
        {
            if (health < 1) health = 1;
            int diff = pd.health - health;
            if (diff < 0) { hc.AddHealth(-diff); return; }
            if (diff == 0) return;
            int blue = pd.healthBlue;
            pd.healthBlue = 0;
            hc.TakeHealth(diff);
            pd.healthBlue = blue;
        }

        // Same event a lifeblood thing sends, the HUD adds the mask and the lifebloo
        static void RestoreBlue(PlayerData pd, int blue)
        {
            for (int i = pd.healthBlue; i < blue; i++) EventRegister.SendEvent("ADD BLUE HEALTH");
        }

        static void SetSoul(HeroController hc, int soul)
        {
            hc.ClearMPSendEvents();
            if (soul > 0) hc.AddMPCharge(soul);
        }
    }
}

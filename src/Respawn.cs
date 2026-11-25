namespace RadianceRush
{
    // The respawn point from before the bench room
    public class Respawn
    {
        string scene, marker;
        int type;
        bool facingRight;

        public static Respawn Capture()
        {
            var pd = PlayerData.instance;
            if (pd == null) return null;
            return new Respawn { scene = pd.respawnScene, marker = pd.respawnMarkerName, type = pd.respawnType, facingRight = pd.respawnFacingRight };
        }

        public void Restore(PlayerData pd)
        {
            if (pd == null) return;
            pd.respawnScene = scene;
            pd.respawnMarkerName = marker;
            pd.respawnType = type;
            pd.respawnFacingRight = facingRight;
        }
    }
}

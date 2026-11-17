using UnityEngine;

namespace RadianceRush
{
    public class Hud : MonoBehaviour
    {
        const float GGSeconds = 6f;

        GUIStyle style;
        float ggUntil;

        public void ShowGG() { ggUntil = Time.unscaledTime + GGSeconds; }

        void OnGUI()
        {
            var mod = RadianceRush.Instance;
            if (mod == null || !RadianceRush.GS.Enabled || !RadianceRush.GS.ShowCounter) return;

            string text = Time.unscaledTime < ggUntil ? "GGs!"
                        : (mod.InRun || mod.Pending) && mod.Index < mod.Fights.Count ? (mod.Index + 1) + "/" + mod.Fights.Count
                        : null;
            if (text == null) return;

            if (style == null)
                style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(16, Screen.height / 36), fontStyle = FontStyle.Bold };

            style.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(22, 18, 200, 60), text, style);
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(20, 16, 200, 60), text, style);
        }
    }
}

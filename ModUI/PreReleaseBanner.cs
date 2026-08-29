using System;
using HarmonyLib;
using UnityEngine;

namespace CustomSpineLoader.ModUI;

public class PreReleaseBanner : MonoBehaviour
{
    private enum Corner
    {
        TopLeft,
        TopRight,
        BottomRight,
        BottomLeft
    }

    private const float SecondsPerCorner = 10f;

    private const float Margin = 2f;

    public static bool Hidden;

    private GUIStyle _style;
    private Corner _corner;
    private float _timer;
    private string _message;

    private GUIStyle Style
    {
        get
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box)
                {
                    fontSize = Mathf.Max(9, Screen.currentResolution.height / 110),
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(4, 4, 2, 2),
                    normal = { textColor = new Color(1f, 1f, 1f, 0.5f) }
                };
            }
            return _style;
        }
    }

    private void Awake()
    {
        _message = $"{Plugin.PluginName} {Plugin.PluginVer} PRE-RELEASE; User: {SteamName()}";
    }

    private static string SteamName()
    {
        try
        {
            var manager = AccessTools.TypeByName("SteamManager");
            var initialised = AccessTools.Property(manager, "Initialized")?.GetValue(null) as bool?;
            if (initialised != true) return "unknown";

            var friends = AccessTools.TypeByName("Steamworks.SteamFriends");
            var name = AccessTools.Method(friends, "GetPersonaName")?.Invoke(null, null) as string;
            return string.IsNullOrWhiteSpace(name) ? "unknown" : name;
        }
        catch (Exception e)
        {
            Plugin.Log.LogInfo("Pre-release banner: no Steam name available (" + e.Message + ").");
            return "unknown";
        }
    }

    private void Update()
    {
        _timer += Time.unscaledDeltaTime;
        if (_timer < SecondsPerCorner) return;

        _timer = 0f;
        _corner = _corner == Corner.BottomLeft ? Corner.TopLeft : _corner + 1;
    }

    private void OnGUI()
    {
        if (Hidden || string.IsNullOrEmpty(_message)) return;

        var content = new GUIContent(_message);
        var size = Style.CalcSize(content);

        var x = _corner is Corner.TopRight or Corner.BottomRight
            ? Screen.width - size.x - Margin
            : Margin;
        var y = _corner is Corner.BottomLeft or Corner.BottomRight
            ? Screen.height - size.y - Margin
            : Margin;

        GUI.Box(new Rect(x, y, size.x, size.y), content, Style);
    }
}

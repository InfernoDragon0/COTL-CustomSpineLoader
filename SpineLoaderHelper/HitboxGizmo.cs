using CustomSpineLoader.MapEditor.Tools;
using UnityEngine;

namespace CustomSpineLoader.SpineLoaderHelper;

/// Draws the outline of an attack swipe's collider where it landed, for tuning hit boxes. Off
/// unless the Debug / WeaponHitboxes config is on; then every swipe is drawn, vanilla ones too, so
/// a custom weapon can be compared against the sword it was built on.
public static class HitboxGizmo
{
    public static readonly Color CustomColour = new(0.2f, 1f, 1f, 1f);
    public static readonly Color PlayerColour = new(1f, 0.75f, 0.2f, 1f);
    public static readonly Color OtherColour = new(1f, 0.3f, 0.3f, 1f);

    private const float Lifetime = 0.7f;
    private const int Segments = 48;

    public static bool Enabled => Plugin.DebugWeaponHitboxes != null && Plugin.DebugWeaponHitboxes.Value;

    public static void Show(Swipe swipe, bool customLightHit)
    {
        if (!Enabled || swipe == null) return;

        var collider = swipe.damageCollider;
        if (collider == null) return;

        var isPlayer = swipe.Origin != null && swipe.Origin.GetComponent<PlayerFarming>() != null;
        var colour = customLightHit ? CustomColour : isPlayer ? PlayerColour : OtherColour;

        var go = new GameObject("CultTweaker_HitboxGizmo");
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = true;
        line.widthMultiplier = 0.05f;
        line.material = MapEditorGizmos.LineMaterial();
        line.sortingOrder = 32000;
        line.startColor = line.endColor = colour;

        var z = swipe.transform.position.z - 0.2f;
        switch (collider)
        {
            case CircleCollider2D circle:
            {
                var centre = circle.transform.TransformPoint(circle.offset);
                var scale = circle.transform.lossyScale;
                var radius = circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                line.positionCount = Segments;
                for (var i = 0; i < Segments; i++)
                {
                    var angle = i / (float)Segments * Mathf.PI * 2f;
                    line.SetPosition(i, new Vector3(centre.x + Mathf.Cos(angle) * radius,
                        centre.y + Mathf.Sin(angle) * radius, z));
                }
                break;
            }
            case PolygonCollider2D polygon:
            {
                var points = polygon.GetPath(0);
                line.positionCount = points.Length;
                for (var i = 0; i < points.Length; i++)
                {
                    var world = polygon.transform.TransformPoint(points[i] + polygon.offset);
                    line.SetPosition(i, new Vector3(world.x, world.y, z));
                }
                break;
            }
            case BoxCollider2D box:
            {
                var half = box.size * 0.5f;
                var corners = new[]
                {
                    new Vector2(-half.x, -half.y), new Vector2(half.x, -half.y),
                    new Vector2(half.x, half.y), new Vector2(-half.x, half.y)
                };
                line.positionCount = 4;
                for (var i = 0; i < 4; i++)
                {
                    var world = box.transform.TransformPoint(corners[i] + box.offset);
                    line.SetPosition(i, new Vector3(world.x, world.y, z));
                }
                break;
            }
            default:
                Object.Destroy(go);
                return;
        }

        // A spoke from the attacker to the swipe's centre shows the reach as well as the size.
        if (swipe.Origin != null)
        {
            var spoke = new GameObject("Spoke");
            spoke.transform.SetParent(go.transform, false);
            var spokeLine = spoke.AddComponent<LineRenderer>();
            spokeLine.useWorldSpace = true;
            spokeLine.widthMultiplier = 0.025f;
            spokeLine.material = line.material;
            spokeLine.sortingOrder = 32000;
            spokeLine.startColor = spokeLine.endColor = colour;
            spokeLine.positionCount = 2;
            var from = swipe.Origin.transform.position;
            var to = swipe.transform.position;
            spokeLine.SetPosition(0, new Vector3(from.x, from.y, z));
            spokeLine.SetPosition(1, new Vector3(to.x, to.y, z));
        }

        go.AddComponent<Fade>().Colour = colour;
    }

    /// Follows a chain hook while it is out: the hook's circle and the chain's box, redrawn every
    /// frame, dimmed while the game has the colliders switched off.
    public static void TrackHook(ChainHook hook)
    {
        if (!Enabled || hook == null) return;

        var follower = hook.GetComponent<HookFollower>();
        if (follower == null) follower = hook.gameObject.AddComponent<HookFollower>();

        var owner = hook.GetOwner();
        var player = owner != null ? owner.GetComponent<PlayerFarming>() : null;
        follower.Colour = player == null ? OtherColour
            : CustomWeapons.IsCustom(player.currentWeapon) ? CustomColour : PlayerColour;
        follower.Bind(hook);
    }

    private sealed class HookFollower : MonoBehaviour
    {
        public Color Colour = PlayerColour;

        private ChainHook _hook;
        private CircleCollider2D _circle;
        private BoxCollider2D _box;
        private LineRenderer _ring;
        private LineRenderer _rect;

        public void Bind(ChainHook hook)
        {
            _hook = hook;
            try
            {
                var traverse = HarmonyLib.Traverse.Create(hook);
                _circle = traverse.Field("hookCollider").GetValue<CircleCollider2D>();
                _box = traverse.Field("chainCollider").GetValue<BoxCollider2D>();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Hitbox gizmo: chain hook colliders unreadable (" + e.Message + ").");
            }

            if (_ring == null) _ring = MakeLine("HookRing", Segments, true);
            if (_rect == null) _rect = MakeLine("ChainBox", 4, true);
        }

        private LineRenderer MakeLine(string name, int points, bool loop)
        {
            var go = new GameObject("CultTweaker_" + name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = loop;
            line.widthMultiplier = 0.05f;
            line.material = MapEditorGizmos.LineMaterial();
            line.sortingOrder = 32000;
            line.positionCount = points;
            return line;
        }

        private void LateUpdate()
        {
            if (!Enabled || _hook == null)
            {
                Show(false);
                return;
            }

            var z = transform.position.z - 0.2f;

            if (_circle != null && _ring != null)
            {
                var centre = _circle.transform.TransformPoint(_circle.offset);
                var scale = _circle.transform.lossyScale;
                var radius = _circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                for (var i = 0; i < Segments; i++)
                {
                    var angle = i / (float)Segments * Mathf.PI * 2f;
                    _ring.SetPosition(i, new Vector3(centre.x + Mathf.Cos(angle) * radius,
                        centre.y + Mathf.Sin(angle) * radius, z));
                }
                Tint(_ring, _circle.enabled);
                _ring.enabled = true;
            }

            if (_box != null && _rect != null)
            {
                var half = _box.size * 0.5f;
                var corners = new[]
                {
                    new Vector2(-half.x, -half.y), new Vector2(half.x, -half.y),
                    new Vector2(half.x, half.y), new Vector2(-half.x, half.y)
                };
                for (var i = 0; i < 4; i++)
                {
                    var world = _box.transform.TransformPoint(corners[i] + _box.offset);
                    _rect.SetPosition(i, new Vector3(world.x, world.y, z));
                }
                Tint(_rect, _box.enabled);
                _rect.enabled = true;
            }
        }

        private void Tint(LineRenderer line, bool live)
        {
            var colour = Colour;
            colour.a = live ? 1f : 0.3f;
            line.startColor = line.endColor = colour;
        }

        private void Show(bool on)
        {
            if (_ring != null) _ring.enabled = on;
            if (_rect != null) _rect.enabled = on;
        }

        private void OnDisable() => Show(false);
    }

    private sealed class Fade : MonoBehaviour
    {
        public Color Colour;
        private float _age;
        private LineRenderer[] _lines;

        private void Awake() => _lines = GetComponentsInChildren<LineRenderer>(true);

        private void Update()
        {
            _age += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(_age / Lifetime);
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            var faded = Colour;
            faded.a = 1f - t * t;
            foreach (var line in _lines)
                if (line != null) line.startColor = line.endColor = faded;
        }
    }
}

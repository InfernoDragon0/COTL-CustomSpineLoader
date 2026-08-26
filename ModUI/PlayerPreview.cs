using System.Collections.Generic;
using Spine.Unity;
using UnityEngine;

namespace CustomSpineLoader.ModUI;

// Portraits of the players: a skeleton of each, kept off in a corner of the world and filmed.
//
// Three things had to be true at once, and each one rules out an obvious answer.
//
// It has to render whatever the spine actually is. Spine 3.8's SkeletonGraphic - which is how the
// game's own inventory tab draws the lamb - goes through a single CanvasRenderer, so one texture
// (atlasAssets[0]) and one material. That is fine for the lamb and wrong here: a cross-atlas fleece
// loses everything but its first page, and a custom spine is built against the Spine/Skeleton MESH
// shader (SpineFolderLoader), which a borrowed UI material renders with the wrong alpha. A
// MeshRenderer has neither problem, so the portrait is a SkeletonAnimation and a camera.
//
// It has to animate on its own. Filming the LIVE player gives a portrait that only ever does what
// the player is doing, and driving an animation on it would drive the player standing in the world.
// So each portrait gets a skeleton of its very own, with its own AnimationState - which is what
// makes an animation picker possible (see Play/Animations below).
//
// And it must not become part of the game. These are made by SkeletonAnimation's own factory, so
// each one is a bare GameObject carrying a MeshFilter, a MeshRenderer and a SkeletonAnimation -
// nothing else. No PlayerFarming, no Health, no UnitObject, no collider, so nothing counts them,
// targets them, or sees them: not PlayerFarming.players, not Health.team1, not the room-lock check
// that asks a room what is standing in it. Cloning the player's GameObject WOULD have done all of
// that, which is precisely why they are built rather than copied. They sit on a layer of their own,
// far outside the world, and only this camera has that layer in its mask.
public static class PlayerPreview
{
    // The target matches the shape of the box it is drawn in, because a square texture stretched
    // into an oblong rect is a squashed lamb. Twice the on-screen size so it stays crisp.
    private const float Supersample = 2f;

    // Far enough from anything the game builds that nothing can wander into shot.
    private static readonly Vector3 Stage = new(12000f, -12000f, 0f);
    private const float StageSpacing = 60f;

    private sealed class Portrait
    {
        public SkeletonAnimation Spine;
        public RenderTexture Texture;
        public SkeletonDataAsset Asset;
        public Vector3 Position;
        public float Size = 1f;
        public bool Untinted;
    }

    private static readonly Dictionary<int, Portrait> _portraits = [];

    private static GameObject _root;
    private static Camera _camera;
    private static int _layer = -1;

    public static RenderTexture Of(int playerId) =>
        _portraits.TryGetValue(playerId, out var portrait) && portrait.Texture != null
            ? portrait.Texture
            : null;

    // ---- keeping a portrait in step ----------------------------------------------------------------

    // Called when the dock is built, and after anything that changes how a player looks. Builds the
    // skeleton if it is missing or the spine has been swapped, then re-dresses it from the live one.
    public static bool Sync(int playerId, float width, float height)
    {
        var player = SpineLoaderHelper.PlayerSpineLoader.ResolvePlayer(playerId);
        var live = player != null ? player.Spine : null;
        if (live == null || live.skeletonDataAsset == null || live.Skeleton == null) return false;

        try
        {
            if (!EnsureRig()) return false;

            var portrait = Build(playerId, live, width, height);
            if (portrait == null) return false;

            Dress(portrait, live);
            Frame(portrait);
            Render(portrait);
            return true;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"CultTweaker: could not draw player {playerId + 1}: {e.Message}");
            return false;
        }
    }

    // Every frame the panel is open, so the portraits move. Cheap: four 256px renders of a skeleton
    // apiece, and the skeletons tick themselves like any other component.
    public static void Tick()
    {
        if (_camera == null) return;

        // Unscaled: the panel runs the world at a tenth speed so a fleece swap shows at once, and a
        // portrait is not the world.
        var delta = Time.unscaledDeltaTime;

        foreach (var portrait in _portraits.Values)
        {
            if (portrait.Spine == null || portrait.Texture == null) continue;

            // Advanced by hand rather than left to Unity's own Update and LateUpdate. Those are
            // driven by Time.deltaTime, which the panel has scaled to a tenth, and compensating for
            // that through SkeletonAnimation.timeScale means the portrait's motion depends on a
            // global the panel is deliberately fiddling with. Driving it here makes it depend on
            // nothing: the component is switched off, and these two calls are the entire frame -
            // Update advances and applies the animation, LateUpdate rebuilds the mesh.
            portrait.Spine.Update(delta);
            portrait.Spine.LateUpdate();

            // After the mesh, not before: the materials to override do not exist until one has
            // been generated. Retried until it takes, then never again.
            if (!portrait.Untinted) portrait.Untinted = Untint(portrait.Spine);

            Render(portrait);
        }
    }

    // ---- the animation picker this exists to make possible --------------------------------------------

    // Sorted, because a spine's authoring order is nobody's browsing order - and sorting groups the
    // families by prefix, which is how they are named.
    public static List<string> Animations(int playerId)
    {
        var names = new List<string>();
        if (!_portraits.TryGetValue(playerId, out var portrait) || portrait.Spine == null) return names;

        var data = portrait.Spine.Skeleton?.Data;
        if (data == null) return names;

        foreach (var animation in data.Animations) names.Add(animation.Name);
        names.Sort(System.StringComparer.OrdinalIgnoreCase);
        return names;
    }

    // Re-dresses an existing portrait and touches nothing else - no rebuild, no re-frame, and above
    // all no rebuilding of the card around it. The card's RawImage points at a render target that is
    // updated in place, so a change of clothes needs no UI work at all: tearing the dock down and
    // building it again is what made it jump about on every fleece.
    public static void Redress(int playerId)
    {
        if (!_portraits.TryGetValue(playerId, out var portrait) || portrait.Spine == null) return;

        var player = SpineLoaderHelper.PlayerSpineLoader.ResolvePlayer(playerId);
        var live = player != null ? player.Spine : null;
        if (live == null || live.Skeleton == null) return;

        try
        {
            Dress(portrait, live);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"CultTweaker: player {playerId + 1} would not re-dress: {e.Message}");
        }
    }

    public static string CurrentAnimation(int playerId)
    {
        if (!_portraits.TryGetValue(playerId, out var portrait) || portrait.Spine == null) return null;

        try
        {
            return portrait.Spine.AnimationState?.GetCurrent(0)?.Animation?.Name;
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    public static void Play(int playerId, string animation)
    {
        if (!_portraits.TryGetValue(playerId, out var portrait) || portrait.Spine == null) return;
        if (string.IsNullOrEmpty(animation)) return;

        try
        {
            portrait.Spine.AnimationState.SetAnimation(0, animation, true);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"CultTweaker: '{animation}' would not play: {e.Message}");
        }
    }

    // ---- building --------------------------------------------------------------------------------------

    private static Portrait Build(int playerId, SkeletonAnimation live, float width, float height)
    {
        if (_portraits.TryGetValue(playerId, out var existing))
        {
            // The same spine as last time: keep the skeleton, it is only the dressing that changes.
            if (existing.Spine != null && existing.Asset == live.skeletonDataAsset) return existing;

            Discard(existing);
            _portraits.Remove(playerId);
        }

        // The factory, NOT Instantiate(player.gameObject): this makes a bare skeleton, where that
        // would make a second player complete with its health, its team and its place in the roster.
        var spine = SkeletonAnimation.NewSkeletonAnimationGameObject(live.skeletonDataAsset);
        if (spine == null) return null;

        spine.gameObject.name = $"CultTweakerPortrait{playerId + 1}";
        spine.transform.SetParent(_root.transform, false);

        var position = Stage + new Vector3(playerId * StageSpacing, 0f, 0f);
        spine.transform.position = position;

        SetLayer(spine.gameObject, _layer);
        spine.Initialize(true);

        // Switched off so Unity stops calling its Update and LateUpdate; Tick does both by hand on
        // an unscaled clock. The MeshRenderer is a separate component and keeps drawing.
        spine.timeScale = 1f;
        spine.enabled = false;

        var portrait = new Portrait
        {
            Spine = spine,
            Asset = live.skeletonDataAsset,
            Position = position,
            Texture = new RenderTexture(
                Mathf.Max(32, Mathf.RoundToInt(width * Supersample)),
                Mathf.Max(32, Mathf.RoundToInt(height * Supersample)),
                16, RenderTextureFormat.ARGB32)
            {
                name = $"CultTweakerPortraitTarget{playerId + 1}",
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            }
        };

        _portraits[playerId] = portrait;
        return portrait;
    }

    // Draws the portrait with Spine's own stock shader instead of the one the game dresses the
    // world in.
    //
    // The player standing in the base is *lit* - the game runs its own lighting over the sprites,
    // and what the player looks like on screen is the art plus that. The portrait camera renders a
    // private layer with none of that pass, so the same lamb came out dim and muddy next to the one
    // in the world. (The first guess was the opposite - a warm tint being added rather than a light
    // being missing - which the side-by-side put straight.)
    //
    // There is no way to light the portrait the way the world is lit without standing up the whole
    // lighting rig for it, so it goes the other way: the stock Spine shader ignores lighting
    // entirely and draws the art at full brightness, which is what a portrait wants anyway. The
    // trade is that a slot relying on the game shader's own blending draws plainly here.
    //
    // Registered through Spine's CustomMaterialOverride rather than by assigning to the
    // MeshRenderer, because SkeletonRenderer rewrites sharedMaterials from the atlas on every mesh
    // rebuild and would undo a direct assignment.
    //
    // Returns false until it has something to override. This ran once straight after Initialize the
    // first time, which is before any mesh has been generated - so sharedMaterials was still empty,
    // nothing was registered, and the portrait carried on rendering unlit-dark through the game's
    // material. It is retried each frame until it takes.
    // What comes off the game's own shader for a portrait: everything that answers to where the
    // player is standing rather than to what they look like. The woods fade is the one that made
    // the portrait dark - it tints the character toward the environment, and the portrait's corner
    // of the map has no environment to tint toward.
    private static readonly string[] WorldKeywords =
        ["_USEFADEINWOODSCOLOR", "_RECEIVESHADOW", "_USEEMISSION_ON"];

    // ASE writes a toggle as a keyword plus a float of the same name, and a shader that branches on
    // the float rather than the keyword would ignore the list above on its own.
    private static readonly string[] WorldToggles =
        ["_UseFadeInWoodsColor", "_ReceiveShadow", "_UseEmission"];

    private static bool Untint(SkeletonAnimation spine)
    {
        var renderer = spine != null ? spine.GetComponent<MeshRenderer>() : null;
        if (renderer == null) return false;

        var stock = SpineLoaderHelper.SpineFolderLoader.SpineShader();
        if (stock == null) return true;

        var any = false;

        foreach (var source in renderer.sharedMaterials)
        {
            if (source == null) continue;

            any = true;
            if (spine.CustomMaterialOverride.ContainsKey(source)) continue;

            var plain = new Material(source)
            {
                name = source.name + " (portrait)",
                hideFlags = HideFlags.HideAndDontSave
            };

            // Which shader the portrait keeps depends on whose atlas it is, and the two cases want
            // opposite things.
            //
            // A CUSTOM spine is built by this mod against Spine/Skeleton, with an atlas packed by
            // Spine's own packer. Handing it the stock shader is a no-op that costs nothing, and
            // this is the case that has always looked right.
            //
            // The STOCK lamb wears the game's own Skeleton_ASE_v1_SoftAlphaTest, and the name is
            // the whole story: it alpha-TESTS. A texel below the cutoff is discarded outright,
            // colour and all. Spine/Skeleton alpha-BLENDS instead, so those same texels come back
            // at whatever alpha they carry - which is why the lamb's atlas showed a red haze on the
            // ears and a red shape floating above the head, and why neither premultiplied nor
            // straight blending could remove them. Nothing about the blend was ever going to; the
            // pixels are supposed to be thrown away, not blended.
            //
            // So the stock lamb keeps the shader that draws it correctly in the world, and only the
            // parts of it that depend on where the player is STANDING come off. That is also the
            // honest answer to the original problem: the portrait was never missing a light, it was
            // wearing the woods' fade colour in a corner of the map with no woods in it.
            var fromSpineShader = source.shader != null && source.shader.name.StartsWith("Spine/");

            if (fromSpineShader)
            {
                plain.shader = stock;
                plain.mainTexture = source.mainTexture;
            }
            else
            {
                foreach (var keyword in WorldKeywords) plain.DisableKeyword(keyword);
                foreach (var toggle in WorldToggles)
                    if (plain.HasProperty(toggle)) plain.SetFloat(toggle, 0f);
            }

            // The biome's colours, pinned ON THE MATERIAL rather than swapped around the render.
            //
            // These are shader globals - LightingManager drives _TimeOfDayColor from the biome's
            // god-ray colour, and in a torch-lit dungeon it reads RGBA(0.358, 0.304, 0.186): the
            // orange. Setting the global to white for the duration of Camera.Render did not stick,
            // which is the tell that something else writes it inside the frame. A value set on the
            // material cannot be raced that way - Unity resolves a material's own property before
            // it ever looks at the global - so the portrait carries its own neutral copy and stops
            // caring what the world is doing.
            plain.SetColor(TimeOfDayColor, Color.white);
            plain.SetColor(GlobalHCol, Color.white);
            plain.SetFloat(GlobalExposure, 1f);

            // The v1.0.4 "red overlays" correction, applied where it can no longer be skipped.
            if (plain.HasProperty("_EmissionMap"))
                plain.SetTextureScale("_EmissionMap", Vector2.zero);

            spine.CustomMaterialOverride[source] = plain;
        }

        return any;
    }

    // The live skin is the answer the fleece and spine choices already produced - attachments copied
    // in, slots hidden, whatever a custom spine's config asked for. Reading it is both simpler than
    // rebuilding it and incapable of disagreeing with what is standing in the world.
    private static void Dress(Portrait portrait, SkeletonAnimation live)
    {
        var skeleton = portrait.Spine.Skeleton;

        // Cleared first, and that is the whole reason a fleece change did not show. Skeleton.SetSkin
        // early-outs when handed the skin it already has, and ApplyFleeceAttachments dresses the
        // player by mutating their live skin IN PLACE - so the object is the same one every time and
        // the portrait was told, correctly and uselessly, that nothing had changed.
        if (live.Skeleton.Skin != null)
        {
            skeleton.SetSkin((Spine.Skin)null);
            skeleton.SetSkin(live.Skeleton.Skin);
        }

        skeleton.SetSlotsToSetupPose();

        // Facing, so a player who is looking left keeps looking left in their portrait.
        skeleton.ScaleX = Mathf.Abs(skeleton.ScaleX) * (live.Skeleton.ScaleX < 0f ? -1f : 1f);

        // Only if nothing is playing yet - a picked animation must survive a fleece change.
        if (portrait.Spine.AnimationState.GetCurrent(0) == null)
        {
            // An idle of the spine's own in preference to whatever the live player is doing. While
            // the panel is up the players are parked in the game's cutscene state, so copying their
            // current animation copies a frozen pose - a portrait that is correct and motionless.
            var wanted = First(portrait,
                              "idle-front", "idle-down", "idle-side", "idle", "Idle",
                              "idle-back", "idle-up")
                          ?? Current(live);

            if (!string.IsNullOrEmpty(wanted))
            {
                Play(portrait, wanted);
                Plugin.Log.LogInfo($"CultTweaker: player portrait is playing '{wanted}'.");
            }
            else
            {
                Plugin.Log.LogWarning("CultTweaker: the portrait spine has no animation to play.");
            }
        }

        portrait.Spine.Update(0f);
        portrait.Spine.LateUpdate();
    }

    private static void Play(Portrait portrait, string animation)
    {
        try
        {
            portrait.Spine.AnimationState.SetAnimation(0, animation, true);
        }
        catch (System.Exception)
        {
            // A spine without that animation just stands in its setup pose.
        }
    }

    private static string Current(SkeletonAnimation live)
    {
        try
        {
            return live.AnimationState?.GetCurrent(0)?.Animation?.Name;
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    // Null rather than "the first animation there is": the caller has a fallback of its own, and a
    // spine whose first animation is a one-shot death would give a portrait that plays once and
    // then lies there.
    private static string First(Portrait portrait, params string[] candidates)
    {
        var data = portrait.Spine.Skeleton?.Data;
        if (data == null) return null;

        foreach (var name in candidates)
            if (data.FindAnimation(name) != null) return name;

        // Anything whose name reads like an idle, before giving up on finding a loop.
        foreach (var animation in data.Animations)
            if (animation.Name.IndexOf("idle", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return animation.Name;

        return null;
    }

    // Measured from the skeleton's own bounds, once, when it is dressed - not per frame. Framing on
    // a moving skeleton makes the portrait breathe in and out as the animation swings its arms.
    private static void Frame(Portrait portrait)
    {
        try
        {
            var buffer = new float[8];
            portrait.Spine.Skeleton.GetBounds(out _, out _, out var wide, out var tall, ref buffer);

            var extent = Mathf.Max(wide, tall) * 0.5f;
            portrait.Size = extent > 0.001f ? extent * 1.25f : 1f;
        }
        catch (System.Exception)
        {
            portrait.Size = 1f;
        }
    }

    // The biome's own colours, which the game keeps as SHADER GLOBALS rather than on any material.
    //
    // This is where the tint comes from, and why nothing done to the material could reach it: the
    // player's shader multiplies by _TimeOfDayColor, and LightingManager drives that from the
    // biome's god-ray colour (GameManager sets it to plain white for the untinted case, which is
    // where the neutral value below comes from). Stand in a torch-lit dungeon and the lamb is
    // orange - correctly, in the world, and pointlessly in a portrait that is not standing anywhere.
    //
    // Set immediately around Camera.Render, which draws synchronously, and put straight back. The
    // globals belong to the whole game and the world's own cameras must not see this.
    private static readonly int TimeOfDayColor = Shader.PropertyToID("_TimeOfDayColor");
    private static readonly int GlobalHCol = Shader.PropertyToID("_GlobalHCol");
    private static readonly int GlobalSCol = Shader.PropertyToID("_GlobalSCol");
    private static readonly int GlobalExposure = Shader.PropertyToID("_GlobalExposure");

    private static void Render(Portrait portrait)
    {
        if (_camera == null || portrait.Spine == null || portrait.Texture == null) return;

        // Centred on the skeleton's middle rather than its feet: GetBounds is relative to the root,
        // and a skeleton stands on its origin.
        _camera.transform.position = portrait.Position + new Vector3(0f, portrait.Size * 0.75f, -100f);
        _camera.orthographicSize = portrait.Size;
        _camera.targetTexture = portrait.Texture;

        var timeOfDay = Shader.GetGlobalColor(TimeOfDayColor);
        var highlight = Shader.GetGlobalColor(GlobalHCol);
        var shadow = Shader.GetGlobalColor(GlobalSCol);
        var exposure = Shader.GetGlobalFloat(GlobalExposure);

        Shader.SetGlobalColor(TimeOfDayColor, Color.white);
        Shader.SetGlobalColor(GlobalHCol, Color.white);
        Shader.SetGlobalFloat(GlobalExposure, 1f);

        try
        {
            _camera.Render();
        }
        finally
        {
            Shader.SetGlobalColor(TimeOfDayColor, timeOfDay);
            Shader.SetGlobalColor(GlobalHCol, highlight);
            Shader.SetGlobalColor(GlobalSCol, shadow);
            Shader.SetGlobalFloat(GlobalExposure, exposure);
            _camera.targetTexture = null;
        }
    }

    // ---- the rig -----------------------------------------------------------------------------------------

    private static bool EnsureRig()
    {
        if (_camera != null && _root != null) return true;

        // FreeLayer always answers now - see there - so there is no failure to report here.
        if (_layer < 0) _layer = FreeLayer();

        if (_root == null)
        {
            _root = new GameObject("CultTweakerPortraits") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(_root);
        }

        if (_camera == null)
        {
            var go = new GameObject("CultTweakerPortraitCamera");
            go.transform.SetParent(_root.transform, false);

            _camera = go.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);

            // Only ours, and nothing else's: the stage sits far outside the world, but a camera
            // that could see the world would still be one more camera rendering it.
            _camera.cullingMask = 1 << _layer;
            _camera.nearClipPlane = 0.01f;
            _camera.farClipPlane = 500f;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;

            // Driven by hand, one render per portrait per frame.
            _camera.enabled = false;
        }

        return true;
    }

    // A layer no camera in the game draws, so a portrait can never appear in the world - asked of
    // OffscreenLayer, which every rig shares. Preferred, not required: see there.
    private static int FreeLayer() => MapEditor.OffscreenLayer.Value;

    private static void SetLayer(GameObject go, int layer)
    {
        foreach (var child in go.GetComponentsInChildren<Transform>(true))
            if (child != null) child.gameObject.layer = layer;
    }

    // ---- teardown -----------------------------------------------------------------------------------------

    public static void Release()
    {
        foreach (var portrait in _portraits.Values) Discard(portrait);
        _portraits.Clear();

        if (_root != null) Object.Destroy(_root);
        _root = null;
        _camera = null;
    }

    private static void Discard(Portrait portrait)
    {
        if (portrait == null) return;

        if (portrait.Spine != null) Object.Destroy(portrait.Spine.gameObject);

        if (portrait.Texture != null)
        {
            portrait.Texture.Release();
            Object.DestroyImmediate(portrait.Texture);
        }

        portrait.Spine = null;
        portrait.Texture = null;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Spine;
using Spine.Unity.AttachmentTools;
using UnityEngine;

namespace CustomSpineLoader.SpineLoaderHelper;

// Baking a follower skin costs seconds, and almost all of it is GetRepackedSkin pulling every region of
// the base skin out of the game's 8192x8192 BC7 atlas one region at a time (~121ms each, measured). The
// result is identical on every boot, so it is worth keeping.
//
// What is kept is deliberately small: the packed atlas as a png, plus the rectangle each attachment ended
// up occupying in it. Restoring is then the same operation the repacker performs minus the packing --
// compose the skin as usual (cheap, no extraction), then point each renderable attachment at the saved
// page. Spine's own SetRegion recomputes offsets and UVs, so none of its maths is duplicated here.
//
// Every failure path returns false or writes nothing, and the caller bakes normally. A cache that cannot
// be read is a slow boot, never a wrong skin.
public static class FollowerSkinCache
{
    private const string FolderName = ".ctcache";
    private const string AtlasFile = "atlas.png";
    private const string LayoutFile = "layout.json";

    // Bump when the layout below changes shape, so old caches are ignored rather than misread.
    private const string Format = "v2";

    private sealed class Layout
    {
        public string Stamp = "";
        public int PageWidth;
        public int PageHeight;
        public List<Entry> Entries = [];
    }

    private sealed class Entry
    {
        public int Slot;
        public string Name = "";
        public float U, V, U2, V2;
        public int X, Y, Width, Height;
        public float OffsetX, OffsetY;
        public int OriginalWidth, OriginalHeight;
        public bool Rotate;
        public int Degrees;
    }

    // ---- invalidation ------------------------------------------------------------------------------
    //
    // The stamp is every input the bake reads, by CONTENT: a hash of config.json and of every png in the
    // variant folder, keyed by file name, plus the game and mod versions and the format version. Edit a
    // part, drop a new png in, delete one, or change the base skin in config.json and the stamp no
    // longer matches, so the next bake is a real one and rewrites the cache. The skin editor's Reload
    // takes the same path, so a save from the editor refreshes the cache in the same session rather than
    // waiting for a restart.
    //
    // Content hashing rather than size-and-write-time is what makes a cache SHAREABLE. Timestamps do not
    // survive being zipped, copied, synced or checked out, so a stamp built from them would miss on
    // somebody else's machine and they would rebake for nothing. Hashing costs almost nothing here --
    // the inputs are a few small pngs and a json, and the multi-megabyte atlas.png is an output, not an
    // input, so it is never hashed.
    //
    // The game version is in the stamp because the cached atlas holds pixels baked out of the game's own
    // follower atlas: if an update redraws that art, a cache built against the old art is wrong rather
    // than merely stale. (A mod that repaints the atlas without changing the game version is the one
    // case this cannot see; delete the .ctcache folders after installing one.)
    private static string StampFor(string variantFolder)
    {
        var stamp = new StringBuilder(Format)
            .Append("|game:").Append(Application.version)
            .Append("|mod:").Append(Plugin.PluginVer);

        using var hasher = System.Security.Cryptography.MD5.Create();

        var inputs = Directory.GetFiles(variantFolder, "*.png", SearchOption.TopDirectoryOnly).ToList();
        var config = Path.Combine(variantFolder, "config.json");
        if (File.Exists(config)) inputs.Add(config);

        foreach (var file in inputs.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var hash = hasher.ComputeHash(File.ReadAllBytes(file));
            stamp.Append('|').Append(Path.GetFileName(file)).Append(':')
                 .Append(BitConverter.ToString(hash, 0, 8).Replace("-", ""));
        }

        return stamp.ToString();
    }

    private static string CacheFolder(string skinVariantName)
    {
        return FollowerSpineLoader.FollowerSkinFolders.TryGetValue(skinVariantName, out var variantFolder)
               && !string.IsNullOrEmpty(variantFolder) && Directory.Exists(variantFolder)
            ? Path.Combine(variantFolder, FolderName)
            : null;
    }

    // ---- restore -----------------------------------------------------------------------------------

    public static bool TryApply(string skinVariantName, Skin composed)
    {
        var folder = CacheFolder(skinVariantName);
        if (folder == null || composed == null) return false;

        var layoutPath = Path.Combine(folder, LayoutFile);
        var atlasPath = Path.Combine(folder, AtlasFile);
        if (!File.Exists(layoutPath) || !File.Exists(atlasPath)) return false;

        try
        {
            var layout = JsonConvert.DeserializeObject<Layout>(File.ReadAllText(layoutPath));
            if (layout == null) return false;

            var wanted = StampFor(FollowerSpineLoader.FollowerSkinFolders[skinVariantName]);
            if (layout.Stamp != wanted)
            {
                Plugin.Log.LogInfo($"{skinVariantName} changed since it was last baked; rebuilding it.");
                return false;
            }

            var page = LoadPage(skinVariantName, atlasPath, layout);
            if (page == null) return false;

            // Resolve everything before touching the skin: a half-applied skin would render wrong, and
            // bailing out is only safe while nothing has been changed yet.
            var byKey = layout.Entries.ToDictionary(e => e.Slot + "/" + e.Name);
            var work = new List<KeyValuePair<Attachment, AtlasRegion>>();

            foreach (var entry in composed.Attachments)
            {
                if (entry.attachment is not IHasRendererObject) continue;

                if (!byKey.TryGetValue(entry.SlotIndex + "/" + entry.Name, out var saved))
                {
                    Plugin.Log.LogInfo($"{skinVariantName} has a part the cache does not know " +
                                       $"('{entry.Name}' in slot {entry.SlotIndex}); rebuilding it.");
                    return false;
                }

                work.Add(new KeyValuePair<Attachment, AtlasRegion>(entry.attachment, RegionFor(saved, page)));
            }

            if (work.Count == 0) return false;

            foreach (var pair in work)
                pair.Key.SetRegion(pair.Value);

            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not restore {skinVariantName} from its cache ({e.Message}); " +
                                  "baking it instead.");
            return false;
        }
    }

    private static AtlasPage LoadPage(string skinVariantName, string atlasPath, Layout layout)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false) { name = skinVariantName };
        if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(atlasPath), false))
        {
            UnityEngine.Object.Destroy(texture);
            return null;
        }

        texture.mipMapBias = -0.5f;

        // Copying the follower material carries the shader, its properties and its keywords, which is
        // what GetRepackedSkin builds its output material from.
        var source = WorshipperData.Instance.SkeletonData.SkeletonDataAsset.atlasAssets[0].PrimaryMaterial;
        var material = new Material(source) { name = skinVariantName, mainTexture = texture };

        SpineFolderLoader.Keep(texture);
        SpineFolderLoader.Keep(material);

        return new AtlasPage
        {
            name = skinVariantName,
            width = layout.PageWidth > 0 ? layout.PageWidth : texture.width,
            height = layout.PageHeight > 0 ? layout.PageHeight : texture.height,
            rendererObject = material
        };
    }

    private static AtlasRegion RegionFor(Entry e, AtlasPage page) => new()
    {
        page = page,
        name = e.Name,
        index = -1,
        u = e.U,
        v = e.V,
        u2 = e.U2,
        v2 = e.V2,
        x = e.X,
        y = e.Y,
        width = e.Width,
        height = e.Height,
        offsetX = e.OffsetX,
        offsetY = e.OffsetY,
        originalWidth = e.OriginalWidth,
        originalHeight = e.OriginalHeight,
        rotate = e.Rotate,
        degrees = e.Degrees
    };

    // ---- save --------------------------------------------------------------------------------------

    public static void TrySave(string skinVariantName, Skin repacked, Texture2D packed)
    {
        var folder = CacheFolder(skinVariantName);
        if (folder == null || repacked == null || packed == null) return;

        try
        {
            var png = ImageConversion.EncodeToPNG(packed);
            if (png == null || png.Length == 0)
            {
                Plugin.Log.LogInfo($"The packed atlas for {skinVariantName} cannot be read back, so it " +
                                   "will be baked again next time.");
                return;
            }

            var layout = new Layout
            {
                Stamp = StampFor(FollowerSpineLoader.FollowerSkinFolders[skinVariantName]),
                PageWidth = packed.width,
                PageHeight = packed.height
            };

            foreach (var entry in repacked.Attachments)
            {
                if (entry.attachment is not IHasRendererObject holder) continue;
                if (holder.RendererObject is not AtlasRegion region) continue;

                layout.Entries.Add(new Entry
                {
                    Slot = entry.SlotIndex,
                    Name = entry.Name,
                    U = region.u, V = region.v, U2 = region.u2, V2 = region.v2,
                    X = region.x, Y = region.y, Width = region.width, Height = region.height,
                    OffsetX = region.offsetX, OffsetY = region.offsetY,
                    OriginalWidth = region.originalWidth, OriginalHeight = region.originalHeight,
                    Rotate = region.rotate, Degrees = region.degrees
                });
            }

            if (layout.Entries.Count == 0) return;

            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, AtlasFile), png);
            File.WriteAllText(Path.Combine(folder, LayoutFile), JsonConvert.SerializeObject(layout));

            Plugin.Log.LogInfo($"Cached {skinVariantName}: {layout.Entries.Count} part(s) and a " +
                               $"{packed.width}x{packed.height} atlas, {png.Length / 1024}KB.");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not cache {skinVariantName} ({e.Message}); it will be baked " +
                                  "again next time.");
        }
    }
}

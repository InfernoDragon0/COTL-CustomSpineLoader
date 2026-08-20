using System;
using System.Collections.Generic;
using System.Reflection;
using COTL_API.Guid;

namespace CustomSpineLoader.APIHelper;

public class CustomDungeonManager
{
    public static Dictionary<FollowerLocation, CustomDungeon> CustomDungeonList { get; } = []; //customDungeon Class list
    public static FollowerLocation EnteringCustomDungeon = FollowerLocation.None;
    public static FollowerLocation Add(CustomDungeon customDungeon)
    {
        var guid = TypeManager.GetModIdFromCallstack(Assembly.GetCallingAssembly());

        // The minted value is keyed by this name, so dungeons that share a Location seed - every
        // json dungeon does - need their InternalName to tell them apart. Without it the second
        // one mints the first one's value and the Add below throws.
        var key = string.IsNullOrEmpty(customDungeon.InternalName)
            ? customDungeon.Location.ToString()
            : customDungeon.InternalName;

        var innerType = GuidManager.GetEnumValue<FollowerLocation>(guid, key);
        customDungeon.Location = innerType;
        customDungeon.ModPrefix = guid;

        CustomDungeonList[innerType] = customDungeon;
        Plugin.Log.LogWarning($"Added: {innerType} {customDungeon.SceneName} {customDungeon.ModPrefix}");
        
        return innerType;
    }
}

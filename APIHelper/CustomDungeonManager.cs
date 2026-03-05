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

        var innerType = GuidManager.GetEnumValue<FollowerLocation>(guid, customDungeon.Location.ToString());
        customDungeon.Location = innerType;
        customDungeon.ModPrefix = guid;

        CustomDungeonList.Add(innerType, customDungeon);
        Plugin.Log.LogWarning($"Added: {innerType} {customDungeon.SceneName} {customDungeon.ModPrefix}");
        
        return innerType;
    }
}

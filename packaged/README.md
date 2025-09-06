# Links
- Read more about my mods at my [Website](https://cotlminimod.infernodragon.net/)
- Join the discord server for support, feedback, suggestions and general modding talk: [modding discord](https://discord.gg/MUjww9ndx2)
- If you like the mod, consider donating [here](https://ko-fi.com/infernodragon0)! Thank you for checking out the mod!

# Custom Spine Loader
Currently features:
- Custom Player Spines with multi skin select
- Override Existing Structure
- Custom Follower Color Control

### Custom Player Spine
- Completely change the look of your lamb!
![img](https://staticdelivery.nexusmods.com/mods/4736/images/49/49-1753503373-1249445029.png)

### Override Existing Structure
- Change the looks of any Structure! (right side is changed)
![img](https://raw.githubusercontent.com/InfernoDragon0/COTL-CustomSpineLoader/refs/heads/master/image.png)

### Custom Follower Color Control
- Fully control each follower's color and transparency!
![img](https://staticdelivery.nexusmods.com/mods/4736/images/49/49-1754472602-386733900.png)

More features to come soon!

### How to Load Player Skins
After installing this plugin correctly, you should be able to navigate to ```Bepinex > plugins > CotLSpineLoader > PlayerSkins``` folder and setup your custom spine skins there.

### Base Player Template Skin
- If you want to build your own skins, you may get the Base Player Template Skin via [NexusMods](https://www.nexusmods.com/cultofthelamb/mods/49?tab=files)

#### Exporting a usable Spine Skin
- Ensure that you have exported the Spine Skin via ```3.8.99 Spine```
- Export as a JSON file.
- Output: ```Nonessential data: TRUE, Animation cleanup: TRUE, Warnings: TRUE```
- Texture atlas: ```Pack TRUE``` with default Pack Settings
- If any warnings occur, it may be best to fix them, or the skin may not load into game.

#### Setting up the Player Spine Skin for Custom Spine Loader
- In the ```PlayerSkins``` folder, create a new folder for each individual spine that you want to load into game
- The folder should be named as your skin name, for this example, we will use ```DEBUGSKIN``` as the skin name
- In the folder ```PlayerSkins/DEBUGSKIN``` add the following files
- A .json file that is your exported Spine Skeleton
- An .atlas file that is your exported Spine Atlas
- Any amount of .png files that are packed together with it
- A config.json file which specifies settings, more info below.

A Complete Skin folder would look something like this
```
| CustomSpineLoader.dll
| PlayerSkins
    | DEBUGSKIN
        | player-main.json
        | player-main.png
        | player-main.atlas
        | config.json
```

### Config File For Spine Skins
Each Spine Skin folder must have a config.json file in it. The following is how you should create the file:
``` 
{   
    "defaultSkin": "CustomSkinName",
    "skins": [
        "CustomSkinName",
        "CustomSkinName2" 
    ]
}
```
defaultSkin will be the first skin that is loaded when the game starts

skins is an array of any amount of strings of the Skins that exist in your Spine Skeleton that you want to load into game

### How to override existing structure designs
Create a folder named after the structure you want to override in BuildingOverrides folder. Add images and a config.json file.

A Complete structure skin folder would look something like this
```
| CustomSpineLoader.dll
| BuildingOverrides
    | FISHING_HUT
        | fishing_hut_BACK.png
        | fishing_hut_FRONT.png
        | config.json
```

### Config file for Structure Overrides
Each Structure Override Skin folder must have a config.json file in it. The following is how you should create the file:
``` 
{
    "overrides": [
        {
            "spriteImageName": "fishing_hut_BACK.png",
            "offset": { "x": 0, "y": 0, "z": 0 },
            "scale": { "x": 1, "y": 1, "z": 1 },
            "rotation": { "x": 0, "y": 0, "z": 0 }
        },
        {
            "spriteImageName": "fishing_hut_FRONT.png",
            "offset": { "x": 0, "y": 0, "z": 0 },
            "scale": { "x": 1, "y": 1, "z": 1 },
            "rotation": { "x": 0, "y": 0, "z": 0 }
        },
    ]
}
```

Change your offset and rotation accordingly, so as to build your structure design.

Few things to note when building the custom structure design:
- when sprites are rendered at ROTATION 0,0,0 it will place the sprite flat against the ground.
- an OFFSET Z of at least -0.027 should be applied to flat sprites to remove z fighting, then to place other sprites above it, go more negative such as -0.04
- if the sprites should be facing the camera, a ROTATION of 300,0,0 is necessary

## Known Issues
- The Custom Player Spines may not have the correct color when attacking with certain weapons.

## Contributors
- Thumbnail Art by [LiteLikesArt](https://x.com/LiteLikesArt)
- Preview Skin by Fiore
- Preview Fishing Hut Structure by hallejr

## Developed by [InfernoDragon0](https://github.com/InfernoDragon0)

Try [CotLMiniMods](https://cult-of-the-lamb.thunderstore.io/package/InfernoDragon0/CotLMiniMods/) for lots of custom stuff, or [Supercharged Tarots](https://thunderstore.io/c/cult-of-the-lamb/p/InfernoDragon0/Supercharged_Tarots/) for overpowered tarots, and [Supercharged Followers](https://thunderstore.io/c/cult-of-the-lamb/p/InfernoDragon0/SuperchargedFollowers/) to bring your followers to battle!
# Instructions on Follower Forms
Read at [link](https://hackmd.io/@InfernoDragon0/HyxvdQ4wWg) for better formatting.


### Preparation of Follower Slots JSON File

- This file, followerSlots.json is an important part of building skins as it will provide you with the `SlotIndex` and `PartName` for each part you want to override later on.
- Every major update of CotL, the format of follower forms may change. This means that you will need to update your skins to match the new updated formats.
- Woolhaven's followerSlots.json has been provided to you in this instruction folder.
- In order to get the latest followerSlots for each update in the future, you will need to enable the `DumpFollowerSpineAtlas` config inside `Bepinex > config > InfernoDragon0.cotl.CustomSpineLoader.cfg` file. This will generate the followerSlots.json for that particular update.

### Folder Structure for Follower Forms

- The provided template folder, `ruffTemplateSkin` shows you the folder structure of a custom follower form, including how to setup the base and variant forms.
- Put the folder into the CultTweaker plugin folder so that it looks like `CultTweaker > FollowerSkins > ruffTemplateSkins`. This will allow the plugin to load the forms into game.

### Editing the Template Form

- Using base skin as an example, you will need the following things inside the folder.
  - A config.json file
  - any amount of image files for your parts
- The image files should be separated as sprites, and not an entire spritesheet.
- The image files can be of any name, as long as you specify it in the config.json file

The (truncated) config.json file looks like this:

```json
{
    "overrideBaseSkin": "Cat",
    "partConfigs": {
        "part2": {
            "SlotIndex": 89,
            "PartName": "HEAD_SKIN_BTM",
            "scaleX": 1.0,
            "scaleY": 1.0,
            "rotation": -90.0,
            "offsetX": 0.0,
            "offsetY": 0.0,
            "colorChoices": ["#FF0000"],
        },
        "part1back": {
            "SlotIndex": 89,
            "PartName": "HEAD_SKIN_BTM_BACK",
            "colorChoices": ["#FF0000"]
        } ...
    }
}
```

You can view the full version in the skin template.

- in this example, part2.png will be applied to Slot 89 at HEAD_SKIN_BTM, and part1back.png will be applied to Slot 89 at HEAD_SKIN_BTM_BACK.
- `overrideBaseSkin` sets the base skin to be used for the custom skin to apply over.
- `partConfigs` is an array of parts that you want to override.
- in `partConfigs`, the key (such as "part2") can be any name, if it matches an image file name in the same folder, it will use that image for the `SlotIndex/PartName` you specify in it.
- In each Key (such as "part2"), it accepts the following data:
  - `SlotIndex & PartName` (required) **can both be found in the followerSlot.json file. use this to specify which slot to override**.
  - `scaleX & scaleY` (optional, default: 1) are for you to increase or decrease the size of that part
  - `rotation` (optional, default: -90) is to rotate the part
  - `offsetX & offsetY` (optional, default: 0) are to move the part away from the center point.
  - `colorChoices `(optional, default: white) is an array of colors to choose from when indoctrinating. Ensure that all parts have the same number of colorChoices or it will not be added to game!
- Note: the key (such as part2) does not necessarily need an image file, it is optional to have an image file for each key, as you can also override just the colors (see template skin for more information).

### Migrating from JSONLoader

- Instead of having a spritesheet with all of the sprites in one image, you must separate them into single sprites.
- The JSON config file differs in the following ways:
  - JSON file should be named config.json, and be in the folder together with the skin
  - `colors` and `overrides` are both combined as `partConfig`
  - `name` is now `partName` and `slotIndex`
  - `rect` is not needed, it will automatically use the sprite size.

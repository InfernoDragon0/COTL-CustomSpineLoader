Tool icons for the in-game map editor (F4).

Drop a PNG here named exactly after the tool it belongs to, and the editor's dock
picks it up on the next launch - no code change, no config:

    Select.png      Shape.png      Structures.png   Enemies.png
    Podiums.png     Doors.png      Lighting.png     Music.png
    Clear.png       Load Map.png   Level.png

Square images work best; 128x128 is plenty (the buttons draw at 56px and preserve
aspect). Any tool without a file here falls back to Assets/colorwheel.png.

There is one non-tool icon with the same treatment:

    Save.png

It sits at the right end of the dock, past the separator.

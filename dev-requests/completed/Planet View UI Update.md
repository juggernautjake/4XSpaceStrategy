When in the Planet view with the maps visible, while hovering over the terrain tiles I want to have each tile the mouse is over to display its biome type and some basic information about it (resources present, avaerage temperature, etc) in a very small window that is somewhat transparent. 
The Text will need some contrast against the planets surface in case the Description Text and the tiles behind the text are similar color or shade.
The Tile description window should anchor its bottom left corner to the mouse with a very slight offset to the right of the mouse so that it is not under the mouse.
Fetch the Tile information from existing sources, do not create more assets to fulfil the data requirement.
The Tile Type Text can be the same color as the tile itself, and the information text such as temperature can be displayed as a color gradient from White going into Ice blue and then Yellow-Orange and Red.

White will be the color for the coldest Temperatures of all planets. light/Ice Blue will be the color for cold/cooler temperatures across all planets. Yellow-Orange will be for Warmer temperatures across all planets. Red will be for the Hottest temperatures across all planets.
When viewing Hot Planets (Volcanic/Hotter Barren Worlds) the Temp color gradient will likely never show as White or blue since those display colder/cooler Temperature.
Inversely when viewing Cold Planets (Ice Worlds/Cooler Barren Worlds) the Temp color gradient will likely never show as Red or Yellow-Orange as those colors display warmer/hotter temperatures.
If there is not already a planet Temperature setting for planet generation, add it so that planets have believable temperatures.
Look at terraforming logic if you need to determine how to best implement a realistic temperature range for planets.
Things like water level or even water being frozen, the BioSphere, etc. will all be dependant on the temperature of the planets. Can't have a BioSphere with too hot or too cold temperature.

In Planet View, with the maps of moons also available, the main planet map is zoomable and slideable via mouse interaction. 
Put this same zoom and sliding/panning functionality on the moons maps for when they are active.
The Tabs representing the moons are currently at the bottom middle of the planet view, I would like to move them to the Top left still within the planet view UI, just anchored to the inside top left corner and stack the tabs vertically.
Currently the tabs display the Names of the moons, I want the tabs to be small squares no larger than the height scale of existing tabs, and display a downscaled image of the moon it represents.
When hovering the mouse over these tabs, the Names of each moon and a description of the type of moon should display in a small window that should anchor its bottom left corner to the mouse with a very slight offset to the right of the mouse so that it is not under the mouse, Similarly to the Tile Information Window above.

Terrain Sandbox Mode needs to be changed. Some sliders will need to be replaced, some will change slightly.
Feature Scale can be left alone for now.
Elevation Slider will change into a Water Level slider.
Moisure will be replaced with a BioSphere slider.
Temperature will be given a greater overall temperature range.
Mountains slider can be done away with for now.


Elevation Slider in the Terrain Sandbox for editing planet/moon surfaces should be Replaced with Water Level Slider and be functional to show the level of water to accurately reflect how much is present on a planet/moons surface at any point during terraforming.
Currently the Elevation Slider makes the terrain more or less mountainous, the slider can be kept for Sandbox/Dev Mode but not be available in player mode.

Any Terraforming option that either adds or removes water should alter the Planet Terrain editor slider for Water Level appropriately so it can be viewed in realtime.
Ice/Cold planets should turn their water Tiles into cold versions of Water Grid Tiles (such as Ice, or snow, etc)

The Moisture slider in the Terrain Sandbox for editing planet/moon surfaces can be replaced or renamed as BioSphere slider, and should Generate plant life Tiles (grasslands, forests, swamps etc), in appropriate places.
The original Moisture slider did this to an extent, however I aim to restructure things a bit so they make more sense and can be somewhat self explanitory.
BioSphere cannot increase unless the other planets/moons Attributes are within limits. Water and atmosphere must exist on the planet/moon.
Temperature must be within the ranges of liquid water temperature for a BioSphere to generate. Too cold and the water will be frozen. Too hot and the Water will evaporate into the atmosphere (added water through terraforming will cool the planet and alter the surface tiles Magma->solidifies as the planet cools)

Temperature slider in the Terrain Sandbox for editing planet surfaces should have a minimum and maximum value that incluedes the coldest and hottest planets/moons at the far left and right sides of the slider.
Planets/moons with liquid water will be somewhere in the middle of this slider.

Planets must have a Water Level to indicate how much water is on a planet, whether frozen or liquid. Full slider (all the way to the right) will indicate the entire surface is covered in water, so if the slider is brought slightly down from full, Islands should begin to appear as water is pulled away.
Determines suitability for different Empire species
Volcanic/Hot worlds will likely have no water, and adding water through terraforming means would cool the planet down somewhat.
Ice/Cold worlds can have water, but it will be frozen into Ice Grid Tiles. If heating Cold planets through terraforming, Ice Grid Tiles or Snow Tiles can melt into water Tiles like Oceans or Lakes and Rivers.
Cold worlds should still allow for Sea Level to Increase with addition of water, however if the planet remains cold, the Water Level increase will just freeze into Ice covering more terrain. 
So in the case of planets/moons with Temperatures below water freezing level, the Water Level Slider being all the way to the right (covering the entire surface) will cover the entire surface of the planet/moon with Ice.
Increasing the planet temperature through terraforming means on an Ice world with maxed out Water Level Slider will result in an Ocean World (at liquid water Temperatures).

The Microbial Seeding Terraform option should increase the BioSphere slider in the Planet Terrain editor so as to Make it possible for Plantlife Tiles to spawn on previously barren/inhospitable worlds.
Planets/moons that are not generated with an active BioSphere cannot gain one by merely meeting the other conditions. Therefor Terraforming options such as the Microbial Seeding and other options that bring plantlife are necessary for these planets/moons to begin Generating a BioSphere in the first place.
On initial creation, requires the planet or moon to have a Stable and thick enough Atmosphere and a sufficient Water Level at liquid water temperatures.
Too thin an atmosphere, or too little water level, or Too Hot/Too Cold won't let the Microbial Seeding option succeed (give warning before activating if failure is likely)


==Advanced Planet Generation==
I want to remake the planet generation and have generation influenced by attribute factors instead of choosing from planet/moon(currently no moon types, this aims to fix that too) types to spawn
For example, a Moon around a Gas Giant that has an atmosphere, has liquid water, and is within the Habitable zone of the host Star, could very likely spawn as a temperate rocky type of world, just like the default world of the Terran Species.
I want to have planet/moon types and their generation parameters be based on prerequisite attribute settings such as Size, Temperature, Water Level, Tectonics, Atmosphere Thickness, BioSphere. All of which should be alterable after game initialization through terraforming means.

==Size==
Size of the celestial body (all planets, all moons, asteroids) will determine its grid size for map generation. Larger Size celestial bodies will have larger size surface grids, 2x1 length by height. Moon sized celestial bodies and asteroids will have much smaller grids.

Size can also determine the presence of tectonics and atmosphere.
Smaller Celestial bodies don’t have the mass to hold an atmosphere as well as a larger planet. Therefore asteroids will have no atmosphere and most moons will not have atmosphere or much atmosphere (thin atmosphere) unless they are large moons or the moons have tectonics.
Tectonics on moons also requires the moon to have a larger size.
Size can also determine the presence of Tectonics for planet sized celestial bodies. Smaller Celestial bodies will have lower chance of tectonic activity, but should not be impossible for it to spawn.
Size will also contribute to celestial bodies having thicker atmospheres.
Gas giants being the largest of planet sizes have extremely thick atmospheres and no ground terrain tiles. They’re Gas Giants after all.

==Temperature==
Planet temperature will be based mostly on distance to the host star (how much light it gets can also depend on star type), but can also be influenced by Atmosphere Thickness.
Planets warm enough to have liquid water, not too cold, not too hot, will THEN become temperate worlds such as Ocean worlds, Terran (rocky worlds), etc.
Planets that are hot because of their proximity to their sun would maybe become a scorched world, and Hot planets with Tectonic activity could become Molten worlds with volcanoes (which would generate Atmosphere).
Terraforming options that would aim to raise or lower the temperature of the planet will affect this Attribute after initial generation to suit species habitability.
The temperature setting slider should go from the low end of extreme cold (which can make ice worlds), and the opposite end of the temperature slider will be extreme heat (which can make volcanic worlds)
In Dev Mode, while moving the sandbox terrain sliders, temperature slider should allow for the planet to scroll from frozen worlds all the way to molten worlds in real time.

==Tectonics==
Some, not all, terrestrial planets should generate a Tectonics Layer to their Surface Map Generation that outlines the boundaries between continents. This Tectonics Layer Overlay can be added to the Survey Mode Index list for now. This overlay should put a slightly transparent White overlay on the planet map, and the borders of the generated continent shapes should have red highlighted grid tiles 1-3 grid tiles wide/thick. Generally only 1 thick but can have some variation if multiple continents' corners meet. This area would have a higher potential to spawn a volcano or mountain on the planets surface grid map, so during planetary generation, the tectonic plate overlay and the terrain grid should work together to influence the planet surface terrain.

While in Tectonics Overlay each continent should have a red arrow the same color as the fault line grid highlight that indicates the direction the continent is pushing, and the size of the arrow should indicate how strongly it is pushing in that direction.

Mountain ranges and potentially volcanoes within the mountain range should spawn on/around the fault lines (essentially how it works in real life) that are between two continental plates that are pushing towards each other or if one plate is pushing towards the other.

Continents will Influence Special events with Plate Tectonics.
Plate Tectonics will indicate areas around the fault lines that will damage infrastructure with events like Earthquakes, and change some of the surrounding surface tiles (maybe spawn a volcano or some tiles change elevation up or down enough to be noticeable in the Topography)

Players may want to avoid building on or near fault lines for their danger factors, but maybe rare or high quality mineral deposits are more common there, so there is some incentive to still attempt building there.

This Tectonics overlay can influence the Mineral Overlay during generation.
Tectonics will influence mountains spawning over fault lines on the planet surface, and tectonics can generate landmasses (continents) that share an edge with other continents while still varying elevation over itself to influence ocean formation and Water Level. On earth many fault lines run under the ocean, some on land are where mountain ranges have formed.

I don’t know the real life ratio of larger terrestrial planets that have tectonic activity vs no tectonic activity, so let’s make tectonic activity on terrestrial planets spawn 1/3 of the time(we may adjust this later) where it’s more likely for the larger planets to have tectonic Activity.

Planets with No tectonic activity will not have a tectonic overlay option.

==Atmosphere Thickness==
Atmosphere Thickness could retain more heat on planet from the host star or from geothermal activity within planet from tectonics(if any), or on planets with no tectonic activity (generates less heat from within) and a thick atmosphere the sun's heat would be reflected somewhat. Though it would need to be further from the sun or it would just get cooked anyways(can't reflect enough light to stay cool) Venus for example is further from the sun than Mercury, but it is hotter than Mercury because of its thick atmosphere and volcanic activity.

Atmosphere Thickness should also influence the Solar Index found in the Survey Tab, thinner to no atmosphere would be much better for Solar Panels since they don't have an atmosphere blocking/reflecting some of the sun's light away. While planets with thicker atmospheres would be worse for Solar and not as much sunlight would reach the planets surface (nearly-no/no tiles highlighted for Solar)

Atmosphere Thickness should heavily influence Wind Index found in the Survey Tab. Wind can only be possible on planets WITH an atmosphere. So thin to no atmosphere planets will have a very low to Zero Wind Index (nearly-no/no tiles highlighted for Wind)
Thickness should also be dependant on planet Size/Mass and a Magnetic Core
Small planets have a harder time holding atmosphere so during system generation I would like to reflect this.
However we do have moons of gas giants in our own solar system with thicker atmospheres than earth, so I think we should model that in our own solar system generation.

==Water Level==
Some species require water, a planet's Water Level will determine habitability for the species who require it.
Water Level is required for a BioSphere to be generated. As long as the temperature allows for the water level to have liquid water, and not frozen water, a biosphere is possible to sustain itself.
Water Level should be on a slider in Terrain Sandbox (replacing the Elevation slider)



==BioSphere==
A BioSphere is the plant life on a planet or moon surface. If there is an atmosphere and the temperature is consistent with liquid water temperature ranges AND there is water (water level slider) then a BioSphere can generate on planet creation.
Planets/moons with liquid water that have a lower water level will likely spawn less BioSphere tiles since plant life needs lots of water.
Ocean worlds/moons (celestial bodies with a very high Water Level in liquid water temperature ranges) can spawn algae in its oceans or coral reefs, (I believe reefs are already a grid tile asset for generation)

The higher the BioSphere slider value on generation of the celestial body (planet or moon) the more plant life tiles will spawn.
Provided the Biosphere depends on many other factors to be in balance or even when the slider is fully cranked up, the conditions might not be good enough for plant life (too hot/cold for liquid water, too thin/thick of an atmosphere, etc)

==Moon Generation==
Currently Moons only have one preset, and they're generally barren surfaces. Implementing these planet structuring attributes, Moons can be influenced by them as well.
Moons should also have their terrain generation influenced by these factors : Size, Temperature, Water Level, Tectonics, Atmosphere Thickness, BioSphere.
As long as the moons meet certain requirements, they too could spawn as ocean worlds, or temperate rocky worlds or even as Volcanic/Hot worlds or Ice/Cold worlds.
We should get much more variety with all Planet types and Moon Types from this System Generation Change.


# Changelog

## [1.0.0] - unreleased
- Deathrest capacity removed: a casket uses every deathrest building linked to it.
- Exponential casket power while deathresting (base x 1.5^n, configurable exponent).
- Deathrest buildings link to every casket in their room and can serve several deathresting pawns at once; their
  power draw and hemogen use scale with the number of caskets served.
- "Deathrest" inspector tab on caskets and deathrest buildings: toggle links, see status, presence and power cost.
- Deathrest capacity serum removed from traders, quest rewards and loot (setting; the item still works).
- Settings: capacity, per-building stack limits, exponent, building power and fuel scaling, serum.
- Fuel readout shows the real time left when a building serves several caskets.
- Compatible with modded deathrest buildings and caskets.
- Existing saves: vanilla building bindings are converted to casket links on load.

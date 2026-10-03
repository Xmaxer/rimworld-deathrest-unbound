# Changelog

## [1.0.0] - unreleased
- A casket uses every deathrest building linked to it. There is no deathrest capacity.
- A deathresting casket draws base power x 1.5^n, where n is the number of buildings it uses. The exponent is a setting.
- Deathrest buildings link to every casket in their room and can serve several deathresting pawns at once. Their
  power draw and hemogen use scale with the number of caskets they serve.
- A "Deathrest" tab on caskets and deathrest buildings toggles links and shows each link's status, presence and
  power cost.
- Traders, quest rewards and loot no longer offer the deathrest capacity serum. Existing serum still works, and a
  setting brings it back.
- Settings for capacity, per-building stack limits, the exponent, building power scaling, fuel scaling and the serum.
- The fuel readout shows the real time left when a building serves several caskets.
- Works with modded deathrest buildings and caskets.
- Loading an existing save converts vanilla building bindings to casket links.

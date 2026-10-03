# Deathrest Unbound

A RimWorld 1.6 mod (Biotech, Harmony) that removes the deathrest building cap and uses power as the limit
instead.

- **No capacity cap.** A deathresting pawn uses every deathrest building linked to their casket. This works for
  every xenotype with the deathrest gene.
- **Exponential casket power.** While deathresting, a casket draws `base × 1.5^n`, where `n` is the number of
  buildings it uses. The exponent is configurable.
- **Shared buildings.** Deathrest buildings link to every casket in their room and can serve several deathresting
  pawns at once. Their power draw and hemogen use scale linearly with the number of caskets they serve.
- **Link management.** A "Deathrest" inspector tab on caskets and buildings lets you toggle individual links
  and shows each link's status, presence and power cost. Disabled links never re-enable themselves.
- **Serum hidden.** The deathrest capacity serum is removed from traders, quest rewards and loot. The def stays,
  so saves still load.
- **Configurable.** Capacity, per-building stack limits, the exponent, building power and fuel scaling, and the
  serum can all be changed in the mod settings.

Works with modded caskets and buildings (Vanilla Races Expanded - Sanguophage is also supported). It is safe to add to an
existing save: vanilla bindings are converted to casket links on load.

## How it works

Vanilla stores one bound pawn and one presence counter on every deathrest building. This mod replaces that with
saved **links** (building ↔ casket) owned by `DeathrestLinkTracker`. While vanilla code, or another mod's patch,
runs for a pawn, `PawnContext` temporarily writes that pawn and the link's presence into the building comp, then
restores it afterwards. Vanilla `Apply`, `CanIncreasePresence` and `CompTickRare`, and third-party postfixes on
them, therefore run unchanged, once per pawn served.

| Path | What |
|---|---|
| `Source/DeathrestUnbound.Core` | Pure logic: power formulas and link-status resolution (unit-tested) |
| `Source/DeathrestUnbound` | The mod: tracker, pawn context, Harmony patches, links tab, settings |
| `Source/DeathrestUnbound.DevHarness` | In-game automated scenarios and the play-test playground (never shipped) |
| `1.6/Patches/HideSerum.xml` | Serum removal, gated on the `hideSerum` setting |

## Development

Needs the .NET SDK, RimWorld 1.6 on Steam, and Harmony (plus VEF and VRE Sanguophage for the compatibility
scenarios) subscribed on the Workshop.

```
make test         # unit tests
make smoke        # full automated in-game suite (Steam running, RimWorld closed), ~5 min
make smoke-novre  # the same without VRE Sanguophage
make play         # isolated play-test profile; start a NEW colony to get the deathrest playground
make package      # dist/DeathrestUnbound, ready to upload
```

`make smoke` and `make play` use their own save-data folders (`tools/.smoke-profile`, `tools/.dev-profile`) and
their own mod lists, so your real saves, settings and mod list are never touched.

### Automated scenarios

| Scenario | Checks |
|---|---|
| boot | The mod loads and the links tab is registered on every deathrest def |
| serum | The serum is gone from every trader and thing-set maker, and the def is kept |
| uncap | 8 buildings on capacity 1, presence, efficiency, casket `100×1.5^8`, building draw |
| shared | 2 pawns share buildings: power ×2, hemogen ×2, wake, disable, rescans keep disabled links |
| bonuses | Shared buildings apply effects to each pawn that met the 75% presence rule, and only to them |
| devwake | Vanilla's dev "Wake and apply bonuses" button: powered caskets get all bonuses, an unpowered one none |
| limits | Stack limits, per-def overrides and vanilla capacity (all opt-in), plus the gizmo's `∞` |
| rooms | Same-room linking, linking after rooms merge, dormant links after a split, cleanup on destroy |
| unpowered | An unpowered casket keeps demanding power, and its buildings stop working and drawing |
| migration | Vanilla bindings from old saves are converted |
| ui | Links tab (casket and building), gizmo and settings rendered without errors, with screenshots |
| vre | VRE buildings link and share, the Invocation Matrix ability and Psychofluid Well psyfocus reach both pawns |
| perf | 6 pawns and 330 links: per-tick cost reported, and the run fails above 2 ms |
| playground-check | Builds the `make play` hall, deathrests its 3 pawns: all powered, all enabled links in use, bonuses apply |
| saveload | Saves, reloads in-session, and verifies link state (enabled flags, presence) |

## Releasing

1. `make smoke` and `make smoke-novre` pass.
2. `make package` builds `dist/DeathrestUnbound`, which `make deploy` links into `RimWorld/Mods/DeathrestUnbound`.
3. In RimWorld (dev mode), go to Mods, select **Deathrest Unbound** (not the Dev Harness), and click Upload.
   Steam writes `About/PublishedFileId.txt` into `dist/`, and `package.ps1` keeps it across repackages.
4. On the Workshop page, paste `workshop/description.bbcode.txt` and add `workshop/gallery/*.png` as screenshots.

Art: `About/Preview.png` and `About/ModIcon.png` were generated with `tools/asset-pipeline/generate_asset.py`
(Gemini; reads `GEMINI_API_KEY` from the environment). `title_preview.py` adds the title, and `crop_gallery.py`
cuts gallery shots from the harness screenshots (`make smoke SCENARIOS=ui,playground-check` first).

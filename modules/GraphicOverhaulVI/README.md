# GraphicOverhaulVI

**Status:** active priority  
**Project:** Kaguya — GTA V Enhanced → GTA VI-style overhaul  
**Scope lock date:** 2026-09-06

GraphicOverhaulVI is the current priority of the Kaguya project. Its job is to rebuild GTA V Enhanced's visual presentation as one coherent, standalone graphics pack rather than stack unrelated graphics mods.

The target is not "GTA V with a prettier LUT". The target is a modernized rendering, material, geometry and asset pipeline whose final image, motion and surface response no longer obviously read as GTA V-era rendering.

## Scope lock

For this phase, complex gameplay behavior, procedural interaction and character-behavior work are **out of scope**. Existing gameplay modules remain in the repository but are not the active priority.

Work order is locked to:

1. **ColorCoreVI** — color-management / HDR / tonemapping pipeline.
2. Camera and exposure.
3. Lighting, RT and shadows.
4. Atmosphere and weather.
5. Materials.
6. World textures, geometry and LOD.
7. Characters and clothing.
8. Vehicles.
9. Water, rain and VFX.
10. Performance.
11. Visual QA and final packaging.

A later block must not become the main development focus until the current block has a validation checkpoint.

## Reference-mod policy

Reference mods such as VisualV may be studied to understand which GTA files and systems they improve and why their result works. Third-party binaries/assets are not committed or redistributed unless their license explicitly permits it.

For each reference component, GraphicOverhaulVI chooses one of four actions:

- **KEEP PRINCIPLE** — reproduce the useful principle in our own implementation.
- **EXTEND** — use the idea as a baseline and push it substantially further.
- **REPLACE** — build a different solution because the reference is insufficient for the target.
- **REJECT** — do not carry the component into the final pack.

The final installation must not require the user to stack VisualV + QuantV + NVE + unrelated presets. GraphicOverhaulVI must become the coherent end product.

## Release rule

Any release explicitly presented as a complete GraphicOverhaulVI version must be **complete and autonomous**: it contains every previously validated component still required by the project. Incremental experiments must be clearly named as experiments/patches and must never be presented as replacements for the full pack.

---

# MASTER TODO

Checkbox state is the source of truth for production status.

## 00 — Infrastructure and references

- [ ] Establish a clean GTA V Enhanced visual baseline.
- [ ] Define the permanent `GraphicOverhaulVI` file/package layout.
- [ ] Inventory GTA V Enhanced visual configuration files, timecycles, shader-relevant files, textures and assets touched by the project.
- [ ] Maintain a reference-mod matrix: file touched / purpose / quality / legal reuse status / our decision.
- [ ] Build deterministic visual test scenes: same location, camera, FOV, time, weather, vehicle and character.
- [ ] Cover day, night, interior, tunnel, rain, storm, desert, city, mountain and coast test scenes.
- [ ] Capture vanilla Enhanced baseline frames.
- [ ] Capture reference-mod frames where legally/technically possible.
- [ ] Capture Forza Horizon 6 reference frames for image-pipeline comparison.
- [ ] Maintain real-world photographic references for Southern California-like lighting/material response.
- [ ] Record FPS, frametime, 1% lows and VRAM for every validated patch.
- [ ] Make development modules independently switchable for A/B tests.
- [ ] Keep full releases cumulative and autonomous.

## 01 — ColorCoreVI — PRIORITY ABSOLUTE

### FH6 / Gran Turismo reference analysis

- [ ] Inventory FH6 files related to shaders, post-processing, color, HDR, tonemapping and display output.
- [ ] Identify likely shader containers and shader metadata.
- [ ] Identify references to tonemap / tone mapping.
- [ ] Identify exposure pipeline references.
- [ ] Identify gamut / color-space transforms.
- [ ] Identify LUT usage and distinguish corrective LUTs from artistic grading.
- [ ] Identify HDR output code paths.
- [ ] Identify SDR output code paths.
- [ ] Identify PQ / ST.2084 references.
- [ ] Identify BT.2020 / Rec.2020 references.
- [ ] Identify scRGB / linear intermediate usage where visible.
- [ ] Identify white-point assumptions.
- [ ] Compare findings with the documented Gran Turismo wide-gamut/HDR philosophy.
- [ ] Separate confirmed implementation facts from hypotheses.

### GTA V Enhanced pipeline mapping

- [ ] Locate the final post-processing chain accessible to us.
- [ ] Determine where GTA V performs exposure.
- [ ] Determine where GTA V performs tonemapping.
- [ ] Determine where GTA V performs color correction / grading.
- [ ] Determine where HDR conversion occurs.
- [ ] Determine which parts can be overridden natively and which require injection/hooking.
- [ ] Map interactions with DLAA.
- [ ] Map interactions with DLSS.
- [ ] Map interactions with RTGI/RTAO/RT reflections/RT shadows.

### Target ColorCoreVI behavior

- [ ] Keep scene-referred/scene-linear data as long as technically possible.
- [ ] Define project white point; D65 is the initial reference unless measurements prove otherwise.
- [ ] Define a wide-gamut working space.
- [ ] Evaluate BT.2020 as the principal output/reference gamut.
- [ ] Prevent premature RGB clipping.
- [ ] Implement controlled gamut mapping.
- [ ] Preserve saturated colors in highlights.
- [ ] Preserve highlight detail.
- [ ] Preserve useful shadow detail without raising blacks artificially.
- [ ] Eliminate unnecessary crushed blacks.
- [ ] Eliminate avoidable clipped whites.
- [ ] Calibrate reds, greens and blues.
- [ ] Calibrate skin-tone behavior.
- [ ] Calibrate saturated automotive paints.
- [ ] Calibrate colored emissive/light sources.
- [ ] Calibrate sky and solar highlights.
- [ ] Produce a correct SDR path.
- [ ] Produce a correct HDR10 path.
- [ ] Validate PQ/ST.2084 behavior in HDR.
- [ ] Validate Windows HDR interaction.
- [ ] Ensure artistic LUTs are optional creative layers, not repairs for a broken pipeline.
- [ ] Validate OBS capture.
- [ ] Validate YouTube HDR → SDR presentation.

**Validation gate C01:** the same controlled scene must remain believable in SDR and HDR without a corrective ReShade LUT, with no obvious highlight hue shift, uncontrolled clipping, lifted blacks or crushed shadow detail.

## 02 — Camera and exposure

- [ ] Rebuild auto-exposure behavior.
- [ ] Tune bright → dark adaptation.
- [ ] Tune dark → bright adaptation.
- [ ] Prevent exposure pumping from small bright objects.
- [ ] Prevent excessive exposure changes caused only by camera orientation.
- [ ] Validate exterior daylight exposure.
- [ ] Validate interiors.
- [ ] Validate tunnel entry and exit.
- [ ] Validate strong backlight.
- [ ] Validate direct sun in frame.
- [ ] Validate night light sources.
- [ ] Rework bloom threshold and energy.
- [ ] Rework glare.
- [ ] Rework lens flare.
- [ ] Remove unjustified fake optical effects.
- [ ] Review chromatic aberration.
- [ ] Review vignette.
- [ ] Review film grain.
- [ ] Rework motion blur around plausible shutter behavior.
- [ ] Rework depth of field and focus transitions.
- [ ] Improve bokeh/CoC where technically possible.

**Validation gate C02:** exposure changes must feel camera-like rather than game-scripted across the full reference-scene suite.

## 03 — Sun, sky and global illumination

- [ ] Audit sun position/trajectory.
- [ ] Tune sun intensity.
- [ ] Tune solar color temperature by time of day.
- [ ] Rebuild sunrise.
- [ ] Rebuild golden hour.
- [ ] Rebuild midday.
- [ ] Rebuild sunset.
- [ ] Rebuild blue hour.
- [ ] Rebuild night sky illumination.
- [ ] Audit moon illumination and presentation.
- [ ] Tune atmospheric sky contribution.
- [ ] Tune shadow chromatic response.
- [ ] Tune ambient light intensity.
- [ ] Balance direct and indirect illumination.
- [ ] Audit Enhanced RTGI behavior.
- [ ] Audit RTAO behavior.
- [ ] Reduce light leaking.
- [ ] Avoid over-occlusion.
- [ ] Recover implausibly dark spaces.
- [ ] Improve perceived bounce lighting.
- [ ] Enforce interior/exterior lighting continuity.

## 04 — Shadows

- [ ] Audit RT shadows.
- [ ] Audit cascaded shadow maps/fallbacks.
- [ ] Improve resolution where useful.
- [ ] Improve shadow draw distance.
- [ ] Improve contact shadows.
- [ ] Validate small-prop shadows.
- [ ] Validate character shadows.
- [ ] Validate vehicle shadows.
- [ ] Validate vegetation shadows.
- [ ] Improve alpha-tested vegetation shadowing.
- [ ] Tune penumbra by source size where accessible.
- [ ] Fix shadow bias issues.
- [ ] Reduce peter-panning.
- [ ] Reduce shadow acne.
- [ ] Validate multiple night light sources.
- [ ] Validate headlight shadows.
- [ ] Validate streetlight shadows.

## 05 — Artificial lighting

- [ ] Streetlights.
- [ ] Interior lamps.
- [ ] Neon.
- [ ] LED.
- [ ] Fluorescent sources.
- [ ] Incandescent sources.
- [ ] Industrial lighting.
- [ ] Shop windows.
- [ ] Signs.
- [ ] Lit windows.
- [ ] Headlights.
- [ ] Tail lights.
- [ ] Indicators.
- [ ] Emergency lights.
- [ ] Light draw distance.
- [ ] Plausible falloff.
- [ ] Plausible color temperature.
- [ ] Plausible source intensity.
- [ ] Separate emissive appearance from actual illumination.
- [ ] Remove/reduce unrealistic coronas.
- [ ] Keep glare only where exposure/source energy justifies it.

## 06 — Atmosphere and weather

- [ ] Audit every GTA V weather type used in Story Mode.
- [ ] CLEAR.
- [ ] EXTRASUNNY.
- [ ] CLOUDS.
- [ ] OVERCAST.
- [ ] SMOG.
- [ ] FOGGY.
- [ ] RAIN.
- [ ] THUNDER.
- [ ] CLEARING.
- [ ] NEUTRAL.
- [ ] Snow variants if retained.
- [ ] Rebuild weather transitions.
- [ ] Model believable atmospheric humidity visually.
- [ ] Rework haze.
- [ ] Rework volumetric fog.
- [ ] Tune fog density by context/altitude where possible.
- [ ] Rework atmospheric distance response.
- [ ] Rework solar scattering appearance.
- [ ] Improve horizon behavior.
- [ ] Represent urban atmospheric pollution without a global color filter.
- [ ] Improve cloud forms.
- [ ] Improve cloud density.
- [ ] Improve cloud color.
- [ ] Improve cloud illumination/shadowing.
- [ ] Improve cloud motion.
- [ ] Improve cloud/weather transitions.
- [ ] Improve storm presentation.
- [ ] Improve lightning.
- [ ] Improve lightning illumination of the world.

## 07 — MaterialsVI

- [ ] Inventory material/shader families used by GTA V Enhanced.
- [ ] Document limitations of each relevant shader family.
- [ ] Standardize base-color authoring.
- [ ] Standardize roughness response.
- [ ] Standardize metallic response.
- [ ] Improve normal maps.
- [ ] Introduce micro-normal detail where useful.
- [ ] Audit specular response.
- [ ] Audit AO usage.
- [ ] Use height/parallax only where it materially improves geometry perception.
- [ ] Improve clear-coat materials.
- [ ] Improve transmission where supported.
- [ ] Improve subsurface response where supported.
- [ ] Investigate anisotropy where useful.
- [ ] Create calibrated project references for dry asphalt.
- [ ] Wet asphalt.
- [ ] Concrete.
- [ ] Soil.
- [ ] Sand.
- [ ] Wood.
- [ ] Painted surfaces.
- [ ] Raw metal.
- [ ] Aluminium.
- [ ] Steel.
- [ ] Chrome.
- [ ] Plastic.
- [ ] Rubber.
- [ ] Fabric.
- [ ] Leather.
- [ ] Glass.
- [ ] Water.
- [ ] Skin.
- [ ] Hair.
- [ ] Validate each material under day/night/rain/interior lighting.

## 08 — World textures

- [ ] Roads.
- [ ] Pavements.
- [ ] Kerbs.
- [ ] Road markings.
- [ ] Walls.
- [ ] Building facades.
- [ ] Roofs.
- [ ] Windows.
- [ ] Floors.
- [ ] Car parks.
- [ ] Tunnels.
- [ ] Drains/sewers.
- [ ] Street furniture.
- [ ] Signs.
- [ ] Traffic signs.
- [ ] Graffiti.
- [ ] Litter.
- [ ] Rocks.
- [ ] Cliffs.
- [ ] Mountains.
- [ ] Beaches.
- [ ] Desert surfaces.
- [ ] Terrain.
- [ ] Replace visibly inadequate source resolution.
- [ ] Enforce coherent texel density.
- [ ] Regenerate/validate mipmaps.
- [ ] Validate anisotropic filtering.
- [ ] Do not treat blind AI x4 upscaling as finished asset work.

## 09 — World geometry and models

- [ ] Identify assets whose silhouette visibly exposes GTA V-era geometry.
- [ ] Add geometry only where silhouette, shading or close-up detail benefits.
- [ ] Add sensible bevels to unnaturally razor-sharp edges.
- [ ] Improve pipes.
- [ ] Improve poles.
- [ ] Improve railings/grilles.
- [ ] Improve benches.
- [ ] Improve streetlights.
- [ ] Improve repeated street furniture.
- [ ] Improve frequently viewed close props.
- [ ] Improve important interiors.
- [ ] Improve commonly reused mission props when safe.
- [ ] Correct normals.
- [ ] Correct tangents.
- [ ] Correct UVs.
- [ ] Change collision only when required.
- [ ] Rebuild LOD0 where required.
- [ ] Rebuild LOD1 where required.
- [ ] Rebuild LOD2 where required.
- [ ] Audit SLOD.
- [ ] Improve LOD transitions.
- [ ] Reduce pop-in.
- [ ] Increase useful draw distances without destroying performance.
- [ ] Investigate improved impostor/HLOD strategy where feasible.

## 10 — Vegetation

- [ ] Trees.
- [ ] Trunks.
- [ ] Branches.
- [ ] Leaves.
- [ ] Leaf normals.
- [ ] Leaf translucency.
- [ ] Grass.
- [ ] Bushes.
- [ ] Flowers.
- [ ] Palms.
- [ ] Cacti.
- [ ] Density.
- [ ] Species/asset variety.
- [ ] Color variation.
- [ ] Lighting response.
- [ ] Shadows.
- [ ] Reflection behavior.
- [ ] Alpha dithering.
- [ ] LOD.
- [ ] Draw distance.
- [ ] Visual wind only when it belongs to the rendering scope.

## 11 — Water

- [ ] Ocean.
- [ ] Pools.
- [ ] Rivers.
- [ ] Canals.
- [ ] Puddles.
- [ ] Reflection.
- [ ] Refraction.
- [ ] Depth absorption.
- [ ] Physically plausible color response.
- [ ] Wave normals.
- [ ] Micro-waves.
- [ ] Foam.
- [ ] Shoreline.
- [ ] Caustics.
- [ ] Underwater rendering.
- [ ] Rain/water interaction.
- [ ] RT reflections where appropriate.

## 12 — Rain and wet surfaces

- [ ] Rain drops.
- [ ] Rain density.
- [ ] Rain velocity/presentation.
- [ ] Drop illumination.
- [ ] Splashes.
- [ ] Ripples.
- [ ] Puddles.
- [ ] Wetness masks/maps.
- [ ] Dynamic roughness.
- [ ] Plausible wet darkening.
- [ ] Wet reflections.
- [ ] Progressive drying after rain if technically feasible.
- [ ] Wet vehicles.
- [ ] Wet characters.
- [ ] Wet glass.
- [ ] Tire spray.

## 13 — Vehicles

- [ ] Automotive paint model.
- [ ] Clear coat.
- [ ] Metallic flakes where feasible.
- [ ] Reflection quality.
- [ ] RT reflections.
- [ ] SSR fallback.
- [ ] Glass.
- [ ] Glass tint.
- [ ] Fresnel response.
- [ ] Chrome.
- [ ] Aluminium.
- [ ] Plastics.
- [ ] Tires.
- [ ] Wheels.
- [ ] Brake discs.
- [ ] Calipers.
- [ ] Headlights.
- [ ] Internal lamp optics.
- [ ] Tail lights.
- [ ] Interiors.
- [ ] Leather.
- [ ] Fabrics.
- [ ] Screens.
- [ ] Gauges.
- [ ] Improve geometry on vehicles whose meshes visibly fail the target.
- [ ] Vehicle LOD.
- [ ] Dirt.
- [ ] Dust.
- [ ] Water.
- [ ] Scratches/damage presentation.

## 14 — Characters

- [ ] Skin shader.
- [ ] Subsurface response.
- [ ] Skin roughness variation.
- [ ] Region-specific face response: forehead/cheeks/lips/nose.
- [ ] Pore detail.
- [ ] Micro-normal detail.
- [ ] Eyes.
- [ ] Cornea.
- [ ] Iris.
- [ ] Eye moisture.
- [ ] Teeth.
- [ ] Tongue.
- [ ] Nails.
- [ ] Hair.
- [ ] Beards.
- [ ] Eyebrows.
- [ ] Eyelashes.
- [ ] Hair transparency/anisotropy where technically possible.
- [ ] Improve insufficient head geometry.
- [ ] Improve ears where needed.
- [ ] Improve hands.
- [ ] Improve feet when visible.
- [ ] Correct normals.
- [ ] Character LOD.
- [ ] Preserve NPC variation.

## 15 — Clothing

- [ ] Inventory protagonist clothing first.
- [ ] Inventory high-frequency NPC clothing next.
- [ ] Identify garments whose silhouette is visibly low-poly.
- [ ] Controlled retopology/subdivision.
- [ ] Preserve rig compatibility.
- [ ] Correct skin weights.
- [ ] Correct UVs.
- [ ] Correct normals/tangents.
- [ ] Model visible seams where geometry is justified.
- [ ] Add real collar thickness where visible.
- [ ] Improve hems.
- [ ] Improve zips.
- [ ] Improve buttons.
- [ ] Improve laces.
- [ ] Keep fiber-scale detail primarily in materials rather than wasteful geometry.
- [ ] Calibrate cotton.
- [ ] Denim.
- [ ] Leather.
- [ ] Synthetics.
- [ ] Nylon.
- [ ] Metal accessories.
- [ ] Shoes.
- [ ] Hats/caps.
- [ ] Glasses.
- [ ] Jewellery.
- [ ] Watches.
- [ ] Validate against vanilla animation set.
- [ ] Cloth simulation/behavior remains outside current GraphicOverhaulVI scope unless required to fix a rendering defect.

## 16 — Particles and VFX

- [ ] Smoke.
- [ ] Fire.
- [ ] Sparks.
- [ ] Explosions.
- [ ] Dust.
- [ ] Debris.
- [ ] Engine smoke.
- [ ] Exhaust.
- [ ] Condensation.
- [ ] Tracers.
- [ ] Muzzle flashes.
- [ ] Impacts.
- [ ] Blood rendering where relevant.
- [ ] Heat haze.
- [ ] Validate every emissive VFX against HDR/exposure.

## 17 — Reflections

- [ ] RT reflections.
- [ ] SSR.
- [ ] Cubemaps/probes.
- [ ] Vehicles.
- [ ] Buildings.
- [ ] Glass.
- [ ] Water.
- [ ] Wet ground.
- [ ] Interiors.
- [ ] Reflection resolution.
- [ ] Reflection distance.
- [ ] Rough reflections.
- [ ] Off-screen object behavior.
- [ ] Prevent implausible mirror-like rough surfaces.

## 18 — Interiors

- [ ] Exposure.
- [ ] RTGI.
- [ ] Bounce light.
- [ ] Windows.
- [ ] Light probes/fallbacks.
- [ ] Local light sources.
- [ ] Interior materials.
- [ ] Interior shadows.
- [ ] Interior reflections.
- [ ] Low-poly geometry.
- [ ] Low-resolution textures.
- [ ] Interior/exterior transitions.
- [ ] Night interiors.

## 19 — Night rendering

- [ ] Night sky.
- [ ] Moon.
- [ ] Stars.
- [ ] Light pollution.
- [ ] Downtown Los Santos brightness.
- [ ] Truly dark rural areas where appropriate.
- [ ] Preserve useful shadow detail without fake lifted blacks.
- [ ] Make headlights visually useful.
- [ ] Streetlights.
- [ ] Signs.
- [ ] Windows.
- [ ] Reflections.
- [ ] Night exposure.
- [ ] Avoid global fake-blue night filters.

## 20 — Draw distance and visual density

- [ ] Object draw distance.
- [ ] Vehicle LOD distance.
- [ ] Ped LOD distance.
- [ ] Vegetation LOD distance.
- [ ] Building LOD distance.
- [ ] Shadow distance.
- [ ] Light distance.
- [ ] Reflection distance.
- [ ] Texture streaming.
- [ ] Pop-in.
- [ ] SLOD.
- [ ] Skyline quality.
- [ ] Distant traffic visual representation when it belongs to rendering scope.

## 21 — Remove obvious GTA V-era visual tells

- [ ] Excess halos.
- [ ] Excess coronas.
- [ ] Ugly dithering.
- [ ] Blurry source textures.
- [ ] Blocky shadows.
- [ ] Simplistic reflections.
- [ ] LOD pops.
- [ ] Fake glow.
- [ ] Fog used only to hide distance.
- [ ] Excess saturation.
- [ ] Artificial weather tints.
- [ ] Plastic-looking materials.
- [ ] Unnecessarily angular close assets.
- [ ] Fake crushed blacks.
- [ ] Emissive lamps that do not illuminate anything.
- [ ] Flat windows.
- [ ] Cardboard-looking vegetation.
- [ ] Artificial blue/transparent water.
- [ ] Waxy characters.

## 22 — Performance

- [ ] Define GPU cost per module.
- [ ] Define VRAM cost per module.
- [ ] Track CPU cost.
- [ ] Track frametime, not only average FPS.
- [ ] Track 1% lows.
- [ ] Detect shader compilation stutter.
- [ ] Tune texture streaming.
- [ ] Tune LOD cost.
- [ ] Validate DLAA.
- [ ] Validate DLSS.
- [ ] Validate 2560×1440.
- [ ] Validate 3440×1440.
- [ ] Preserve headroom for future non-graphics systems.
- [ ] Never validate performance from a stationary camera only.

## 23 — Visual QA

- [ ] Test every weather at multiple times of day.
- [ ] Test every major map biome/region.
- [ ] City.
- [ ] Desert.
- [ ] Mountain.
- [ ] Coast/ocean.
- [ ] Interiors.
- [ ] Tunnels.
- [ ] Underwater.
- [ ] High-speed driving.
- [ ] Flight/long-distance views.
- [ ] Characters.
- [ ] Light/dark/saturated vehicles.
- [ ] SDR.
- [ ] HDR.
- [ ] Still screenshots.
- [ ] OBS video capture.
- [ ] Night.
- [ ] Rain.
- [ ] Storm.
- [ ] No patch is validated from one attractive screenshot alone.

## 24 — Packaging

- [ ] GraphicOverhaulVI becomes the only graphics pack required by the final installation.
- [ ] No mandatory separate VisualV installation.
- [ ] No mandatory stack of unrelated graphics overhauls.
- [ ] Redistribute third-party components only when permission/license allows it.
- [ ] Reimplement useful principles when redistribution is not allowed.
- [ ] Provide install process.
- [ ] Provide uninstall/vanilla restore process.
- [ ] Provide logs.
- [ ] Detect supported GTA V Enhanced versions.
- [ ] Detect known conflicts.
- [ ] Every complete release is cumulative/autonomous.

---

# Patch registry

Patch IDs are permanent. A patch can be superseded but its historical record is never deleted.

| Patch | Date | Area | Status | Purpose |
|---|---|---|---|---|
| `P0001` | 2026-09-06 | Project governance | **APPLIED** | Lock GraphicOverhaulVI scope, work order, master TODO, validation gates and checkpoint system. |
| `P0002` | 2026-09-06 | ColorCoreVI | **IN PROGRESS** | Add a read-only FH6/GTA V Enhanced color-pipeline inventory scanner and establish the first ColorCoreVI evidence baseline. |

Patch statuses: `PLANNED`, `IN PROGRESS`, `TEST`, `APPLIED`, `REJECTED`, `SUPERSEDED`.

---

# Checkpoint rule

A checkpoint is a durable project decision or validated technical state, not a casual test result. Checkpoints are mirrored in `CHECKPOINTS.md` and important scope/order decisions are also stored in ChatGPT memory.

A checkpoint must contain:

- permanent ID (`CP-xxxx`);
- date;
- state (`LOCKED`, `VALIDATED`, `REOPENED`, `SUPERSEDED`);
- exact decision/result;
- evidence or patch IDs;
- what is explicitly not being changed;
- next allowed block.

Current checkpoints are maintained in [`CHECKPOINTS.md`](CHECKPOINTS.md).

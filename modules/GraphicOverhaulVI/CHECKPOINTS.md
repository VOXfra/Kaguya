# GraphicOverhaulVI — Durable checkpoints

This file is the durable project-state ledger. It exists so the project can be resumed without reconstructing decisions from chat history.

Do not rewrite historical checkpoints to make later work look cleaner. If a decision changes, mark the old checkpoint `REOPENED` or `SUPERSEDED` and add a new checkpoint.

## CP-0001 — GraphicOverhaulVI becomes active priority

- **Date:** 2026-09-06
- **State:** `LOCKED`
- **Decision:** The GTA V → VI project stops spreading active effort across unrelated systems. `GraphicOverhaulVI` is the current production priority.
- **In scope:** rendering/color pipeline, exposure/camera image response, lighting/RT, atmosphere/weather, materials, textures, geometry/LOD, vegetation, water/rain, characters/clothing visuals, vehicles, VFX, reflections, interiors/night, draw distance, performance, QA and packaging.
- **Out of scope for this phase:** complex character behavior, procedural interactions, gameplay AI and other behavior systems unless a tiny change is strictly required to fix a visual/rendering defect.
- **Evidence:** `P0001`.
- **Next allowed block:** ColorCoreVI.

## CP-0002 — ColorCoreVI is first technical block

- **Date:** 2026-09-06
- **State:** `LOCKED`
- **Decision:** Color management is completed before materials, weather art direction or asset remastering are treated as final, because those systems must be judged through a stable image pipeline.
- **Reference direction:** Gran Turismo's documented wide-gamut/HDR philosophy is a reference target. Forza Horizon 6 is inspected locally to determine its actual implementation. No claim that FH6 literally contains Gran Turismo code is accepted without evidence.
- **Target topics:** scene-linear handling where accessible, wide-gamut work/output, white point, gamut mapping, tonemapping, HDR10/PQ, SDR mapping, exposure interaction, highlight hue preservation and clipping control.
- **Evidence:** `P0001`, `P0002`.
- **Validation required:** `C01` in the master TODO.
- **Next allowed block:** camera/exposure only after C01 is validated.

## CP-0003 — Reference mods are references, not the final dependency stack

- **Date:** 2026-09-06
- **State:** `LOCKED`
- **Decision:** VisualV and other graphics mods may be inspected component by component. GraphicOverhaulVI decides whether to reproduce the principle, extend it, replace it or reject it. The final pack must be coherent and should not require a pile of separate graphics mods.
- **Legal constraint:** third-party binaries/assets are not committed or redistributed unless their license/permission allows it.
- **Evidence:** `P0001`.

## CP-0004 — Complete releases are autonomous

- **Date:** 2026-09-06
- **State:** `LOCKED`
- **Decision:** Any ZIP/version described as a complete GraphicOverhaulVI release must contain all still-required components from previous validated releases. Experimental/additive patches are named explicitly and never presented as full replacements.
- **Evidence:** `P0001`.

## CP-0005 — Evidence before imitation

- **Date:** 2026-09-06
- **State:** `VALIDATED`
- **Decision:** The FH6/GT reference phase starts with read-only evidence collection. Confirmed files/containers are separated from hypotheses before implementing a GTA V replacement.
- **Evidence:** `P0002` completed on the user's real installations with 16,688 files indexed, 3,367 candidates and 0 scan errors.
- **Result:** the inventory was sufficient to select a targeted second-stage inspection instead of continuing broad scans.

## CP-0006 — FH6 exposes explicit ColorCore container families

- **Date:** 2026-09-06
- **State:** `VALIDATED`
- **Decision/result:** P0002 identified native FH6 resources whose names directly map to the ColorCoreVI problem: `media\colourgrades.zip`, `media\displaymappers.zip`, `media\postEffects.zip`, `media\_library\Shaders.zip`, plus `media\timeofday\TimeOfDay*.xml`, weather presets and supporting camera/sky resources.
- **Important caveat:** the broad P0002 keyword score is not itself evidence quality. The FH6 install also contains a `reshade-shaders` folder and the v0.1.x keyword matcher produced substring noise. P0003 therefore targets native paths explicitly and does not treat ReShade hits as FH6 engine evidence.
- **Evidence / patch IDs:** `P0002`, `P0003`.
- **Validation result:** P0003 collected the native target set from the user's real FH6 install and exposed the actual LUT/display-mapper structure.

## CP-0007 — FH6 separates HDR-domain artistic grading from SDR/HDR display mapping

- **Date:** 2026-09-06
- **State:** `LOCKED`
- **Decision/result:** Real P0003 evidence shows a modular FH6 color architecture rather than a single final-screen LUT.
- **Confirmed LUT format:** all 42 `colourgrades` LUTs and both `displaymappers` are 32×32×32 RGBA16F 3D cubes with a 12-byte header `(0, 32, 100.0)`. The sampled lattice encoded by `Default.lut` spans a very large 0→100 scene/HDR domain; alpha is zero.
- **Confirmed separation:** `colourgrades.zip` contains artistic/creative grade families such as `Default`, `Flat`, daylight/dawn/dusk/night film-stock variants and accessibility/event grades. `displaymappers.zip` separately contains `DefaultSDR.lut` and `DefaultHDR.lut`.
- **Display-mapper behavior:** `DefaultSDR` maps the high scene range into a 0→1 output and deliberately compresses/desaturates saturated highlights toward white. `DefaultHDR` preserves a much larger output range (observed maximum ~24.796875) and also performs chroma-dependent highlight compression rather than independent channel clipping.
- **Exposure/filmic configuration:** FH6 loose configuration exposes adaptive-exposure ranges/keys, tonemap delays and filmic-curve parameters. `TimeOfDayA.xml` exposes time-varying day/night color-grade blend amounts.
- **Best-supported working model, not yet runtime-order proof:** scene/HDR content + adaptive exposure/filmic shaping → artistic grade contribution(s) → separate SDR/HDR display mapper → final transfer/output encoding. The final runtime shader/order still must be located before this is called exact.
- **Important non-claim:** this resembles the modular HDR/WCG philosophy documented by Polyphony, but there is still no evidence that FH6 copied or directly uses Gran Turismo code.
- **P0003 caveat:** 244 collector errors all came from `Camera.zip` entries using ZIP compression method 22, unsupported by the collector runtime. Core `colourgrades`, `displaymappers`, `postEffects`, loose time-of-day/weather files and shader indexing were still recovered; camera archive decoding is deferred unless it becomes necessary.
- **Evidence / patch IDs:** `P0003`; detailed evidence belongs in `ColorCoreVI/FH6_FINDINGS.md`.
- **Next allowed action:** map GTA V Enhanced equivalents inside `common.rpf`, `update.rpf` and `update2.rpf`, then compare GTA's current pipeline against the confirmed FH6 reference structure.

## CP-0008 — GTA Enhanced already has contextual exposure/filmic/HDR branches; ColorCoreVI should preserve orchestration and replace the final mapping layer

- **Date:** 2026-09-06
- **State:** `LOCKED`
- **Decision/result:** P0004 opened the user's real `common.rpf`, `update.rpf` and `update2.rpf` with 0 errors and exposed GTA V Enhanced's current ColorCore data layer.
- **Real P0004 result:** 2,913 archive files indexed; 98 selected candidates; 98 extracted; 7,448,342 extracted bytes; no source modification; no GTA key file written.
- **Confirmed native GTA capabilities:** `visualsettings.dat` exposes separate bright/dark filmic A–F/W parameter families, a global exposure curve and adaptation system. Weather/timecycle XML drives 58 distinct `postfx_*` controls including exposure/min/max, bright/dark filmic overrides, parametric RGB correction/shift/gradients, bloom and optical post-FX. `timecycle_mods_*` can override these values contextually for missions/interiors/gameplay states.
- **Confirmed Enhanced additions:** the update layer changes `Adaptation.min.step.size` from `0.15` to `0.0001`, adds `Adaptation.hdr.*`, HDR10 dithering, HDR-aware dynamic dithering, `hdr.game.useITMBlend`, a disabled-by-default HDR game lift/gamma/gain/levels/HSV correction block, and an enabled HDR UI correction/alpha-adjustment block.
- **Important unknowns:** P0004 does not prove what `ITM` means, does not expose PQ/BT.2020/scRGB semantics, and does not reveal a FH6-style 3D display-mapper LUT or the final transfer shader.
- **Architecture decision:** do not throw away Rockstar's weather/timecycle/mission orchestration. Preserve and calibrate it. GraphicOverhaulVI should add/replace the final technical mapping layer with a separate creative-grade stage where needed plus a dedicated 3D SDR/HDR display mapper with highlight/gamut compression inspired by the measured FH6 behavior.
- **Evidence / patch IDs:** `P0004`; detailed evidence belongs in `ColorCoreVI/GTA_ENHANCED_FINDINGS.md`.
- **Next allowed action:** `P0005` must probe GTA V Enhanced's real DX12 presentation/output path (swapchain format, color space, HDR metadata, recreation changes) before the first display-mapper injection is attempted.

---

## Checkpoint template

```text
## CP-XXXX — Short name
- Date: YYYY-MM-DD
- State: LOCKED | VALIDATED | REOPENED | SUPERSEDED
- Decision/result:
- Evidence / patch IDs:
- Explicit non-goals:
- Validation gate:
- Next allowed block:
```

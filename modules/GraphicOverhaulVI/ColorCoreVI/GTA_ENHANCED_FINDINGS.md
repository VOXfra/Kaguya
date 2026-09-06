# ColorCoreVI — GTA V Enhanced evidence findings

**Evidence source:** user's local GTA V Enhanced installation (`E:\\Jeux Epic\\GTAVEnhanced`), collected read-only by P0004 on 2026-09-06.  
**Status:** confirmed configuration evidence + explicitly marked runtime hypotheses.  
**Rule:** do not claim final transfer/output behavior until the DX12 output path is observed directly.

## P0004 collection result

- archives requested: `common.rpf`, `update\\update.rpf`, `update\\update2.rpf`
- archives opened: **3 / 3**
- archive files indexed: **2,913**
- selected ColorCore candidates: **98**
- extracted files: **98**
- extracted bytes: **7,448,342**
- collector errors: **0**
- GTA key derivation: **in memory only**
- GTA key files written: **false**
- source game files modified: **false**

Archive-level result:

| Archive | State | Encryption | Entry count | File count |
|---|---|---|---:|---:|
| `common.rpf` | OPENED | NG | 705 | 665 |
| `update\\update.rpf` | OPENED | NG | 1,790 | 1,144 |
| `update\\update2.rpf` | OPENED | NG | 1,119 | 1,104 |

The selected baseline configuration came from `common.rpf` and `update.rpf`. P0004 did not find a filename-confirmed ColorCore configuration candidate in `update2.rpf`; this does **not** prove that update2 contains no shader/runtime ColorCore data.

## 1. GTA already has an explicit filmic tonemapper parameter family

Both baseline and update `visualsettings.dat` expose separate bright and dark filmic parameter sets:

- `Tonemapping.bright.filmic.A = 0.22`
- `B = 0.3`
- `C = 0.10`
- `D = 0.2`
- `E = 0.01`
- `F = 0.3`
- `W = 4.0`
- bright exposure = `-0.5`

Dark family:

- same A/B/C/D/F/W baseline
- `E = 0.0`
- dark exposure = `3.0`

The A–F/W naming and values are structurally compatible with a Hable/Uncharted-style rational filmic curve family. The exact compiled shader expression is not proven by configuration alone, so this remains a strong implementation inference rather than a disassembly-level fact.

## 2. GTA has an explicit exposure curve and adaptation system

`visualsettings.dat` exposes:

- `Exposure.curve.scale = -106.200`
- `Exposure.curve.power = 0.007`
- `Exposure.curve.offset = 104.700`

Base/common adaptation includes:

- `Adaptation.min.step.size = 0.15`
- `Adaptation.max.step.size = 3.0`
- `Adaptation.step.size.mult = 1.5`
- `Adaptation.threshold = 0.0`
- `Adaptation.sun.exposure.tweak = -2`

In `update.rpf`, Enhanced changes the existing minimum adaptation step dramatically:

- `Adaptation.min.step.size`: **0.15 → 0.0001**

and adds a dedicated HDR adaptation family:

- `Adaptation.hdr.min.step.size = 0.0001`
- `Adaptation.hdr.max.step.size = 3.0`
- `Adaptation.hdr.step.size.mult = 1.5`
- `Adaptation.hdr.threshold = 0.0`

This is direct evidence that Enhanced contains a separate HDR-aware adaptation branch/configuration rather than only reusing the old SDR values.

## 3. Enhanced adds a dedicated HDR output/configuration block

The update `visualsettings.dat` adds a large HDR-specific section that is absent from the common baseline.

Confirmed parameters include:

### HDR10 dithering

- `HDR10Dithering.enabled = 1`
- `HDR10Dithering.dynNoise = 1`
- `HDR10Dithering.range = 1023`

### Dynamic dithering

Enhanced adds separate LDR/HDR ranges, per-channel HDR ranges and dedicated 4K variants. Examples:

- `dynamicDithering.HDR.Min.R.Range = 2048`
- `dynamicDithering.HDR.Max.R.Range = 128`
- corresponding G/B ranges
- 4K HDR maximum ranges of `32`

### HDR game path

- `hdr.game.useITMBlend = 1`
- `hdr.game.cc.enabled = 0`

The game HDR color-correction block is present even though disabled by default. It exposes:

- RGB lift
- RGB gamma
- RGB gain
- RGB levels min/max/gamma
- HSV hue shift
- saturation
- contrast
- contrast pivot
- value

### HDR UI path

- `hdr.ui.cc.enabled = 1`
- `hdr.ui.alphaAdjustment.enabled = 1`

with a parallel lift/gamma/gain/levels/HSV parameter family. Default UI contrast is `1.5` with contrast pivot `0.81`.

### Important unknown

The meaning of `ITM` in `hdr.game.useITMBlend` is **not yet proven** by P0004. Do not expand it to “inverse tone mapping” or any other phrase as fact until code/shader evidence confirms the implementation.

## 4. Weather/time-of-day files already orchestrate the post-process dynamically

P0004 extracted **37 timecycle files**. Weather timecycles contain **58 distinct `postfx_*` parameter names**.

Important confirmed families include:

### Exposure

- `postfx_exposure`
- `postfx_exposure_min`
- `postfx_exposure_max`

Typical weather files store **13 values per parameter**, giving a time-of-day curve/table rather than one static value.

Common observed exposure bounds are approximately:

- minimum: `-3.5`
- maximum: `5.0`

with weather/time-specific deviations.

### Filmic tonemapping

The timecycle system can override both bright and dark filmic branches independently:

- `postfx_tonemap_filmic_override_dark`
- `postfx_tonemap_filmic_exposure_dark`
- `postfx_tonemap_filmic_a` through `f`
- `postfx_tonemap_filmic_w`
- `postfx_tonemap_filmic_override_bright`
- `postfx_tonemap_filmic_exposure_bright`
- `postfx_tonemap_filmic_a_bright` through `f_bright`
- `postfx_tonemap_filmic_w_bright`

This proves that Rockstar's weather/time system can drive the shape of the filmic response contextually.

### Parametric color shaping

The weather timecycles also expose several non-LUT color controls:

- `postfx_correct_col_r/g/b`
- `postfx_correct_cutoff`
- `postfx_shift_col_r/g/b`
- `postfx_shift_cutoff`
- `postfx_desaturation`
- bottom/middle/top gradient colors
- gradient midpoint controls

This is a contextual parametric grading system, not a modern general 3D LUT display mapper.

### Bloom and optical post-FX

- bright-pass threshold / width
- bloom intensity
- vignetting color/intensity/radius/contrast
- noise
- motion blur and other post effects

## 5. Timecycle modifiers can override the same ColorCore parameters for gameplay states

`timecycle_mods_*.xml` contains many local/situational modifiers that override exposure, bloom, filmic parameters and color shaping.

Examples include modifiers with explicit filmic A–F values and a `hud_def_colorgrade` modifier in `timecycle_mods_4.xml`.

This is important for GraphicOverhaulVI: replacing the whole Rockstar orchestration layer would risk breaking missions, interiors, gameplay states and special effects. Preserving this orchestration is preferable unless a specific part proves irreparably limited.

## 6. What P0004 did not find

No extracted data-level configuration provided direct evidence for:

- a FH6-style `32³ RGBA16F` display-mapper LUT;
- BT.2020 / Rec.2020 matrices;
- PQ / ST.2084 transfer-function constants;
- scRGB semantics;
- the exact final HDR transfer/encoding shader;
- exact placement of `hdr.game.useITMBlend` relative to filmic tonemapping and presentation.

Those responsibilities are therefore likely implemented in compiled shaders/runtime code or in resources not identifiable by the P0004 filename/data filter.

## 7. Confirmed structural comparison with FH6

### GTA V Enhanced already provides

- contextual weather/time-of-day orchestration;
- dynamic exposure controls;
- a filmic tonemapper parameter family;
- independent bright/dark shaping;
- parametric color shifts/correction/gradients;
- a dedicated Enhanced HDR adaptation branch;
- HDR10 dithering;
- a dedicated HDR game/UI color-control block.

### FH6 reference adds a capability not exposed in GTA data

- creative grading over a large 0→100 HDR-like domain using 32³ RGBA16F LUTs;
- a **separate** SDR/HDR 3D display mapper;
- luminance/chroma-dependent highlight compression and desaturation instead of independent RGB clipping.

## 8. ColorCoreVI design decision after P0004

The current best architecture is **not** to discard Rockstar's entire color stack.

Preserve and calibrate:

1. GTA weather/time-of-day state selection;
2. native exposure orchestration;
3. native contextual filmic controls where useful;
4. mission/interior/timecycle modifiers.

Add or replace at the final technical-mapping layer:

5. a dedicated GraphicOverhaulVI creative-grade stage where needed;
6. a dedicated 3D SDR/HDR display mapper inspired by the measured FH6 behavior;
7. explicit highlight/gamut compression;
8. final SDR/HDR transfer handling appropriate to GTA Enhanced's actual DX12 output path.

This avoids turning GraphicOverhaulVI into a static LUT preset and avoids throwing away Rockstar's large contextual authoring system.

## 9. Next evidence requirement — DX12 output-stage probe

Before injecting the first real ColorCoreVI mapper, P0005 must observe the final GTA V Enhanced presentation path at runtime and record at minimum:

- DXGI swapchain format;
- buffer count and dimensions;
- SDR vs HDR presentation state;
- calls to `SetColorSpace1` and their `DXGI_COLOR_SPACE_TYPE`;
- calls to `SetHDRMetaData` and HDR10 metadata when present;
- resize/recreation changes;
- enough information to decide whether the mapper should run before or after Rockstar's current HDR conversion.

Only after this output-stage checkpoint should GraphicOverhaulVI inject a real 3D display mapper.

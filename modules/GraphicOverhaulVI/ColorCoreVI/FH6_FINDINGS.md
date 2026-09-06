# ColorCoreVI — FH6 evidence findings

**Evidence source:** user's local Forza Horizon 6 installation, collected read-only by P0002/P0003 on 2026-09-06.  
**Status:** confirmed evidence + explicitly marked hypotheses.  
**Rule:** do not promote a working model to a runtime fact until the final shader/output path is located.

## P0003 collection result

- FH6 root: `C:\XboxGames\Forza Horizon 6\Content`
- native targets found: 15 / 15 requested
- archive entries indexed: 12,583
- evidence hits: 1,074
- selected files extracted: 395
- source files modified: **false**
- collector errors: 244, all tied to `media\Camera.zip` entries using ZIP compression method 22. This did not block the core color-grade/display-mapper evidence.

## 1. Native ColorCore containers

High-value native containers confirmed in the installation:

- `media\colourgrades.zip`
- `media\displaymappers.zip`
- `media\postEffects.zip`
- `media\Camera.zip`
- `media\Sky.zip`
- `media\_library\Shaders.zip`
- `media\timeofday\TimeOfDay.xml`
- `media\timeofday\TimeOfDayA.xml`
- `media\timeofday\TimeOfDayB.xml` exists in the broader P0002 inventory but was not copied by P0003 v0.1.0 and should be included in a follow-up evidence pass.
- `media\tracks\DefaultTrackSettings.xml`
- weather preset/configuration files.

The local game directory also contains `reshade-shaders`; those files are user-side/non-native evidence and are excluded from claims about FH6's engine pipeline.

## 2. FH6 3D LUT binary format

Every inspected creative/display LUT has exactly the same envelope:

- file size: `262,156` bytes
- 12-byte header:
  - `uint32 = 0`
  - `uint32 = 32`
  - `float32 = 100.0`
- payload: `32 × 32 × 32 × 4` IEEE-754 half-float values
- storage therefore matches a 32³ RGBA16F 3D cube
- alpha channel is zero in the inspected LUTs

The lattice order inferred from `Default.lut` is R-fastest, then G, then B.

### Default scene-domain lattice

`Default.lut` behaves as the identity reference over a strongly non-linear sampling grid that spans 0→100 rather than a conventional SDR 0→1 cube. The R-axis lattice values are approximately:

`0, 0.000230, 0.001085, 0.002979, 0.006493, 0.012421, 0.021866, 0.036285, 0.057678, 0.088684, 0.132935, 0.195068, 0.28125, 0.400391, 0.562988, 0.784180, 1.083008, 1.486328, 2.029297, 2.757813, 3.732422, 5.039063, 6.789063, 9.132813, 12.273438, 16.5, 22.1875, 29.859375, 40.25, 54.34375, 73.625, 100.0`.

This is direct evidence that FH6's LUT stage is designed to accept a large HDR/scene-like range; it is not merely an 8-bit/screen-space grading LUT.

## 3. Creative grades are separate from display mapping

`colourgrades.zip` contains 42 creative/accessibility/event LUTs, including:

- `Default.lut`
- `Flat.lut`
- `Forte_BaseFilmStock.lut`
- `Forte_BaseFilmStock_Corrected.lut`
- daylight, dawn, dusk, night, city-night, sunset and winter film-stock variants
- accessibility/color-vision grades
- special event/photo grades.

Some creative LUTs deliberately compress the very high 0→100 input range. Example: `Forte_BaseFilmStock.lut` levels off around ~15.29 for very high neutral inputs. That means creative look shaping is occurring while values are still far above a conventional 0→1 SDR range.

## 4. Dedicated SDR and HDR display mappers

`displaymappers.zip` contains exactly:

- `DefaultSDR.lut`
- `DefaultHDR.lut`

Both use the same 32³ RGBA16F / 0→100 input-domain format.

### DefaultSDR

Observed neutral mapping examples:

| Input lattice value | SDR neutral output |
|---:|---:|
| 0.132935 | 0.130981 |
| 0.784180 | 0.699707 |
| 1.083008 | 0.805664 |
| 2.029297 | 0.939453 |
| 3.732422 | 0.975586 |
| 5.039063 | 1.000000 |
| 100.0 | 1.000000 |

The output range is capped at `1.0`.

Crucially, saturated highlights are not simply channel-clipped. A maximum pure-red lattice point maps to white (`1,1,1`), and other saturated primaries also converge toward neutral/highlight white. The display mapper therefore performs 3D luminance/chroma-dependent highlight handling.

### DefaultHDR

Observed neutral mapping examples:

| Input lattice value | HDR neutral output |
|---:|---:|
| 0.132935 | 0.132813 |
| 0.784180 | 0.897461 |
| 1.083008 | 1.175781 |
| 2.029297 | 1.983398 |
| 3.732422 | 3.089844 |
| 5.039063 | 3.763672 |
| 9.132813 | 4.804688 |
| 16.5 | 6.425781 |
| 29.859375 | 9.367188 |
| 54.34375 | 14.75 |
| 73.625 | 19.0 |
| 100.0 | 24.796875 |

Maximum observed output is ~`24.796875`.

Again, saturated high inputs move toward white. A maximum pure-red lattice point is approximately `(16.56, 5.02, 5.02)`, not `(24.8,0,0)`. Maximum green and blue inputs exhibit corresponding cross-channel lift. This is strong evidence of explicit highlight desaturation/gamut compression.

### Explicitly unproven hypothesis

If a later runtime trace proves that this output is in scRGB units, `1.0` could correspond to the conventional scRGB reference scale and the ~24.8 ceiling could imply a ~2000-nit class target. **P0003 does not prove scRGB output or a 2000-nit target, so this must not be presented as fact.**

## 5. Exposure / filmic configuration evidence

`media\timeofday\TimeOfDay.xml` exposes parameters including:

- `tonemapDelay`
- `cameraExposureRange` (observed `-2` to `6`)
- `toneAdaptiveExposure` keys and exposure values
- a `filmicTone` parameter block with white/shoulder/linear/toe/exposure controls
- bloom/desaturation/vignette/light-ray/image controls.

`media\tracks\DefaultTrackSettings.xml` exposes another fuller set including:

- `TonemapEnabled`
- `ToneMapAdaptiveExposure`
- cockpit-specific exposure settings
- tonemap delays
- camera exposure range
- `filmicTone Version="2"` with white/shoulder/linear/toe/exposure parameters.

The parameter names are compatible with a Hable/Uncharted-style filmic family, but the exact runtime implementation and order have not yet been proved.

## 6. Time-of-day grading blend evidence

`TimeOfDayA.xml` contains time-varying curves for:

- `DayColorGradeAmount`
- `NightColorGradeAmount`
- `DayNightPostProcess`

The curves swap relative contribution over the day/night cycle. This confirms that creative grading is blended dynamically rather than being one static global look.

P0003 has not yet identified the exact runtime binding from those weights to named LUT files such as the Forte daylight/night variants.

## 7. Shader-pack evidence and limitation

`media\_library\Shaders.zip` contains more than 12k entries. P0003 indexed it but intentionally did not copy the whole ~165 MB archive.

No entry name directly exposed a decisive `tonemap`, `displaymapper`, `HDR`, `postprocess` or `exposure` final-pass shader. Some binary/string hits exist for `exposure`, `sRGB`, `PQ` or `ACES`, but their contexts are mostly material/test resources and are insufficient to claim the final output implementation.

Therefore the final shader responsible for the exact runtime order, transfer function and display encoding remains an open item.

## 8. Best-supported FH6 working model

The evidence currently supports this architecture strongly enough to use as a design reference:

1. scene/HDR lighting content;
2. adaptive exposure and filmic shaping;
3. creative HDR-domain grade contribution(s), blended by context/time of day;
4. dedicated 3D SDR or HDR display mapper with highlight/gamut/chroma handling;
5. final display transfer/encoding stage not yet located.

The exact order of steps 2–4 must still be verified at runtime/shader level.

## 9. Implication for GraphicOverhaulVI

ColorCoreVI should not be built as a ReShade LUT preset. The reference architecture argues for distinct responsibilities:

- preserve a large scene-referred range for as long as GTA V Enhanced permits;
- control exposure separately;
- keep creative grading separate from display mapping;
- implement explicit SDR and HDR output mappings;
- perform highlight/gamut compression in 3D so saturated automotive paints, signage, skies and emissives do not simply clip channels or shift hue;
- treat the final display transfer separately from artistic grading.

The next evidence requirement is to extract GTA V Enhanced's `visualsettings.dat`, timecycle/weather resources and any accessible post-processing/output controls from its RPF archives, then map where an FH6-like split can be inserted or reproduced.

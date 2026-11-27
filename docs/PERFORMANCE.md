# Motion measurements

Measured on 27 November 2026 using an interactive Windows desktop with one 1920×1080 display at 96 DPI and 144 Hz, WPF rendering tier 2 and Windows animations enabled. Each run uses fresh demo state, a 600 ms warm-up and 24 rapid transitions between compact, expanded, options and tiny poses. Native polling is excluded from the isolated transition probe.

| Rendering callback metric | v1.0 baseline | v1.1 |
| --- | ---: | ---: |
| Samples | 1,099 | 1,099 |
| Median interval | 6.94 ms | 6.94 ms |
| 95th percentile | 7.59 ms | 7.67 ms |
| 99th percentile | 15.82 ms | 15.96 ms |
| Intervals above 1.5× refresh budget | 27 | 27 |

The short runs show similar pacing, with occasional hitches. They do not establish a speed improvement. The v1.1 maximum observed interval was 29.20 ms. WPF `CompositionTarget.Rendering` callbacks measure application callback cadence, not actual DWM presentation times or input latency. Background desktop activity and hardware can affect results.

Software renders at 96, 120, 144 and 192 DPI all matched expected alpha bounds. Physical mixed-DPI monitor transitions remain unverified because only one display was available. The behavior suite separately exercises hover reversals, interrupted carousels, compact/tiny transitions, notification peeks and reduced-motion behavior.

To reproduce, build and then run this alone on an interactive desktop:

```powershell
./scripts/measure-motion.ps1
# Optional comparison with a previously built executable:
./scripts/measure-motion.ps1 -AssemblyPath C:/path/to/DeskPill.exe -Output artifacts/baseline.json
```

The report stays in ignored `artifacts/`. It contains timing and display metadata, with no private app state or desktop captures. Broader hardware testing should include physical 100%/150%/200% display transitions, battery power and application load before making general smoothness claims.

The v1.2 layout/dictation build was checked with the same isolated transition probe: 1,097 samples, median 6.94 ms, p95 7.50 ms, p99 15.99 ms and maximum 24.33 ms; 28 intervals exceeded 1.5× refresh budget. All four software DPI bounds passed. This continues to show occasional hitches rather than a general improvement claim. The dictation indicator itself was visually checked; it is not a microphone-to-screen latency benchmark.

The v1.2.1 hover adjustment adds intent delay (110 ms to open, 130 ms for options) without changing spring parameters or the 360 ms leave grace period. Deterministic physics checks compare opening, options, closing and tiny tuck at simulated 30, 60, 75, 90, 120, 144, 165, 240 and 360 Hz, plus a 50 ms frame gap. Samples at equal elapsed times remain within 3 DIPs of the 240 Hz trajectory over a 224-DIP width change. These checks verify integration behavior, not real display smoothness. The only physical display measured here remains 144 Hz; refresh rate, compositor scheduling and machine load affect visible pacing.

For v1.3 on the same 144 Hz display, Glass recorded a 6.94 ms median, 7.60 ms p95 and 15.94 ms p99 across 1,100 isolated rendering callbacks. The Ghibli reference recorded 6.95/7.64/16.00 ms across 1,096 callbacks. Each had 28 intervals above 1.5× refresh budget. Short runs show similar callback pacing, with occasional hitches; they do not measure game overhead or actual compositor presentation. All four software DPI bounds passed. Use `./scripts/measure-motion.ps1 -Theme Glass` to repeat the translucent preset measurement.

For v2.0 on the same 144 Hz display, the unchanged isolated transition probe with Ghibli Meadow recorded 1,098 callbacks: median 6.94 ms, p95 7.38 ms, p99 15.93 ms, maximum 35.26 ms, with 31 intervals above 1.5× refresh budget. All four software DPI alpha bounds passed. This remains similar pacing with occasional hitches, not a universal smoothness guarantee. The probe's alpha check expects no exterior shadow: Classic fails that particular criterion in both v1.5.1 and v2.0 because its original shadow extends beyond shell geometry. Agents output is coalesced before posting to the UI dispatcher; fixtures verify stable shell dimensions during response delivery and preserve newer drafts across switching/reopening. These checks do not measure provider latency or real microphone-to-agent timing.

For v2.1 on the same 144 Hz display, the Ghibli Meadow isolated probe recorded 1,095 callbacks: median 6.945 ms, p95 7.409 ms, p99 16.032 ms and maximum 33.101 ms; 29 intervals exceeded 1.5× refresh budget. Software bounds passed at 96/120/144/192 DPI. These remain similar callback timings with occasional hitches. The new agent composer and image-review chip were separately rendered at those scales. No physical mixed-DPI capture or provider workload was measured; no universal smoothness improvement is claimed.

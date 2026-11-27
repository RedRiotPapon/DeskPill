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

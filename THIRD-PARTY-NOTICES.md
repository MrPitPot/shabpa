# Third-party components

Shabpa's own code is under the MIT license (see `LICENSE`). It ships with these components, each under its own license:

| Component | Used for | License |
|---|---|---|
| [WinDivert 2.2.2](https://github.com/basil00/WinDivert) (`WinDivert.dll`, `WinDivert64.sys`, unmodified official release `WinDivert-2.2.2-A.zip`) | per-app download/upload limits | LGPL-3.0 or GPL-2.0, see `src/Shabpa/Native/WinDivert-LICENSE.txt` |
| [RNNoise 0.1.1](https://github.com/xiph/rnnoise) (`rnnoise.dll`, built from the release source, see `tools/rnnoise/`) | live microphone noise suppression | BSD-3-Clause, see `src/Shabpa/Native/rnnoise-LICENSE.txt` |
| [Open-Meteo](https://open-meteo.com) (web service, no code included) | weather page | CC BY 4.0 data, free for non-commercial use; attribution shown in the app |
| [LibreHardwareMonitorLib 0.9.6](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (NuGet package, unmodified) | optional advanced sensors: CPU temperature, CPU fan, any-brand graphics-card readings | MPL-2.0 |
| [Vazirmatn](https://github.com/rastikerdar/vazirmatn) by Saber Rastikerdar | Persian UI font | SIL Open Font License 1.1, see https://openfontlicense.org |
| [Microsoft.Diagnostics.Tracing.TraceEvent](https://github.com/microsoft/perfview) and its dependencies (NuGet) | per-app network statistics (ETW) | MIT |
| [.NET](https://github.com/dotnet/runtime) / WPF | runtime and UI | MIT |

WinDivert is loaded dynamically and is not modified; you may replace `WinDivert.dll` / `WinDivert64.sys` with your own build of the same version.

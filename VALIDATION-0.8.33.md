# 0.8.33 release packaging correction

0.8.32 source, local installer, integrity checks, isolated reinstall, 368 synthetic checks, official-engine loop/restart fixtures and WPF controls passed locally. The exact 0.8.32 tag was pushed without rewriting history. Its release CI failed before publication while copying a hard-coded WindowsDesktop 10.0.12 license path from a fresh NuGet cache. No 0.8.32 release assets were published by that workflow.

The build script now reads both exact included framework versions from the published runtimeconfig, validates unambiguous version metadata, and copies .NET/WPF notices from those matching runtime packages. It neither removes license checks nor changes package verification. This handles differing runtime patches selected on a clean SDK restore. Version 0.8.33 preserves the tested features in VALIDATION-0.8.32.md; only package version manifests, visible version heading and runtime-license lookup change. The failed 0.8.32 tag is retained.

The existing 0.8.33 tag workflow retains source/engine/UI/installer/public-file/owned-reinstall checks and the original GitHub destination. Local package metadata and exact-commit CI results are recorded under ignored artifacts after the source commit. No real company endpoint, new authentication, user app termination or automatic user installation is used.

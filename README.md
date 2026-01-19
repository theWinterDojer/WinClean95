# WinClean 95

## Overview
WinClean 95 is a Windows desktop cleaner focused on safe, reversible cleanup of known temp, cache, and report files. It enforces strict allowlist/denylist safety rules and defaults to Recycle Bin cleanup to preserve recoverability.

## Download
Latest release: https://github.com/theWinterDojer/WinClean95/releases/latest

## Tech Stack
- C#/.NET 8 (WPF)
- MVVM via CommunityToolkit.Mvvm
- xUnit for tests
- Windows interop for Recycle Bin operations

## Solution Layout
- `Cleaner.App`: WPF UI and view models.
- `Cleaner.Core`: core models, safety rules, and services.
- `Cleaner.Windows`: Windows-specific providers and interop.
- `Cleaner.Tests`: xUnit tests for safety and core logic.

## Build
```
dotnet build Cleaner.sln -c Release
```

## Run
```
dotnet run --project Cleaner.App -c Release
```

## Test
```
dotnet test Cleaner.sln -c Release
```

## Publish
```
dotnet publish Cleaner.App/Cleaner.App.csproj -c Release -r win-x64 -p:SelfContained=true -o ./artifacts/publish
```
Output goes to `./artifacts/publish` (adjust with `-o`).

## Safety Guarantees
- Providers add allowlist roots before scanning.
- Protected-path denylist remains enforced for system and user roots.
- Temp roots are validated (must contain Temp/Tmp segment and be unprotected).
- Default cleanup action is Recycle Bin (reversible delete).

## Platform Notes
- UI targets Windows (`net8.0-windows`).
- Use the .NET 8 SDK for building and testing.

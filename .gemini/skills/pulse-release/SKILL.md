---
name: pulse-release
description: Build, smoke test, publish single-file standalone Pulse.exe, and manage GitHub releases and CI/CD pipelines.
---

# Pulse Release & Distribution Skill

Use this skill when preparing, validating, or publishing new releases of Pulse (Windows Dynamic Island).

## Quick Publish Workflow

To produce a single-file, self-contained `Pulse.exe` for end-user distribution:

```powershell
# 1. Clean previous build artifacts
if (Test-Path "./publish") { Remove-Item "./publish" -Recurse -Force }

# 2. Build Release solution & verify unit tests
dotnet build Pulse.sln -c Release
dotnet test tests/Pulse.Tests/Pulse.Tests.csproj -c Release --no-build

# 3. Publish single-file unpackaged executable
dotnet publish src/Pulse/Pulse.csproj -c Release -r win-x64 -o ./publish

# 4. Verify binary creation
Get-Item "./publish/Pulse.exe" | Select-Object Name, Length, LastWriteTime
```

## Smoke Test Checklist

Before shipping or tagging a release:
1. Ensure no competing instance is running:
   `Stop-Process -Name Pulse -Force -ErrorAction SilentlyContinue`
2. Launch `./publish/Pulse.exe` via `Start-Process`:
   `$proc = Start-Process -FilePath (Resolve-Path "./publish/Pulse.exe").Path -PassThru`
3. Verify process responsiveness:
   `Get-Process -Id $proc.Id | Select-Object Id, ProcessName, Responding, Path`
4. Inspect `%LOCALAPPDATA%\Pulse\crash.log` for any exceptions.
5. Close the test process:
   `Stop-Process -Id $proc.Id -Force`

## GitHub Actions Release Pipeline

A GitHub Actions workflow is configured in `.github/workflows/release.yml`.

### Automated Release by Git Tag:
```powershell
git tag -a v2.1.0 -m "Release v2.1.0"
git push origin v2.1.0
```
GitHub Actions will automatically:
1. Compile the Release binaries on `windows-latest`.
2. Run all 342 unit tests.
3. Publish `Pulse.exe` (unpackaged, self-contained single-file).
4. Package `Pulse-win-x64.zip`.
5. Create a GitHub Release with attached binaries and auto-generated release notes.

### Manual Release:
Trigger the workflow manually from the **Actions** tab on GitHub via `workflow_dispatch`.

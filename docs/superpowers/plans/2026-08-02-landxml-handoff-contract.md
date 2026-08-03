# LandXML Handoff Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish the AutoGIS-Civil3D repository baseline and deliver a versioned, read-only .NET 8 validator and CLI for one-surface LandXML handoff ZIPs, backed by sanitized golden packages.

**Architecture:** `AutoGIS-Civil3D` owns a strict language-neutral ZIP contract. A pure `net8.0` library validates the container, embedded JSON Schema, manifest semantics, checksum, streaming LandXML topology, and cross-file consistency; a thin console project only maps reports to text and exit codes. Autodesk and ArcGIS dependencies remain outside this slice.

**Tech Stack:** C# 12, .NET 8 SDK, System.Text.Json, streaming System.Xml, JsonSchema.Net 9.4.0, SharpZipLib 1.4.2, xUnit 2.9.3, Microsoft.NET.Test.Sdk 18.8.1, PowerShell, Python 3, GitHub Actions on Windows.

## Global Constraints

- Treat `docs/superpowers/specs/2026-08-02-landxml-handoff-contract-design.md` as normative.
- Target `net8.0`; use C# 12, nullable reference types, implicit usings, deterministic builds, and warnings as errors.
- Install the Microsoft .NET 8 SDK before scaffolding; the machine currently has .NET runtimes but no SDK.
- Do not reference ArcGIS, AutoCAD ObjectARX, or Civil 3D assemblies in the validator, CLI, fixtures, or ordinary tests.
- Version 1 accepts exactly `handoff.json` and `surface.landxml` at the ZIP root, with exact case-sensitive names.
- Validate ZIPs read-only without extraction.
- Cap `handoff.json` at 1 MiB, `surface.landxml` at 2 GiB, and each compression ratio at 100:1.
- Support ZIP methods `Stored` and `Deflated` only; reject encrypted entries, links, directories, unknown entries, duplicates, and unsafe paths.
- Support LandXML 1.2 only and one TIN surface only.
- Manifest units are `metre`, `international_foot`, and `us_survey_foot`; vertical direction is `positive_up`.
- Unknown vertical datum yields `ValidWithWarnings` and CLI exit code 2; it is not invalid.
- Stable CLI exits are 0 valid, 1 invalid, 2 valid with warnings, and 3 invocation or operational failure.
- Passing contract validation never claims a successful Civil 3D import.
- Golden packages must be synthetic and contain no customer data, project names, usernames, workstation names, or absolute source paths.
- Keep the original diagnostic ZIP unchanged and label it superseded; preserve the fixed ZIP and source with their verified hashes.
- Do not push or claim live `AUTOGISDIAGNOSTICS` success as part of this plan.

## Dependency Notes

- JsonSchema.Net 9.4.0 targets .NET 8 and evaluates JSON Schema 2020-12. Build the schema once with `Dialect.Draft202012`; evaluate instances with `RequireFormatValidation = true`.
- SharpZipLib 1.4.2 is used only for read-only ZIP metadata and streams. Its `ZipEntry` exposes encryption and compression method directly, avoiding a custom central-directory parser.
- Keep all package versions in `Directory.Packages.props` and commit generated lock files.

## File and Responsibility Map

### Repository and provenance

- `.gitattributes` — deterministic text/binary handling.
- `.gitignore` — .NET, Visual Studio, test, and diagnostic build output exclusions.
- `README.md` — scope, quick start, safety boundary, and status.
- `docs/architecture-handoff.md` — producer/contract/validator/future-adapter dependency direction.
- `docs/adr/0001-handoff-contract-ownership.md` — ownership decision and consequences.
- `docs/diagnostics/diagnostic-kit-audit.md` — existing audit, relocated without changing evidence.
- `diagnostics/AutoGIS.Civil3D.Diagnostics/` — repaired 0.1.1 diagnostic source kit.
- `artifacts/diagnostics/original/AutoGIS.Civil3D.Diagnostics-0.1.0-build-kit.zip` — immutable original.
- `artifacts/diagnostics/current/AutoGIS.Civil3D.Diagnostics-0.1.1-build-kit.zip` — current fixed source kit.
- `artifacts/diagnostics/README.md` — versions, hashes, validation state, and external live gate.

### Build and contract

- `global.json` — .NET 8 SDK floor with feature-band roll-forward.
- `Directory.Build.props` — shared compiler, restore-lock, and warning settings.
- `Directory.Packages.props` — pinned NuGet versions.
- `AutoGIS.Civil3D.sln` — library, CLI, tests, and fixture builder.
- `contract/v1/handoff-manifest.schema.json` — normative JSON Schema 2020-12 file.

### Validator library

- `src/AutoGIS.Civil3D.Handoff/AutoGIS.Civil3D.Handoff.csproj` — pure library and embedded schema.
- `src/AutoGIS.Civil3D.Handoff/Validation/ValidationStatus.cs` — overall statuses.
- `src/AutoGIS.Civil3D.Handoff/Validation/IssueSeverity.cs` — warning/error severity.
- `src/AutoGIS.Civil3D.Handoff/Validation/ValidationIssue.cs` — stable issue record.
- `src/AutoGIS.Civil3D.Handoff/Validation/IssueCodes.cs` — public compatibility constants.
- `src/AutoGIS.Civil3D.Handoff/Validation/ValidationReport.cs` — report and verified metadata.
- `src/AutoGIS.Civil3D.Handoff/Manifest/HandoffManifest.cs` — internal typed manifest records and enums.
- `src/AutoGIS.Civil3D.Handoff/Manifest/ManifestSchemaValidator.cs` — embedded schema build/evaluation.
- `src/AutoGIS.Civil3D.Handoff/Manifest/ManifestParser.cs` — UTF-8 JSON, schema, semantic parsing, and warning creation.
- `src/AutoGIS.Civil3D.Handoff/Packaging/BundleLimits.cs` — fixed v1 size/ratio limits.
- `src/AutoGIS.Civil3D.Handoff/Packaging/BoundedReadStream.cs` — actual-byte enforcement.
- `src/AutoGIS.Civil3D.Handoff/Packaging/BundleArchive.cs` — ZIP metadata validation and entry streams.
- `src/AutoGIS.Civil3D.Handoff/LandXml/LandXmlSurfaceSummary.cs` — parsed semantic summary.
- `src/AutoGIS.Civil3D.Handoff/LandXml/ForbiddenSequenceReadStream.cs` — raw `<!DOCTYPE` rejection across chunk boundaries.
- `src/AutoGIS.Civil3D.Handoff/LandXml/LandXmlSurfaceParser.cs` — streaming XML and topology validation.
- `src/AutoGIS.Civil3D.Handoff/BundleValidator.cs` — public orchestration entry point.

### CLI

- `src/AutoGIS.Civil3D.Handoff.Cli/AutoGIS.Civil3D.Handoff.Cli.csproj` — executable referencing only the pure library.
- `src/AutoGIS.Civil3D.Handoff.Cli/CliApplication.cs` — arguments, operational-error mapping, and exit codes.
- `src/AutoGIS.Civil3D.Handoff.Cli/TextReportRenderer.cs` — deterministic human-readable report.
- `src/AutoGIS.Civil3D.Handoff.Cli/Program.cs` — process entry point.

### Tests and golden packages

- `tests/AutoGIS.Civil3D.Handoff.Tests/AutoGIS.Civil3D.Handoff.Tests.csproj` — xUnit suite.
- `tests/AutoGIS.Civil3D.Handoff.Tests/TestManifests.cs` — canonical known/unknown manifest strings and controlled mutations.
- `tests/AutoGIS.Civil3D.Handoff.Tests/TestLandXml.cs` — minimal synthetic LandXML strings and non-seekable streams.
- `tests/AutoGIS.Civil3D.Handoff.Tests/TestRepository.cs` — robust repository-root discovery for committed fixtures.
- `tests/AutoGIS.Civil3D.Handoff.Tests/TestPackageBuilder.cs` — focused temporary packages for unit tests.
- `tests/AutoGIS.Civil3D.Handoff.Tests/ContractSchemaTests.cs` — schema conformance.
- `tests/AutoGIS.Civil3D.Handoff.Tests/ManifestParserTests.cs` — semantic manifest behavior.
- `tests/AutoGIS.Civil3D.Handoff.Tests/BundleArchiveTests.cs` — ZIP safety.
- `tests/AutoGIS.Civil3D.Handoff.Tests/LandXmlSurfaceParserTests.cs` — XML and topology.
- `tests/AutoGIS.Civil3D.Handoff.Tests/BundleValidatorTests.cs` — end-to-end library reports.
- `tests/AutoGIS.Civil3D.Handoff.Tests/CliApplicationTests.cs` — rendering and exit codes.
- `tests/AutoGIS.Civil3D.Handoff.Tests/GoldenFixtureConformanceTests.cs` — committed package matrix.
- `tools/AutoGIS.Civil3D.FixtureBuilder/` — deterministic synthetic ZIP recipes and controlled malformed-header mutations.
- `fixtures/v1/valid/*.zip` — valid and warning packages.
- `fixtures/v1/invalid/*.zip` — one-primary-fault packages.
- `fixtures/v1/README.md` — fixture generation command and expected matrix.

### Automation

- `.github/workflows/ci.yml` — locked restore, Release build, tests, formatting check, and diagnostic static validation on Windows.

---

### Task 1: Establish the repository baseline and preserve diagnostics

**Files:**
- Create: `.gitattributes`
- Create: `.gitignore`
- Create: `README.md`
- Create: `docs/architecture-handoff.md`
- Create: `docs/adr/0001-handoff-contract-ownership.md`
- Move: `diagnostic-kit-audit.md` to `docs/diagnostics/diagnostic-kit-audit.md`
- Move: `diagnostic-kit-review/AutoGIS.Civil3D.Diagnostics/` to `diagnostics/AutoGIS.Civil3D.Diagnostics/`
- Move: `AutoGIS.Civil3D.Diagnostics-0.1.0-build-kit.zip` to `artifacts/diagnostics/original/AutoGIS.Civil3D.Diagnostics-0.1.0-build-kit.zip`
- Move: `AutoGIS.Civil3D.Diagnostics-0.1.1-build-kit.zip` to `artifacts/diagnostics/current/AutoGIS.Civil3D.Diagnostics-0.1.1-build-kit.zip`
- Create: `artifacts/diagnostics/README.md`

**Interfaces:**
- Consumes: untracked diagnostic artifacts already present at repository root.
- Produces: immutable artifact paths and hashes used by documentation and later live QA.

- [ ] **Step 1: Verify exact move inputs and reject linked paths**

Run this read-only PowerShell preflight from the repository root:

```powershell
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath '.').Path
$expected = @(
  'AutoGIS.Civil3D.Diagnostics-0.1.0-build-kit.zip',
  'AutoGIS.Civil3D.Diagnostics-0.1.1-build-kit.zip',
  'diagnostic-kit-audit.md',
  'diagnostic-kit-review\AutoGIS.Civil3D.Diagnostics'
)
foreach ($relative in $expected) {
  $resolved = (Resolve-Path -LiteralPath (Join-Path $repo $relative)).Path
  if (-not $resolved.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Input escaped repository: $resolved"
  }
}
$links = Get-ChildItem -LiteralPath (Join-Path $repo 'diagnostic-kit-review\AutoGIS.Civil3D.Diagnostics') -Recurse -Force |
  Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }
if ($links) { throw "Diagnostic source contains reparse points: $($links.FullName -join ', ')" }
Get-FileHash -Algorithm SHA256 -LiteralPath $expected[0], $expected[1]
```

Expected hashes:

```text
eecb977d69ff86eec34d02d881991edd5533eee77e8b854e68cbfcab69ea0af9
ce9149fff4dd8a497218cf049abab73d48922946b4d933ad6f60987d0f50ac9b
```

- [ ] **Step 2: Move the known inputs into their approved locations**

Use exact literal paths after the preflight succeeds:

```powershell
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path 'docs\diagnostics','diagnostics','artifacts\diagnostics\original','artifacts\diagnostics\current' -Force | Out-Null
Move-Item -LiteralPath 'diagnostic-kit-audit.md' -Destination 'docs\diagnostics\diagnostic-kit-audit.md'
Move-Item -LiteralPath 'diagnostic-kit-review\AutoGIS.Civil3D.Diagnostics' -Destination 'diagnostics\AutoGIS.Civil3D.Diagnostics'
Move-Item -LiteralPath 'AutoGIS.Civil3D.Diagnostics-0.1.0-build-kit.zip' -Destination 'artifacts\diagnostics\original\AutoGIS.Civil3D.Diagnostics-0.1.0-build-kit.zip'
Move-Item -LiteralPath 'AutoGIS.Civil3D.Diagnostics-0.1.1-build-kit.zip' -Destination 'artifacts\diagnostics\current\AutoGIS.Civil3D.Diagnostics-0.1.1-build-kit.zip'
```

Do not remove `diagnostic-kit-review/` unless it is empty after the move and `Get-Item` confirms it is a normal directory rather than a reparse point.

- [ ] **Step 3: Add repository hygiene and baseline documentation**

Create `.gitattributes`:

```gitattributes
* text=auto
*.cs text eol=lf
*.csproj text eol=lf
*.json text eol=lf
*.md text eol=lf
*.ps1 text eol=crlf
*.cmd text eol=crlf
*.xml text eol=lf
*.yml text eol=lf
*.zip binary
```

Create `.gitignore`:

```gitignore
.vs/
.vscode/
bin/
obj/
TestResults/
*.user
*.suo
*.dll
*.pdb
*.nupkg
*.snupkg
coverage/
```

Create `README.md` with this baseline:

```markdown
# AutoGIS-Civil3D

Contract-first handoff tooling between AutoGIS surface exports and a future Civil 3D adapter.

## Current slice

- One versioned ZIP containing `handoff.json` and one LandXML 1.2 TIN surface.
- A pure .NET 8 validator and CLI with no Autodesk or ArcGIS runtime dependency.
- Synthetic golden packages for conformance and regression testing.
- A preserved read-only Civil 3D diagnostic kit for later authorized workstation validation.

Contract validation proves package conformance only. It does not prove that Civil 3D imported the surface.

See `docs/architecture-handoff.md`, ADR-0001, and the approved design under `docs/superpowers/specs/`.
```

Create `docs/architecture-handoff.md` with:

```markdown
# AutoGIS to Civil 3D handoff architecture

AutoGIS will later produce a versioned ZIP containing exactly `handoff.json` and `surface.landxml`. The pure .NET 8 validator in this repository checks that package before any Autodesk API is involved.

Dependency direction is AutoGIS producer -> language-neutral contract -> pure validator <- future Civil 3D adapter. The pure validator never references ArcGIS, AutoCAD, or Civil 3D assemblies.

Version 1 carries one LandXML 1.2 TIN surface. DWG/DXF, multiple surfaces, producer integration, Autodesk adapter code, and live import automation are separate future slices.

Contract-valid means structurally and semantically conformant. It does not mean Civil 3D import-tested.
```

Create `docs/adr/0001-handoff-contract-ownership.md` with:

```markdown
# ADR-0001: AutoGIS-Civil3D owns the handoff contract

**Status:** Accepted

## Context

AutoGIS produces surface geometry, while Civil 3D consumption needs a stable boundary that can be tested without either desktop application.

## Decision

This repository owns the versioned schema, semantic rules, issue codes, validator, CLI, and conformance fixtures. AutoGIS will adopt the contract as a producer. Autodesk-specific code will live in a future adapter that depends on the pure validator.

## Consequences

- Contract tests run with .NET 8 only.
- Producer and consumer changes are checked against the same golden ZIPs.
- ArcGIS and Autodesk references cannot enter the core validation library.
- Live Civil 3D import remains a separate release gate.
```

Create `artifacts/diagnostics/README.md` with this table:

```markdown
| Version | Role | SHA-256 | Live Civil 3D status |
|---|---|---|---|
| 0.1.0 | Superseded original | `eecb977d69ff86eec34d02d881991edd5533eee77e8b854e68cbfcab69ea0af9` | Not run |
| 0.1.1 | Current fixed source kit | `ce9149fff4dd8a497218cf049abab73d48922946b4d933ad6f60987d0f50ac9b` | Awaiting authorized workstation |
```

Retain the audit's evidence and remove its two-space Markdown line endings so `git diff --check` is clean.

- [ ] **Step 4: Verify preservation and existing diagnostic tests**

Run:

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath `
  'artifacts\diagnostics\original\AutoGIS.Civil3D.Diagnostics-0.1.0-build-kit.zip', `
  'artifacts\diagnostics\current\AutoGIS.Civil3D.Diagnostics-0.1.1-build-kit.zip'
python diagnostics/AutoGIS.Civil3D.Diagnostics/tests/validate_package.py
python diagnostics/AutoGIS.Civil3D.Diagnostics/tests/validate_windows_scripts.py
git diff --check
```

Expected:

```text
Static validation passed: versions, all C# sources, manifest, build, staged install, and uninstall.
Windows wrapper validation passed: install, backup, fail-closed preflight, and uninstall.
```

- [ ] **Step 5: Commit the baseline**

```powershell
git add .gitattributes .gitignore README.md docs/architecture-handoff.md docs/adr/0001-handoff-contract-ownership.md docs/diagnostics/diagnostic-kit-audit.md diagnostics artifacts/diagnostics
git commit -m "chore: establish Civil 3D handoff baseline"
```

---

### Task 2: Install .NET 8 and add the normative manifest schema

**Files:**
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `AutoGIS.Civil3D.sln`
- Create: `contract/v1/handoff-manifest.schema.json`
- Create: `src/AutoGIS.Civil3D.Handoff/AutoGIS.Civil3D.Handoff.csproj`
- Create: `src/AutoGIS.Civil3D.Handoff/Validation/ValidationStatus.cs`
- Create: `src/AutoGIS.Civil3D.Handoff/Validation/IssueSeverity.cs`
- Create: `src/AutoGIS.Civil3D.Handoff/Validation/ValidationIssue.cs`
- Create: `src/AutoGIS.Civil3D.Handoff/Validation/IssueCodes.cs`
- Create: `src/AutoGIS.Civil3D.Handoff/Manifest/ManifestSchemaValidator.cs`
- Create: `tests/AutoGIS.Civil3D.Handoff.Tests/AutoGIS.Civil3D.Handoff.Tests.csproj`
- Create: `tests/AutoGIS.Civil3D.Handoff.Tests/TestManifests.cs`
- Create: `tests/AutoGIS.Civil3D.Handoff.Tests/ContractSchemaTests.cs`

**Interfaces:**
- Consumes: approved manifest shape from the design specification.
- Produces: `ManifestSchemaValidator.Validate(JsonElement) -> IReadOnlyList<ValidationIssue>` and public report primitives.

- [ ] **Step 1: Install and verify the .NET 8 SDK**

Current evidence is `dotnet --info` reporting `No SDKs were found`. Request approval for the machine-level package installation, then run:

```powershell
winget install --id Microsoft.DotNet.SDK.8 --exact --source winget --accept-package-agreements --accept-source-agreements
& "$env:ProgramFiles\dotnet\dotnet.exe" --list-sdks
```

Expected: at least one SDK line beginning with `8.0.`. Do not install Autodesk SDKs in this task.

- [ ] **Step 2: Create deterministic solution and package configuration**

Create `global.json`:

```json
{
  "sdk": {
    "version": "8.0.100",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup Condition="'$(MSBuildProjectName)' != 'AutoGIS.Civil3D.Diagnostics'">
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>12.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  </PropertyGroup>
</Project>
```

Create `Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="JsonSchema.Net" Version="9.4.0" />
    <PackageVersion Include="SharpZipLib" Version="1.4.2" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.8.1" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>
</Project>
```

Create the library project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="JsonSchema.Net" />
    <PackageReference Include="SharpZipLib" />
  </ItemGroup>
  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>AutoGIS.Civil3D.Handoff.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>
</Project>
```

Create the test project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/AutoGIS.Civil3D.Handoff/AutoGIS.Civil3D.Handoff.csproj" />
  </ItemGroup>
</Project>
```

Generate the solution and add both projects:

```powershell
dotnet new sln -n AutoGIS.Civil3D
dotnet sln AutoGIS.Civil3D.sln add src/AutoGIS.Civil3D.Handoff/AutoGIS.Civil3D.Handoff.csproj
dotnet sln AutoGIS.Civil3D.sln add tests/AutoGIS.Civil3D.Handoff.Tests/AutoGIS.Civil3D.Handoff.Tests.csproj
```

- [ ] **Step 3: Write the failing schema tests**

Write tests against the following target public API, but do not create the production types until Step 5:

```csharp
public enum ValidationStatus { Valid, ValidWithWarnings, Invalid }
public enum IssueSeverity { Warning, Error }

public sealed record ValidationIssue(
    string Code,
    IssueSeverity Severity,
    string Message,
    string? Location = null);

public static class IssueCodes
{
    public const string ManifestInvalidJson = "MAN001";
    public const string ManifestSchemaViolation = "MAN002";
    public const string ManifestSemanticViolation = "MAN003";
}
```

Add tests that parse a complete known-datum JSON instance and an instance with an extra root property:

```csharp
[Fact]
public void Valid_known_datum_manifest_satisfies_schema()
{
    using JsonDocument json = JsonDocument.Parse(TestManifests.KnownDatum);
    Assert.Empty(ManifestSchemaValidator.Validate(json.RootElement));
}

[Fact]
public void Unknown_root_property_is_rejected()
{
    using JsonDocument json = JsonDocument.Parse(
        TestManifests.KnownDatum.Replace(
            "\"contract_version\":\"1.0\",",
            "\"contract_version\":\"1.0\",\"unexpected\":true,",
            StringComparison.Ordinal));

    ValidationIssue issue = Assert.Single(ManifestSchemaValidator.Validate(json.RootElement));
    Assert.Equal(IssueCodes.ManifestSchemaViolation, issue.Code);
}
```

`TestManifests.KnownDatum` must contain the exact example values from the approved design, using a real 64-character lowercase SHA-256 string.

- [ ] **Step 4: Run the focused tests and confirm red**

Run:

```powershell
dotnet test tests/AutoGIS.Civil3D.Handoff.Tests/AutoGIS.Civil3D.Handoff.Tests.csproj --filter FullyQualifiedName~ContractSchemaTests
```

Expected: compile failure because `ManifestSchemaValidator` and the schema resource do not exist.

- [ ] **Step 5: Create the complete JSON Schema and evaluator**

Create `contract/v1/handoff-manifest.schema.json` with:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://github.com/0bnoxide/AutoGIS-Civil3D/contract/v1/handoff-manifest.schema.json",
  "type": "object",
  "additionalProperties": false,
  "required": ["contract_version", "package_id", "created_utc", "producer", "surface", "coordinate_reference"],
  "properties": {
    "contract_version": { "const": "1.0" },
    "package_id": {
      "type": "string",
      "pattern": "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-5][0-9a-fA-F]{3}-[89aAbB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}$"
    },
    "created_utc": { "type": "string", "format": "date-time" },
    "producer": {
      "type": "object",
      "additionalProperties": false,
      "required": ["name", "version"],
      "properties": {
        "name": { "$ref": "#/$defs/name100" },
        "version": { "$ref": "#/$defs/name64" },
        "source_commit": { "type": "string", "pattern": "^[0-9a-f]{7,64}$" }
      }
    },
    "surface": {
      "type": "object",
      "additionalProperties": false,
      "required": ["filename", "sha256", "landxml_version", "name", "point_count", "face_count"],
      "properties": {
        "filename": { "const": "surface.landxml" },
        "sha256": { "type": "string", "pattern": "^[0-9a-f]{64}$" },
        "landxml_version": { "const": "1.2" },
        "name": { "$ref": "#/$defs/name100" },
        "point_count": { "type": "integer", "minimum": 1 },
        "face_count": { "type": "integer", "minimum": 1 }
      }
    },
    "coordinate_reference": {
      "type": "object",
      "additionalProperties": false,
      "required": ["horizontal", "vertical"],
      "properties": {
        "horizontal": {
          "type": "object",
          "additionalProperties": false,
          "required": ["kind", "authority", "code", "unit"],
          "properties": {
            "kind": { "const": "projected" },
            "authority": { "const": "EPSG" },
            "code": { "type": "integer", "minimum": 1 },
            "unit": { "$ref": "#/$defs/unit" }
          }
        },
        "vertical": {
          "type": "object",
          "additionalProperties": false,
          "required": ["unit", "direction", "datum"],
          "properties": {
            "unit": { "$ref": "#/$defs/unit" },
            "direction": { "const": "positive_up" },
            "datum": {
              "oneOf": [
                { "$ref": "#/$defs/knownDatum" },
                { "$ref": "#/$defs/unknownDatum" }
              ]
            }
          }
        }
      }
    }
  },
  "$defs": {
    "name100": {
      "type": "string",
      "minLength": 1,
      "maxLength": 100,
      "pattern": "^[^\\u0000-\\u001f\\u007f]+$"
    },
    "name64": {
      "type": "string",
      "minLength": 1,
      "maxLength": 64,
      "pattern": "^[^\\u0000-\\u001f\\u007f]+$"
    },
    "unit": { "enum": ["metre", "international_foot", "us_survey_foot"] },
    "knownDatum": {
      "type": "object",
      "additionalProperties": false,
      "required": ["status", "authority", "code", "name"],
      "properties": {
        "status": { "const": "known" },
        "authority": { "$ref": "#/$defs/name64" },
        "code": { "type": "integer", "minimum": 1 },
        "name": { "$ref": "#/$defs/name100" }
      }
    },
    "unknownDatum": {
      "type": "object",
      "additionalProperties": false,
      "required": ["status"],
      "properties": {
        "status": { "const": "unknown" },
        "note": { "$ref": "#/$defs/name100" }
      }
    }
  }
}
```

Create the public primitive files from Step 3. Add this item group to the library project when the schema file is created:

```xml
<ItemGroup>
  <EmbeddedResource
    Include="../../contract/v1/handoff-manifest.schema.json"
    Link="Contract/handoff-manifest.schema.json"
    LogicalName="AutoGIS.Civil3D.Handoff.Contract.v1.handoff-manifest.schema.json" />
</ItemGroup>
```

Then implement the evaluator:

```csharp
internal static class ManifestSchemaValidator
{
    private const string ResourceName =
        "AutoGIS.Civil3D.Handoff.Contract.v1.handoff-manifest.schema.json";
    private static readonly Lazy<JsonSchema> Schema = new(BuildSchema);

    internal static IReadOnlyList<ValidationIssue> Validate(JsonElement instance)
    {
        EvaluationResults result = Schema.Value.Evaluate(
            instance,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = true
            });

        return result.IsValid
            ? Array.Empty<ValidationIssue>()
            : new[]
            {
                new ValidationIssue(
                    IssueCodes.ManifestSchemaViolation,
                    IssueSeverity.Error,
                    "handoff.json does not satisfy contract version 1.0.",
                    result.InstanceLocation.ToString())
            };
    }

    private static JsonSchema BuildSchema()
    {
        Assembly assembly = typeof(ManifestSchemaValidator).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded schema {ResourceName}.");
        using JsonDocument document = JsonDocument.Parse(stream);
        return JsonSchema.Build(
            document.RootElement.Clone(),
            new BuildOptions { Dialect = Dialect.Draft202012 });
    }
}
```

- [ ] **Step 6: Run schema tests, restore locks, and build**

```powershell
dotnet restore AutoGIS.Civil3D.sln
dotnet test tests/AutoGIS.Civil3D.Handoff.Tests/AutoGIS.Civil3D.Handoff.Tests.csproj --filter FullyQualifiedName~ContractSchemaTests
dotnet build AutoGIS.Civil3D.sln -c Release --no-restore
```

Expected: tests pass and all warnings are absent.

- [ ] **Step 7: Commit the toolchain and schema**

```powershell
git add global.json Directory.Build.props Directory.Packages.props AutoGIS.Civil3D.sln contract src/AutoGIS.Civil3D.Handoff tests/AutoGIS.Civil3D.Handoff.Tests
git commit -m "feat: define LandXML handoff manifest schema"
```

---

### Task 3: Parse manifest semantics and vertical-datum warnings

**Files:**
- Create: `src/AutoGIS.Civil3D.Handoff/Manifest/HandoffManifest.cs`
- Create: `src/AutoGIS.Civil3D.Handoff/Manifest/ManifestParser.cs`
- Modify: `src/AutoGIS.Civil3D.Handoff/Validation/IssueCodes.cs`
- Create: `tests/AutoGIS.Civil3D.Handoff.Tests/ManifestParserTests.cs`

**Interfaces:**
- Consumes: `ManifestSchemaValidator.Validate(JsonElement)`.
- Produces: `ManifestParser.Parse(ReadOnlyMemory<byte>) -> ManifestParseResult`, including a typed `HandoffManifest` and deterministic issues.

- [ ] **Step 1: Write failing parser tests**

Cover these exact outcomes:

```csharp
[Fact]
public void Known_datum_returns_typed_manifest_without_issues()
{
    ManifestParseResult result = ManifestParser.Parse(
        Encoding.UTF8.GetBytes(TestManifests.KnownDatum));

    Assert.NotNull(result.Manifest);
    Assert.Empty(result.Issues);
    Assert.Equal(2256, result.Manifest.CoordinateReference.Horizontal.Code);
    Assert.Equal(VerticalDatumStatus.Known, result.Manifest.CoordinateReference.Vertical.Datum.Status);
}

[Fact]
public void Unknown_datum_returns_review_warning()
{
    ManifestParseResult result = ManifestParser.Parse(
        Encoding.UTF8.GetBytes(TestManifests.UnknownDatum));

    ValidationIssue warning = Assert.Single(result.Issues);
    Assert.Equal(IssueCodes.UnknownVerticalDatum, warning.Code);
    Assert.Equal(IssueSeverity.Warning, warning.Severity);
}

[Fact]
public void Offset_timestamp_passes_schema_but_fails_normalized_utc_semantics()
{
    byte[] json = Encoding.UTF8.GetBytes(
        TestManifests.WithCreatedUtc("2026-08-02T00:00:00-06:00"));
    ManifestParseResult result = ManifestParser.Parse(json);
    Assert.Contains(result.Issues, issue => issue.Code == IssueCodes.ManifestSemanticViolation);
}

[Fact]
public void Date_without_time_fails_schema()
{
    byte[] json = Encoding.UTF8.GetBytes(
        TestManifests.WithCreatedUtc("2026-08-02"));
    ManifestParseResult result = ManifestParser.Parse(json);
    Assert.Contains(result.Issues, issue => issue.Code == IssueCodes.ManifestSchemaViolation);
}
```

Also test invalid UTF-8, malformed JSON, blank-trimmed producer/surface names, and a path-shaped producer field containing `C:\Users\name`.

- [ ] **Step 2: Run focused tests and confirm red**

```powershell
dotnet test tests/AutoGIS.Civil3D.Handoff.Tests/AutoGIS.Civil3D.Handoff.Tests.csproj --filter FullyQualifiedName~ManifestParserTests
```

Expected: compile failure because the manifest models and parser do not exist.

- [ ] **Step 3: Implement typed models and semantic validation**

Use these internal types consistently:

```csharp
internal enum LinearUnit { Metre, InternationalFoot, UsSurveyFoot }
internal enum VerticalDatumStatus { Known, Unknown }

internal sealed record HandoffManifest(
    string ContractVersion,
    Guid PackageId,
    DateTimeOffset CreatedUtc,
    ProducerManifest Producer,
    SurfaceManifest Surface,
    CoordinateReferenceManifest CoordinateReference);

internal sealed record ProducerManifest(string Name, string Version, string? SourceCommit);
internal sealed record SurfaceManifest(
    string Filename,
    string Sha256,
    string LandxmlVersion,
    string Name,
    long PointCount,
    long FaceCount);
internal sealed record CoordinateReferenceManifest(
    HorizontalReferenceManifest Horizontal,
    VerticalReferenceManifest Vertical);
internal sealed record HorizontalReferenceManifest(int Code, LinearUnit Unit);
internal sealed record VerticalReferenceManifest(
    LinearUnit Unit,
    VerticalDatumManifest Datum);
internal sealed record VerticalDatumManifest(
    VerticalDatumStatus Status,
    string? Authority,
    int? Code,
    string? Name,
    string? Note);
internal sealed record ManifestParseResult(
    HandoffManifest? Manifest,
    IReadOnlyList<ValidationIssue> Issues);
```

`ManifestParser.Parse` must parse with `JsonDocument`, stop on schema errors, read required properties explicitly, require UUID format `D`, require a normalized trailing-`Z` UTC timestamp, reject path-shaped producer fields, map unit strings exactly, and add `WRN001` for unknown datum without adding an error.

Add:

```csharp
public const string UnknownVerticalDatum = "WRN001";
```

- [ ] **Step 4: Run focused and schema tests**

```powershell
dotnet test tests/AutoGIS.Civil3D.Handoff.Tests/AutoGIS.Civil3D.Handoff.Tests.csproj --filter "FullyQualifiedName~ManifestParserTests|FullyQualifiedName~ContractSchemaTests"
```

Expected: all focused tests pass.

- [ ] **Step 5: Commit manifest parsing**

```powershell
git add src/AutoGIS.Civil3D.Handoff/Manifest src/AutoGIS.Civil3D.Handoff/Validation/IssueCodes.cs tests/AutoGIS.Civil3D.Handoff.Tests
git commit -m "feat: validate handoff manifest semantics"
```

---

### Task 4: Enforce ZIP container safety without extraction

**Files:**
- Create: `src/AutoGIS.Civil3D.Handoff/Packaging/BundleLimits.cs`
- Create: `src/AutoGIS.Civil3D.Handoff/Packaging/BoundedReadStream.cs`
- Create: `src/AutoGIS.Civil3D.Handoff/Packaging/BundleArchive.cs`
- Modify: `src/AutoGIS.Civil3D.Handoff/Validation/IssueCodes.cs`
- Create: `tests/AutoGIS.Civil3D.Handoff.Tests/TestPackageBuilder.cs`
- Create: `tests/AutoGIS.Civil3D.Handoff.Tests/BundleArchiveTests.cs`

**Interfaces:**
- Consumes: stable issue types.
- Produces: `BundleArchive.Open(string) -> BundleOpenResult`, `ReadManifestBytes()`, and `OpenSurfaceStream()`.

- [ ] **Step 1: Write failing ZIP safety tests**

`TestPackageBuilder` creates temporary ZIPs with fixed entry order and timestamp. Add tests for valid two-entry ZIP, missing surface, extra entry, unsafe path, case collision, directory/link flags, encryption, unsupported method, declared size limits, ratio limits, malformed archive, and a `BoundedReadStream` whose source exceeds its runtime limit.

```csharp
[Theory]
[InlineData(PackageFault.MissingSurface, "ZIP003")]
[InlineData(PackageFault.ExtraEntry, "ZIP004")]
[InlineData(PackageFault.UnsafePath, "ZIP005")]
[InlineData(PackageFault.CaseCollision, "ZIP006")]
[InlineData(PackageFault.EncryptedSurface, "ZIP008")]
[InlineData(PackageFault.UnsupportedCompression, "ZIP009")]
public void Invalid_container_returns_stable_primary_code(
    PackageFault fault,
    string expectedCode)
{
    string path = TestPackageBuilder.Create(fault);
    BundleOpenResult result = BundleArchive.Open(path);
    Assert.Null(result.Archive);
    Assert.Equal(expectedCode, Assert.Single(result.Issues).Code);
}
```

- [ ] **Step 2: Run focused tests and confirm red**

```powershell
dotnet test tests/AutoGIS.Civil3D.Handoff.Tests/AutoGIS.Civil3D.Handoff.Tests.csproj --filter FullyQualifiedName~BundleArchiveTests
```

Expected: compile failure because packaging types do not exist.

- [ ] **Step 3: Implement fixed limits and actual-byte enforcement**

```csharp
internal static class BundleLimits
{
    internal const long ManifestBytes = 1L * 1024 * 1024;
    internal const long SurfaceBytes = 2L * 1024 * 1024 * 1024;
    internal const double MaximumCompressionRatio = 100d;
    internal const int EntryCount = 2;
}
```

Define:

```csharp
internal sealed class BundleLimitExceededException : InvalidDataException
{
    internal BundleLimitExceededException(string entryName, long limit)
        : base($"{entryName} exceeded the {limit}-byte streaming limit.")
    {
        EntryName = entryName;
        Limit = limit;
    }

    internal string EntryName { get; }
    internal long Limit { get; }
}

internal sealed record BundleOpenResult(
    BundleArchive? Archive,
    IReadOnlyList<ValidationIssue> Issues);
```

`BoundedReadStream` wraps a readable stream, increments a `long` count in both `Read` and `ReadAsync`, and throws `BundleLimitExceededException` before returning bytes that would move the count above its limit. It delegates read/seek state to the source and rejects writes. Unit-test synchronous and asynchronous reads.

- [ ] **Step 4: Implement deterministic ZIP metadata validation**

Add:

```csharp
public const string InvalidArchive = "ZIP001";
public const string EntryCountMismatch = "ZIP002";
public const string MissingRequiredEntry = "ZIP003";
public const string UnexpectedEntry = "ZIP004";
public const string UnsafeEntryName = "ZIP005";
public const string DuplicateEntryName = "ZIP006";
public const string NonRegularEntry = "ZIP007";
public const string EncryptedEntry = "ZIP008";
public const string UnsupportedCompression = "ZIP009";
public const string ManifestTooLarge = "ZIP010";
public const string SurfaceTooLarge = "ZIP011";
public const string CompressionRatioExceeded = "ZIP012";
public const string StreamLimitExceeded = "ZIP013";
```

`BundleArchive.Open` uses `ICSharpCode.SharpZipLib.Zip.ZipFile` and validates in this exact primary-code order: archive parsing; unsafe/rooted names; case-insensitive collisions; unexpected names; missing required names; final entry count; regular-file status; encryption; compression method; nonnegative declared sizes; size limits; ratio. This makes a one-entry archive return `ZIP003`, an extra named entry return `ZIP004`, and a case collision return `ZIP006`. Return a disposable archive only when clean. Convert `ZipException` to `ZIP001`; leave filesystem and access failures as operational exceptions.

- [ ] **Step 5: Run ZIP tests and the full current suite**

```powershell
dotnet test tests/AutoGIS.Civil3D.Handoff.Tests/AutoGIS.Civil3D.Handoff.Tests.csproj --filter FullyQualifiedName~BundleArchiveTests
dotnet test AutoGIS.Civil3D.sln
```

Expected: all tests pass with no extracted files.

- [ ] **Step 6: Commit ZIP safety**

```powershell
git add src/AutoGIS.Civil3D.Handoff/Packaging src/AutoGIS.Civil3D.Handoff/Validation/IssueCodes.cs tests/AutoGIS.Civil3D.Handoff.Tests
git commit -m "feat: validate handoff ZIP safety"
```

---

### Task 5: Stream LandXML structure and TIN topology validation

**Files:**
- Create: `src/AutoGIS.Civil3D.Handoff/LandXml/LandXmlSurfaceSummary.cs`
- Create: `src/AutoGIS.Civil3D.Handoff/LandXml/ForbiddenSequenceReadStream.cs`
- Create: `src/AutoGIS.Civil3D.Handoff/LandXml/LandXmlSurfaceParser.cs`
- Modify: `src/AutoGIS.Civil3D.Handoff/Validation/IssueCodes.cs`
- Create: `tests/AutoGIS.Civil3D.Handoff.Tests/TestLandXml.cs`
- Create: `tests/AutoGIS.Civil3D.Handoff.Tests/LandXmlSurfaceParserTests.cs`

**Interfaces:**
- Consumes: a bounded surface stream.
- Produces: `LandXmlSurfaceParser.Parse(Stream) -> LandXmlParseResult`.

- [ ] **Step 1: Write failing streaming parser tests**

Use a minimal valid LandXML 1.2 document with metric units, EPSG 26913, one `Surface`, one TIN `Definition`, three points, and one face:

```csharp
[Fact]
public void Parses_one_valid_tin_surface()
{
    using Stream xml = TestLandXml.Stream(TestLandXml.Valid);
    LandXmlParseResult result = LandXmlSurfaceParser.Parse(xml);

    Assert.Empty(result.Issues);
    Assert.Equal("Existing Ground", result.Summary!.SurfaceName);
    Assert.Equal(3, result.Summary.PointCount);
    Assert.Equal(1, result.Summary.FaceCount);
    Assert.Equal(26913, result.Summary.EpsgCode);
}
```

Add one test per primary code for malformed XML, DTD token split across read chunks, wrong namespace/version, no surface, two surfaces, two definitions, malformed point, duplicate point ID, nonfinite coordinate, malformed face, missing point reference, repeated face vertex, and near-zero horizontal triangle.

- [ ] **Step 2: Run focused tests and confirm red**

```powershell
dotnet test tests/AutoGIS.Civil3D.Handoff.Tests/AutoGIS.Civil3D.Handoff.Tests.csproj --filter FullyQualifiedName~LandXmlSurfaceParserTests
```

Expected: compile failure because the parser does not exist.

- [ ] **Step 3: Add result types and issue codes**

```csharp
internal enum VerticalUnitFamily { Metre, Foot }
internal readonly record struct Point3(double Northing, double Easting, double Elevation);

internal sealed record LandXmlSurfaceSummary(
    string LandxmlVersion,
    string SurfaceName,
    long PointCount,
    long FaceCount,
    int EpsgCode,
    LinearUnit HorizontalUnit,
    VerticalUnitFamily VerticalUnitFamily);

internal sealed record LandXmlParseResult(
    LandXmlSurfaceSummary? Summary,
    IReadOnlyList<ValidationIssue> Issues);
```

Add `XML001` through `XML012` in order: malformed XML, forbidden DTD, unsupported version, invalid surface count, invalid definition count, invalid point, duplicate point ID, nonfinite coordinate, invalid face, unknown point reference, repeated face vertex, and degenerate face.

- [ ] **Step 4: Implement raw DTD rejection and secure reader settings**

`ForbiddenSequenceReadStream` scans bytes for ASCII `<!DOCTYPE` across read boundaries and throws a dedicated internal exception. Place it beneath an `XmlReader` configured as:

```csharp
XmlReaderSettings settings = new()
{
    Async = false,
    DtdProcessing = DtdProcessing.Prohibit,
    XmlResolver = null,
    IgnoreComments = true,
    IgnoreProcessingInstructions = true,
    MaxCharactersFromEntities = 0
};
```

Map the dedicated exception to `XML002`; map other `XmlException` values to `XML001`.

- [ ] **Step 5: Implement the forward-only TIN parser**

Do not load `XDocument`. Track points in `Dictionary<long, Point3>`, surface/definition/face counts as `long`, and parse point text in northing/easting/elevation order with invariant finite doubles. Require faces to contain three distinct positive integer IDs already present in the points dictionary.

Use this degeneracy rule:

```csharp
double cross =
    (b.Easting - a.Easting) * (c.Northing - a.Northing) -
    (b.Northing - a.Northing) * (c.Easting - a.Easting);
double maxSquaredEdge = Math.Max(
    SquaredDistance(a, b),
    Math.Max(SquaredDistance(b, c), SquaredDistance(c, a)));
bool degenerate = maxSquaredEdge == 0d ||
    Math.Abs(cross) <= 1e-12 * maxSquaredEdge;
```

Map LandXML `meter`, `foot`, and `USSurveyFoot` horizontal units exactly; map elevation unit `meter` or `feet` to its family. Require one positive integer `CoordinateSystem/@epsgCode`.

- [ ] **Step 6: Run parser and full tests**

```powershell
dotnet test tests/AutoGIS.Civil3D.Handoff.Tests/AutoGIS.Civil3D.Handoff.Tests.csproj --filter FullyQualifiedName~LandXmlSurfaceParserTests
dotnet test AutoGIS.Civil3D.sln
```

Expected: all tests pass; a non-seekable test stream confirms forward-only parsing.

- [ ] **Step 7: Commit LandXML parsing**

```powershell
git add src/AutoGIS.Civil3D.Handoff/LandXml src/AutoGIS.Civil3D.Handoff/Validation/IssueCodes.cs tests/AutoGIS.Civil3D.Handoff.Tests
git commit -m "feat: validate LandXML TIN topology"
```

---

### Task 6: Orchestrate checksum and manifest-to-LandXML cross-checks

**Files:**
- Create: `src/AutoGIS.Civil3D.Handoff/BundleValidator.cs`
- Create: `src/AutoGIS.Civil3D.Handoff/Validation/ValidationReport.cs`
- Modify: `src/AutoGIS.Civil3D.Handoff/Validation/IssueCodes.cs`
- Create: `tests/AutoGIS.Civil3D.Handoff.Tests/BundleValidatorTests.cs`

**Interfaces:**
- Consumes: `BundleArchive`, `ManifestParser`, and `LandXmlSurfaceParser`.
- Produces: public `BundleValidator.ValidateBundle(string) -> ValidationReport`.

- [ ] **Step 1: Write failing end-to-end library tests**

Use actual temporary ZIPs for valid known datum, valid unknown datum, bad SHA-256, and all six cross-check mismatches:

```csharp
[Fact]
public void Unknown_datum_is_valid_with_warning()
{
    string path = TestPackageBuilder.CreateValid(VerticalDatumStatus.Unknown);
    ValidationReport report = new BundleValidator().ValidateBundle(path);

    Assert.Equal(ValidationStatus.ValidWithWarnings, report.Status);
    Assert.Equal(IssueCodes.UnknownVerticalDatum, Assert.Single(report.Issues).Code);
    Assert.NotNull(report.Metadata);
}
```

- [ ] **Step 2: Run focused tests and confirm red**

```powershell
dotnet test tests/AutoGIS.Civil3D.Handoff.Tests/AutoGIS.Civil3D.Handoff.Tests.csproj --filter FullyQualifiedName~BundleValidatorTests
```

Expected: compile failure because `BundleValidator` is absent.

- [ ] **Step 3: Add integrity and cross-check codes**

```csharp
public const string ChecksumMismatch = "INT001";
public const string SurfaceNameMismatch = "XCK001";
public const string PointCountMismatch = "XCK002";
public const string FaceCountMismatch = "XCK003";
public const string EpsgMismatch = "XCK004";
public const string HorizontalUnitMismatch = "XCK005";
public const string VerticalUnitFamilyMismatch = "XCK006";
```

- [ ] **Step 4: Implement trust-boundary orchestration**

Expose:

```csharp
public sealed class BundleValidator
{
    public ValidationReport ValidateBundle(string path);
}
```

Validate container, manifest, checksum, XML, and cross-checks in that order. Hash the bounded surface stream first, encode it with `Convert.ToHexString(hash).ToLowerInvariant()`, and reopen the stream for XML only after checksum success. Map `BundleLimitExceededException` raised while reading either entry to `ZIP013`, and stop before the next layer. Stop on errors at each layer while retaining manifest warnings. Build:

```csharp
public sealed record VerifiedPackageMetadata(
    Guid PackageId,
    string SurfaceName,
    long PointCount,
    long FaceCount,
    int EpsgCode);

public sealed record ValidationReport(
    ValidationStatus Status,
    IReadOnlyList<ValidationIssue> Issues,
    VerifiedPackageMetadata? Metadata);
```

Map metre only to metre; accept either foot definition for LandXML elevation family `feet`. Do not infer a datum or resolve EPSG online.

- [ ] **Step 5: Run focused and full tests**

```powershell
dotnet test tests/AutoGIS.Civil3D.Handoff.Tests/AutoGIS.Civil3D.Handoff.Tests.csproj --filter FullyQualifiedName~BundleValidatorTests
dotnet test AutoGIS.Civil3D.sln
```

Expected: all pass, and the bad-checksum test proves XML parsing was skipped.

- [ ] **Step 6: Commit the deep validator**

```powershell
git add src/AutoGIS.Civil3D.Handoff tests/AutoGIS.Civil3D.Handoff.Tests
git commit -m "feat: validate complete LandXML handoff bundles"
```

---

### Task 7: Add the thin text CLI and stable exit codes

**Files:**
- Create: `src/AutoGIS.Civil3D.Handoff.Cli/AutoGIS.Civil3D.Handoff.Cli.csproj`
- Create: `src/AutoGIS.Civil3D.Handoff.Cli/CliApplication.cs`
- Create: `src/AutoGIS.Civil3D.Handoff.Cli/TextReportRenderer.cs`
- Create: `src/AutoGIS.Civil3D.Handoff.Cli/Program.cs`
- Modify: `AutoGIS.Civil3D.sln`
- Modify: `tests/AutoGIS.Civil3D.Handoff.Tests/AutoGIS.Civil3D.Handoff.Tests.csproj`
- Create: `tests/AutoGIS.Civil3D.Handoff.Tests/CliApplicationTests.cs`

**Interfaces:**
- Consumes: public `BundleValidator` and `ValidationReport`.
- Produces: `CliApplication.Run(string[], TextWriter, TextWriter) -> int`.

- [ ] **Step 1: Create the CLI project scaffold and test reference**

Create:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>autogis-civil3d-handoff</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../AutoGIS.Civil3D.Handoff/AutoGIS.Civil3D.Handoff.csproj" />
  </ItemGroup>
  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>AutoGIS.Civil3D.Handoff.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>
</Project>
```

Add it to `AutoGIS.Civil3D.sln`, and add a project reference from the test project to the CLI project.

Create `Program.cs` immediately as the intended one-line entry point:

```csharp
return CliApplication.Run(args, Console.Out, Console.Error);
```

- [ ] **Step 2: Write failing CLI tests**

Test no arguments, too many arguments, missing file, valid package, warning package, and invalid package:

```csharp
[Fact]
public void Warning_package_exits_two_and_requires_human_review()
{
    string path = TestPackageBuilder.CreateValid(VerticalDatumStatus.Unknown);
    StringWriter stdout = new();
    StringWriter stderr = new();

    int exitCode = CliApplication.Run(new[] { path }, stdout, stderr);

    Assert.Equal(2, exitCode);
    Assert.Contains("VALID WITH WARNINGS", stdout.ToString());
    Assert.Contains("not equivalent to Civil 3D import-tested", stdout.ToString());
    Assert.Equal(string.Empty, stderr.ToString());
}
```

- [ ] **Step 3: Run CLI tests and confirm red**

```powershell
dotnet test tests/AutoGIS.Civil3D.Handoff.Tests/AutoGIS.Civil3D.Handoff.Tests.csproj --filter FullyQualifiedName~CliApplicationTests
```

Expected: compile failure because `CliApplication` and `TextReportRenderer` are absent.

- [ ] **Step 4: Implement arguments, rendering, and exits**

```csharp
internal static class CliApplication
{
    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 1)
        {
            error.WriteLine("Usage: autogis-civil3d-handoff <bundle.zip>");
            return 3;
        }

        try
        {
            ValidationReport report = new BundleValidator().ValidateBundle(args[0]);
            TextReportRenderer.Write(report, output);
            return report.Status switch
            {
                ValidationStatus.Valid => 0,
                ValidationStatus.Invalid => 1,
                ValidationStatus.ValidWithWarnings => 2,
                _ => 3
            };
        }
        catch (Exception exception)
        {
            error.WriteLine($"Operational failure: {exception.Message}");
            return 3;
        }
    }
}
```

Render status, ordered issues, verified metadata, and always end with `Contract-valid is not equivalent to Civil 3D import-tested.` `Program.cs` returns `CliApplication.Run(args, Console.Out, Console.Error)`.

- [ ] **Step 5: Run CLI and full tests**

```powershell
dotnet test tests/AutoGIS.Civil3D.Handoff.Tests/AutoGIS.Civil3D.Handoff.Tests.csproj --filter FullyQualifiedName~CliApplicationTests
dotnet test AutoGIS.Civil3D.sln
```

Expected: all tests pass.

- [ ] **Step 6: Commit the CLI**

```powershell
git add AutoGIS.Civil3D.sln src/AutoGIS.Civil3D.Handoff.Cli tests/AutoGIS.Civil3D.Handoff.Tests
git commit -m "feat: add LandXML handoff validation CLI"
```

---

### Task 8: Build deterministic valid, ZIP, and manifest golden packages

**Files:**
- Create: `tools/AutoGIS.Civil3D.FixtureBuilder/AutoGIS.Civil3D.FixtureBuilder.csproj`
- Create: `tools/AutoGIS.Civil3D.FixtureBuilder/FixtureRecipe.cs`
- Create: `tools/AutoGIS.Civil3D.FixtureBuilder/ZipRecipeWriter.cs`
- Create: `tools/AutoGIS.Civil3D.FixtureBuilder/ZipHeaderMutator.cs`
- Create: `tools/AutoGIS.Civil3D.FixtureBuilder/Program.cs`
- Create: `fixtures/v1/valid/*.zip`
- Create: first group of `fixtures/v1/invalid/*.zip`
- Create: `tests/AutoGIS.Civil3D.Handoff.Tests/GoldenFixtureConformanceTests.cs`
- Create: `tests/AutoGIS.Civil3D.Handoff.Tests/TestRepository.cs`
- Modify: `tests/AutoGIS.Civil3D.Handoff.Tests/AutoGIS.Civil3D.Handoff.Tests.csproj`
- Modify: `AutoGIS.Civil3D.sln`

**Interfaces:**
- Consumes: v1 rules and validator codes.
- Produces: deterministic recipes and actual checked-in ZIP packages.

- [ ] **Step 1: Write the failing first-group matrix**

Create `TestRepository.Root` in the test project before adding the theory:

```csharp
internal static class TestRepository
{
    internal static string Root
    {
        get
        {
            DirectoryInfo? current = new(AppContext.BaseDirectory);
            while (current is not null)
            {
                bool hasSolution = File.Exists(Path.Combine(current.FullName, "AutoGIS.Civil3D.sln"));
                bool hasSpec = File.Exists(Path.Combine(
                    current.FullName,
                    "docs",
                    "superpowers",
                    "specs",
                    "2026-08-02-landxml-handoff-contract-design.md"));
                if (hasSolution && hasSpec) return current.FullName;
                current = current.Parent;
            }

            throw new InvalidOperationException("Could not locate the AutoGIS-Civil3D repository root.");
        }
    }
}
```

Add exact rows:

| Package | Expected status | Primary code |
|---|---|---|
| `valid/known-vertical-datum.zip` | `Valid` | none |
| `valid/unknown-vertical-datum.zip` | `ValidWithWarnings` | `WRN001` |
| `invalid/malformed-archive.zip` | `Invalid` | `ZIP001` |
| `invalid/missing-surface.zip` | `Invalid` | `ZIP003` |
| `invalid/extra-entry.zip` | `Invalid` | `ZIP004` |
| `invalid/unsafe-path.zip` | `Invalid` | `ZIP005` |
| `invalid/case-collision.zip` | `Invalid` | `ZIP006` |
| `invalid/symlink-entry.zip` | `Invalid` | `ZIP007` |
| `invalid/encrypted-entry.zip` | `Invalid` | `ZIP008` |
| `invalid/unsupported-compression.zip` | `Invalid` | `ZIP009` |
| `invalid/manifest-too-large.zip` | `Invalid` | `ZIP010` |
| `invalid/surface-too-large-declared.zip` | `Invalid` | `ZIP011` |
| `invalid/compression-ratio.zip` | `Invalid` | `ZIP012` |
| `invalid/manifest-invalid-json.zip` | `Invalid` | `MAN001` |
| `invalid/manifest-missing-field.zip` | `Invalid` | `MAN002` |
| `invalid/manifest-unknown-property.zip` | `Invalid` | `MAN002` |
| `invalid/manifest-version.zip` | `Invalid` | `MAN002` |
| `invalid/manifest-timestamp.zip` | `Invalid` | `MAN003` |
| `invalid/checksum.zip` | `Invalid` | `INT001` |

Run and expect missing-file failures.

- [ ] **Step 2: Implement deterministic recipe writing**

`FixtureCatalog.WriteAll(string outputRoot)` accepts one explicit non-root directory, rejects an empty path, filesystem roots, reparse points, and the repository root itself, and writes only catalog-named files beneath that directory. `Program` accepts exactly one output path and delegates to the catalog. This permits normal generation into `fixtures/v1` and deterministic comparison in a unique temporary directory without broad deletion.

The builder writes through a fresh child temporary directory, uses entry order manifest then surface, timestamp `2026-08-02T00:00:00`, UTF-8 without BOM, and `Stored` unless a recipe requires `Deflated`. It explicitly sets a stable host system and regular-file external attributes on both ordinary entries.

Create the fixture-builder project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SharpZipLib" />
  </ItemGroup>
  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>AutoGIS.Civil3D.Handoff.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>
</Project>
```

Add it to `AutoGIS.Civil3D.sln` and add a project reference from the test project to the fixture-builder project.

`TestRepository.Root` walks parent directories from `AppContext.BaseDirectory` until it finds both `AutoGIS.Civil3D.sln` and `docs/superpowers/specs/2026-08-02-landxml-handoff-contract-design.md`; if it reaches the filesystem root, it throws a descriptive `InvalidOperationException`.

```csharp
internal sealed record FixtureRecipe(
    string RelativePath,
    string Manifest,
    string Surface,
    CompressionMethod CompressionMethod,
    Action<byte[]>? ArchiveMutation);
```

`ZipHeaderMutator` updates both local and central records for the named entry: set flag bit 0 for encryption, method 12 for unsupported compression, and uncompressed size `0x80000001` for declared oversize. Set Unix mode `0xA1FF` for the link fixture. Build the ratio fixture from two million repeated `A` characters inside an XML comment with Deflate.

- [ ] **Step 3: Generate the first package group**

```powershell
dotnet run --project tools/AutoGIS.Civil3D.FixtureBuilder/AutoGIS.Civil3D.FixtureBuilder.csproj -- fixtures/v1
```

Expected: two valid packages and first invalid group are created.

- [ ] **Step 4: Run conformance and determinism tests**

Regenerate into a unique `%TEMP%` directory and compare SHA-256 for every generated ZIP to its committed copy:

```powershell
dotnet test tests/AutoGIS.Civil3D.Handoff.Tests/AutoGIS.Civil3D.Handoff.Tests.csproj --filter FullyQualifiedName~GoldenFixtureConformanceTests
```

Expected: every row and byte comparison passes.

- [ ] **Step 5: Commit fixture infrastructure**

```powershell
git add AutoGIS.Civil3D.sln tools fixtures/v1 tests/AutoGIS.Civil3D.Handoff.Tests
git commit -m "test: add deterministic handoff package fixtures"
```

---

### Task 9: Complete topology and cross-check golden packages

**Files:**
- Modify: `tools/AutoGIS.Civil3D.FixtureBuilder/FixtureRecipe.cs`
- Modify: `tools/AutoGIS.Civil3D.FixtureBuilder/Program.cs`
- Create: remaining `fixtures/v1/invalid/*.zip`
- Modify: `tests/AutoGIS.Civil3D.Handoff.Tests/GoldenFixtureConformanceTests.cs`
- Create: `fixtures/v1/README.md`

**Interfaces:**
- Consumes: fixture builder and released codes.
- Produces: complete minimum conformance matrix.

- [ ] **Step 1: Add failing XML and cross-check rows**

| Package | Primary code |
|---|---|
| `xml-malformed.zip` | `XML001` |
| `xml-dtd.zip` | `XML002` |
| `xml-version.zip` | `XML003` |
| `xml-no-surface.zip` | `XML004` |
| `xml-multiple-surfaces.zip` | `XML004` |
| `xml-multiple-definitions.zip` | `XML005` |
| `xml-invalid-point.zip` | `XML006` |
| `xml-duplicate-point-id.zip` | `XML007` |
| `xml-nonfinite-coordinate.zip` | `XML008` |
| `xml-invalid-face.zip` | `XML009` |
| `xml-unknown-point-reference.zip` | `XML010` |
| `xml-repeated-face-vertex.zip` | `XML011` |
| `xml-degenerate-face.zip` | `XML012` |
| `surface-name-mismatch.zip` | `XCK001` |
| `point-count-mismatch.zip` | `XCK002` |
| `face-count-mismatch.zip` | `XCK003` |
| `epsg-mismatch.zip` | `XCK004` |
| `horizontal-unit-mismatch.zip` | `XCK005` |
| `vertical-unit-family-mismatch.zip` | `XCK006` |
| `vertical-direction-invalid.zip` | `MAN002` |
| `vertical-datum-invalid.zip` | `MAN002` |

Run and expect missing-file failures.

- [ ] **Step 2: Add one-primary-fault recipes**

Each starts from the known-datum source, changes one field or XML node, recalculates SHA-256 unless checksum is the intended fault, and leaves earlier layers valid. The DTD package places `<!DOCTYPE LandXML [<!ENTITY x "unsafe">]>` before the root without referencing the entity. The nonfinite coordinate uses `NaN`; the degenerate face uses collinear projected coordinates.

- [ ] **Step 3: Generate and test the complete matrix**

```powershell
dotnet run --project tools/AutoGIS.Civil3D.FixtureBuilder/AutoGIS.Civil3D.FixtureBuilder.csproj -- fixtures/v1
dotnet test tests/AutoGIS.Civil3D.Handoff.Tests/AutoGIS.Civil3D.Handoff.Tests.csproj --filter FullyQualifiedName~GoldenFixtureConformanceTests
```

Expected: every row has its documented status and primary code; regeneration is byte-for-byte deterministic.

- [ ] **Step 4: Document safe fixture provenance**

`fixtures/v1/README.md` states that geometry is synthetic, gives the exact generator command, records fixed timestamps/order, explains header mutations, prohibits manual binary editing, and explains valid-with-warning.

- [ ] **Step 5: Run full tests and commit**

```powershell
dotnet test AutoGIS.Civil3D.sln
git add tools fixtures/v1 tests/AutoGIS.Civil3D.Handoff.Tests
git commit -m "test: complete handoff conformance matrix"
```

---

### Task 10: Add CI, finish documentation, and verify the slice

**Files:**
- Create: `.github/workflows/ci.yml`
- Modify: `README.md`
- Modify: `docs/architecture-handoff.md`
- Create: `contract/v1/README.md`
- Modify: `artifacts/diagnostics/README.md`
- Modify: project `packages.lock.json` files

**Interfaces:**
- Consumes: complete solution, CLI, fixtures, and preserved diagnostics.
- Produces: repeatable Windows CI and an evidence-backed handoff for later Civil 3D QA.

- [ ] **Step 1: Add Windows CI**

```yaml
name: ci

on:
  push:
  pull_request:

jobs:
  test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
          cache: true
          cache-dependency-path: '**/packages.lock.json'
      - name: Restore
        run: dotnet restore AutoGIS.Civil3D.sln --locked-mode
      - name: Build
        run: dotnet build AutoGIS.Civil3D.sln -c Release --no-restore
      - name: Test
        run: dotnet test AutoGIS.Civil3D.sln -c Release --no-build
      - name: Formatting
        run: dotnet format AutoGIS.Civil3D.sln --verify-no-changes --no-restore
      - name: Diagnostic package validation
        run: python diagnostics/AutoGIS.Civil3D.Diagnostics/tests/validate_package.py
      - name: Diagnostic Windows wrapper validation
        run: python diagnostics/AutoGIS.Civil3D.Diagnostics/tests/validate_windows_scripts.py
```

- [ ] **Step 2: Finish user and contract docs**

Add quick-start commands:

```powershell
dotnet restore AutoGIS.Civil3D.sln --locked-mode
dotnet build AutoGIS.Civil3D.sln -c Release --no-restore
dotnet test AutoGIS.Civil3D.sln -c Release --no-build
dotnet run --project src/AutoGIS.Civil3D.Handoff.Cli -- fixtures/v1/valid/known-vertical-datum.zip
```

Document exits 0/1/2/3 and state that code 2 requires vertical-datum review. `contract/v1/README.md` defines exact names, units, one-surface scope, limits, structural schema, semantic rules, and versioning. Architecture docs link the schema, library, CLI, fixtures, audit, and deferred live gate.

- [ ] **Step 3: Run locked full verification**

```powershell
dotnet restore AutoGIS.Civil3D.sln --locked-mode
dotnet build AutoGIS.Civil3D.sln -c Release --no-restore
dotnet test AutoGIS.Civil3D.sln -c Release --no-build
dotnet format AutoGIS.Civil3D.sln --verify-no-changes --no-restore
python diagnostics/AutoGIS.Civil3D.Diagnostics/tests/validate_package.py
python diagnostics/AutoGIS.Civil3D.Diagnostics/tests/validate_windows_scripts.py
dotnet run --project src/AutoGIS.Civil3D.Handoff.Cli -- fixtures/v1/valid/known-vertical-datum.zip
if ($LASTEXITCODE -ne 0) { throw "Known-datum fixture returned $LASTEXITCODE, expected 0." }
dotnet run --project src/AutoGIS.Civil3D.Handoff.Cli -- fixtures/v1/valid/unknown-vertical-datum.zip
if ($LASTEXITCODE -ne 2) { throw "Unknown-datum fixture returned $LASTEXITCODE, expected 2." }
dotnet run --project src/AutoGIS.Civil3D.Handoff.Cli -- fixtures/v1/invalid/checksum.zip
if ($LASTEXITCODE -ne 1) { throw "Checksum fixture returned $LASTEXITCODE, expected 1." }
git diff --check
```

Expected CLI exits: 0, 2, and 1. Record exact test counts without claiming live Civil 3D execution.

- [ ] **Step 4: Review dependencies and boundaries**

```powershell
dotnet list AutoGIS.Civil3D.sln package --vulnerable --include-transitive
rg -n "Autodesk|Autodesk\.AutoCAD|Autodesk\.Civil|ArcGIS|arcpy" src tests tools contract
if ($LASTEXITCODE -eq 0) { throw "Forbidden ArcGIS or Autodesk dependency found in the pure contract slice." }
if ($LASTEXITCODE -gt 1) { throw "Dependency-boundary search failed with exit $LASTEXITCODE." }
git status --short
```

Expected: no vulnerable packages, no Autodesk/ArcGIS imports in the new slice, and only intentional changes.

- [ ] **Step 5: Commit CI and final documentation**

```powershell
git add .github README.md docs/architecture-handoff.md contract/v1 artifacts/diagnostics/README.md src tests tools fixtures Directory.Packages.props
git commit -m "docs: complete handoff contract verification"
```

Report final test counts, commit SHA, remaining untracked files, and the still-open gate: build/load diagnostic 0.1.1 on an authorized Civil 3D 2025 workstation and run `AUTOGISDIAGNOSTICS` with sanitized evidence.

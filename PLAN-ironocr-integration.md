# PLAN: IronOCR Runtime and Debugging Support

## Problem Statement

The rewrite to C# has already happened. The remaining work is not a language migration problem; it is a runtime-support and debugging problem.

Current state:

- The repo already contains the .NET application.
- The simple .NET multi-stage Dockerfile baseline is the right target state and should be preserved.
- Azure OCR is not the source of the current instability.
- IronOCR fails in environment-specific ways across Docker and debugger-driven workflows, especially on Apple Silicon.

Observed failures point to a support-matrix mismatch rather than an application-logic defect:

1. Linux ARM64 container debugging failed with `IRONOCR-TESSERACT-DEPLOYMENT-ERROR-LINUX` and explicit messages about missing `Tesseract.linux-arm64.zip` and `libtesseract-5`.
2. The installed `IronOcr.Linux` package in this repo version contains only `linux-x64` native assets.
3. VS Code Docker debugging uses a fast-mode style workflow that bind-mounts project output into the container and runs the debug build from `/app/bin/Debug/net10.0`.
4. Forcing Linux x64 containers on Apple Silicon is possible, but under the debugger and emulation it produced different IronOCR initialization failures, so it is not a clean default workflow.

Conclusion:

The primary problem is the interaction of:

- IronOCR native packaging
- Docker host architecture
- VS Code container debug behavior

This is not primarily a Notion client, worker, or OCR-abstraction design problem.

## Verified Findings

### Repository facts

- The app is already a .NET console application with `IOcrProvider`, `AzureOcrProvider`, and `IronOcrProvider`.
- `IronOcrProvider` sets the license key eagerly and constructs `IronTesseract` directly.
- The VS Code Docker launch path uses Dockerfile-based debugging and mounts local project output into the container.

### Package facts

- `IronOcr.Linux` 2026.4.1 ships native assets only in `runtimes/linux-x64/native`.
- The installed package does not include `linux-arm64` native assets.
- NuGet package discovery shows `IronOcr`, `IronOcr.Linux`, `IronOcr.MacOs`, and `IronOcr.MacOs.ARM`, but no Linux ARM-specific runtime package.

### Vendor documentation facts

- IronSoftware publicly documents Linux support around mainstream 64-bit Ubuntu and Debian distributions and presents Docker support in that context.
- Their Linux setup guidance focuses on standard Linux distributions and required native packages, but it does not establish Linux ARM64 support for the package version in use here.
- Their troubleshooting guidance strongly suggests escalating platform-specific issues with a minimal standalone repro project when runtime behavior diverges by environment.

### Tooling facts

- Microsoft container tooling documents a fast-mode optimization for Dockerfile debugging in which project output is mounted into the container rather than relying purely on the final runtime image.
- Because of that behavior, a successful published Release image does not guarantee a successful VS Code Docker F5 experience.

## Proposed Solution

Preserve the simple .NET Dockerfile baseline and formalize runtime support by execution mode.

### Supported Mode 1: native local IronOCR development on macOS ARM

Use native host execution as the primary IronOCR development loop on Apple Silicon.

- `dotnet run` and `dotnet test` should be the default path for IronOCR-specific debugging on macOS ARM.
- macOS ARM package support remains useful for this mode.
- This avoids the vendor/package gap in Linux ARM Docker debugging.

### Supported Mode 2: Linux x64 container validation

Treat Linux x64 as the official IronOCR container target.

- Docker and Compose validation for IronOCR should run on Linux x64.
- CI and release validation should align to Linux x64 for IronOCR.
- Production container support should be defined around the platform the native package actually ships.

### Deferred or Unsupported Mode: VS Code Docker F5 for IronOCR on Apple Silicon

Do not treat Apple Silicon Docker F5 debugging as a supported IronOCR workflow unless it is proven end-to-end.

- The current package version does not provide Linux ARM64 native assets.
- The x64-emulation workaround is not yet stable enough to treat as a supported path.
- This should be documented as a tooling/runtime limitation, not misrepresented as a stable feature.

## Design Rationale

### Why this is the right level of change

- The existing .NET application design is already appropriate.
- The provider abstraction is not the root issue.
- Overcomplicating the Dockerfile or app architecture to compensate for a vendor/runtime mismatch would create fragile, low-value complexity.

### Alternatives considered

#### Option A: make Linux ARM64 Docker debugging work directly

Not recommended.

- There is no evidence that the current IronOCR package version supports Linux ARM64 natively.
- The observed `Tesseract.linux-arm64.zip` failure is consistent with missing vendor assets, not a small local misconfiguration.

#### Option B: force Linux x64 Docker debugging everywhere on Apple Silicon

Viable only as an experimental fallback.

- It may be useful for narrow validation scenarios.
- It interacts poorly with fast-mode bind mounts and emulation.
- It should not become the default development story without stable proof.

#### Option C: preserve Docker simplicity and define a support matrix

Recommended.

- It matches the observed package/runtime evidence.
- It keeps the working .NET Docker baseline clean.
- It gives the project a defensible support contract.

## Architecture Impact

### What stays the same

- The .NET application structure
- The `IOcrProvider` abstraction
- The Notion client and worker flow
- The simple multi-stage .NET Docker image approach

### What changes

- The project stops treating provider behavior as identical across all host/container/debug combinations.
- Runtime support becomes an explicit compatibility concern.
- Documentation and validation workflows become platform-aware.

### Architectural principle

Provider interchangeability remains valid at the application boundary, but runtime support is platform-specific and must be designed and documented as such.

## Data Model and Configuration

No application data model changes are required.

Existing environment variables remain the core configuration surface:

- `OCR_PROVIDER`
- `IRONOCR_LICENSE_KEY`
- existing Notion variables
- existing Azure variables

Potential documentation additions:

- supported host/container/debug matrix
- recommended IronOCR development workflow on Apple Silicon
- explicit statement that Linux x64 is the IronOCR container target

## Recommended Support Matrix

| Host | Execution Mode | Azure | IronOCR | Status |
|---|---|---|---|---|
| macOS ARM | `dotnet run` / native debug | Yes | Yes | Supported |
| macOS ARM | VS Code Docker F5 | Yes | Not yet reliable | Unsupported or experimental |
| macOS ARM | Docker Compose targeting Linux x64 | Yes | Intended validation path | Supported with explicit x64 targeting |
| Linux x64 | Docker / Compose / CI | Yes | Yes | Primary IronOCR container target |
| Linux ARM64 | Docker / Compose / CI | Likely fine for Azure | No evidence of IronOCR support | Unsupported for IronOCR |

## Implementation Steps

### Progress Snapshot (2026-04-12)

- Step 1 status: Completed
- Step 2 status: Completed
- Step 3 status: Completed (workflow split is in place)
- Step 4 status: Not started
- Step 5 status: Partial (test gate is active; platform-specific runtime gates still need explicit CI enforcement)
- Step 6 status: Pending decision (only needed if Apple Silicon Docker F5 parity is a hard requirement)

### Step 1: Re-baseline the repo narrative (Completed)

- Update planning and documentation so they reflect reality: the C# rewrite already exists.
- Remove rewrite-era framing that still talks about Python as the main implementation path.
- Preserve the simple .NET Dockerfile baseline in planning and docs.

Completed evidence:

- Root Dockerfile is now a simple .NET multi-stage build.
- README reflects the .NET runtime and provider model.
- Plan narrative is centered on runtime support, not language migration.

### Step 2: Define official IronOCR platform support (Completed)

- Declare Linux x64 as the official IronOCR container target.
- Declare native macOS ARM execution as the preferred IronOCR development path on Apple Silicon.
- Explicitly mark Apple Silicon Docker F5 for IronOCR as unsupported or experimental.

Completed evidence:

- README includes an explicit support matrix and recommended IronOCR workflows.
- VS Code tasks include both host-arch IronOCR run and explicit Linux x64 IronOCR run.

### Step 3: Separate workflows by purpose (Completed)

- Local native workflow for developer debugging of IronOCR.
- Linux x64 container workflow for integration and release validation.
- Provider-specific validation so Azure remains unaffected by IronOCR platform limitations.

Completed evidence:

- Docker Compose profiles split Azure and IronOCR execution.
- Dedicated task labels make Apple Silicon experimental flow vs Linux x64 validation flow explicit.

### Step 4: Review package-reference strategy (Next)

- Evaluate whether mixed runtime package references in the main app project are creating non-deterministic debug output.
- Prefer the smallest change that prevents platform contamination without complicating the clean Docker baseline.
- Candidate directions:
  - conditional package references
  - runtime-specific build configuration
  - limited project separation if justified

### Step 5: Add explicit validation gates (In progress)

- Native macOS ARM IronOCR validation
- Linux x64 container IronOCR validation
- Apple Silicon Docker F5 excluded from the supported matrix until validated

Current evidence:

- Automated unit tests are green (`dotnet test` passing).
- CI runs `dotnet test` before Docker image build/push.

### Step 6: Vendor escalation if Docker-debug parity is required (Pending)

- If Apple Silicon Docker F5 is a hard requirement, prepare a minimal standalone repro for IronSoftware.
- Include:
  - package versions
  - .NET version
  - host OS and Docker runtime details
  - ARM64 container failure logs
  - x64 emulation failure logs
  - exact debugger mount/run behavior

## Next Actions

1. Publish a beta image tag and validate IronOCR end-to-end on a Linux x64 server.
2. Resolve the package-reference strategy in the app project to avoid cross-platform runtime contamination during debug flows.
3. Add explicit CI or scripted gates for IronOCR Linux x64 container validation (not only unit tests).
4. Add a documented native macOS ARM IronOCR smoke-test command sequence and expected output.
5. Keep Apple Silicon Docker F5 labeled experimental until a reproducible green validation exists.
6. If F5 parity becomes mandatory, produce and share a minimal vendor repro package for escalation.

## Open Questions

- Is Apple Silicon Docker F5 support for IronOCR a hard requirement, or only a local-development convenience?
- Is Linux x64 container support sufficient for the project’s production needs?
- Should cross-platform runtime package references remain in a single app project, or be narrowed conditionally?
- Is a non-fast-mode Docker debug experiment worth pursuing, or should the project avoid that complexity?

## Validation Checklist

- [x] The plan preserves the .NET Dockerfile baseline rather than reverting to Python.
- [x] Recommendations are tied to package contents, documented tooling behavior, or observed failures.
- [x] Linux x64 is treated as the explicit IronOCR container target.
- [x] Apple Silicon Docker F5 is not represented as supported unless validated.
- [x] The plan focuses first on support-matrix clarity, not speculative architecture churn.

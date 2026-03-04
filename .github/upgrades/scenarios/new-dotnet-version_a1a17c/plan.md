# .NET 10.0 Upgrade Plan

## Table of Contents

- [Executive Summary](#executive-summary)
- [Migration Strategy](#migration-strategy)
- [Detailed Dependency Analysis](#detailed-dependency-analysis)
- [Project-by-Project Plans](#project-by-project-plans)
- [Package Update Reference](#package-update-reference)
- [Breaking Changes Catalog](#breaking-changes-catalog)
- [Risk Management](#risk-management)
- [Testing & Validation Strategy](#testing--validation-strategy)
- [Complexity & Effort Assessment](#complexity--effort-assessment)
- [Source Control Strategy](#source-control-strategy)
- [Success Criteria](#success-criteria)

---

## Executive Summary

### Scenario Overview

This plan outlines the upgrade of **LabelPlus_Next solution** from **.NET 9.0 to .NET 10.0 (LTS)**. The solution consists of 6 projects built with Avalonia UI framework, all currently targeting net9.0.

### Scope

**Projects Affected:** 6 projects
- **LabelPlus_Next.csproj** - Core class library (14,072 LOC)
- **LabelPlus_Next.Desktop.csproj** - Desktop application (86 LOC)
- **LabelPlus_Next.Tools.csproj** - Tools application (2,702 LOC)
- **LabelPlus_Next.Update.csproj** - Update utility (1,888 LOC)
- **LabelPlus_Next.ApiServer.csproj** - API server (978 LOC)
- **LabelPlus_Next.Test.csproj** - Test project (1,183 LOC)

**Total LOC:** 20,909  
**Estimated LOC to Modify:** 270+ (approximately 1.3% of codebase)

### Current State → Target State

| Component | Current | Target |
|-----------|---------|--------|
| **Framework** | .NET 9.0 | .NET 10.0 (LTS) |
| **Windows Projects** | net9.0 | net10.0-windows |
| **Class Libraries** | net9.0 | net10.0 |
| **Package Updates** | 5 packages | Upgraded to compatible versions |

### Selected Strategy

**All-At-Once Strategy** - All 6 projects upgraded simultaneously in a single atomic operation.

**Rationale:**
- ✅ Small solution (6 projects)
- ✅ Simple, clear dependency structure (depth = 1, no cycles)
- ✅ All projects currently on .NET 9.0 (homogeneous)
- ✅ All packages have compatible target framework versions
- ✅ No security vulnerabilities requiring staged mitigation
- ✅ All SDK-style projects (modern project format)
- ✅ Total codebase is manageable (21K LOC)
- ✅ Low-to-medium complexity API changes

**Approach:** Update all project files and package references simultaneously, followed by unified build, compilation error fixes, and comprehensive testing.

### Complexity Assessment

**Overall Complexity: Low-Medium**

**Discovered Metrics:**
- **Projects**: 6
- **Dependency Depth**: 1 layer
- **Circular Dependencies**: None
- **Current Framework**: net9.0 (all projects)
- **Package Updates**: 5 packages
- **Security Vulnerabilities**: 0
- **API Issues**: 270 total (10 binary incompatible, 29 source incompatible, 231 behavioral changes)

**Complexity Breakdown:**
- **Low Risk**: LabelPlus_Next.Desktop (86 LOC, no API issues)
- **Low Risk**: LabelPlus_Next.Test (1,183 LOC, no API issues)
- **Medium Risk**: LabelPlus_Next.Update (1,888 LOC, 53+ issues)
- **Medium Risk**: LabelPlus_Next.Tools (2,702 LOC, 92+ issues)
- **Medium Risk**: LabelPlus_Next.ApiServer (978 LOC, 14+ issues, identity migration)
- **Medium Risk**: LabelPlus_Next (14,072 LOC, 111+ issues, core library)

### Critical Issues

**API Compatibility Challenges:**

1. **IdentityModel Migration (ApiServer)** - 8 binary incompatible issues
   - `System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler` and related APIs moved to Microsoft.IdentityModel namespace
   - Requires migration from WIF to modern identity stack

2. **TimeSpan API Changes** - 16 source incompatible issues
   - Methods like `FromMinutes(long)`, `FromSeconds(long)` require int/double parameter adjustments

3. **DataProtection API Changes** - 9 source incompatible issues
   - `System.Security.Cryptography.ProtectedData` and `DataProtectionScope` namespace/assembly changes

4. **Behavioral Changes** - 231 instances (mostly low-impact runtime changes)
   - `System.Uri` behavior changes (102 instances)
   - `System.Text.Json.JsonDocument` changes (32 instances)
   - `System.Net.Http.HttpContent` changes (25 instances)

**Package Updates:**
- Microsoft.EntityFrameworkCore.InMemory: 9.0.8 → 10.0.3
- Microsoft.Extensions.DependencyInjection: 9.0.8 → 10.0.3
- Newtonsoft.Json: 13.0.3 → 13.0.4
- System.Management: 9.0.9 → 10.0.3
- System.Security.Cryptography.ProtectedData: 9.0.8 → 10.0.3

### Recommended Approach

**Incremental vs All-At-Once Decision: All-At-Once**

Given the small solution size, simple dependency structure, and homogeneous starting state, an all-at-once approach minimizes overall timeline and avoids multi-targeting complexity.

**Execution Flow:**
1. ✅ **Prerequisites validated** (SDK, branch setup)
2. **Atomic Upgrade** - Update all project files + packages in single operation
3. **Build & Fix** - Restore dependencies, build, fix compilation errors
4. **Test & Validate** - Run all tests, verify functionality
5. **Source Control** - Single commit for entire upgrade

### Iteration Strategy Used

**Fast Batch Approach** (chosen based on "Simple" complexity classification):
- Phase 1: Discovery & Classification (3 iterations)
- Phase 2: Foundation (3 iterations - dependency analysis, strategy, project stubs)
- Phase 3: Detail Generation (2 iterations - batch all projects, complete sections)

**Expected Remaining Iterations:** 5-6 iterations

---

## Migration Strategy

### Approach Selection: All-At-Once Strategy

**Decision:** Upgrade all 6 projects simultaneously in a single coordinated operation.

**Justification:**

✅ **Meets All-At-Once Criteria:**
- Small solution (6 projects, well below 30-project threshold)
- All projects currently on .NET 9.0 (homogeneous baseline)
- Simple dependency structure (depth = 1, no cycles)
- All packages have known compatible versions for .NET 10.0
- No security vulnerabilities requiring staged mitigation
- Manageable total codebase (21K LOC)
- All SDK-style projects (modern tooling support)

✅ **Advantages for This Solution:**
- **Fastest completion time** - Single upgrade cycle vs. multiple phases
- **No multi-targeting complexity** - Avoid maintaining multiple framework versions
- **All projects benefit simultaneously** - Entire team can use .NET 10.0 features immediately
- **Clean dependency resolution** - No mixed-framework project references
- **Simple coordination** - One atomic change, one test cycle, one deployment

⚠️ **Challenges (Mitigated):**
- **Larger testing surface** → Mitigated by comprehensive test project (LabelPlus_Next.Test)
- **Higher initial risk** → Mitigated by low complexity assessment + thorough breaking changes catalog
- **Coordinated deployment** → Not applicable (all projects in same solution, deployed together)

### All-At-Once Strategy Implementation

**Core Principle:** Atomic, simultaneous upgrade with no intermediate states.

All project file updates + all package updates + compilation fixes → **Single Task**

**Execution Sequence:**

1. **Update All Project Files** (TargetFramework changes)
   - LabelPlus_Next.csproj: `<TargetFramework>net9.0</TargetFramework>` → `net10.0`
   - LabelPlus_Next.Desktop.csproj: `net9.0` → `net10.0-windows`
   - LabelPlus_Next.Tools.csproj: `net9.0` → `net10.0-windows`
   - LabelPlus_Next.Update.csproj: `net9.0` → `net10.0-windows`
   - LabelPlus_Next.ApiServer.csproj: `net9.0` → `net10.0`
   - LabelPlus_Next.Test.csproj: `net9.0` → `net10.0`

2. **Update All Package References** (across all projects simultaneously)
   - See [Package Update Reference](#package-update-reference) for complete matrix
   - 5 packages across 4 projects

3. **Restore Dependencies** (`dotnet restore`)

4. **Build Solution and Fix All Compilation Errors**
   - Build entire solution to identify all errors
   - Fix all compilation errors based on breaking changes catalog
   - See [Breaking Changes Catalog](#breaking-changes-catalog) for detailed guidance

5. **Rebuild and Verify** (solution builds with 0 errors)

6. **Run All Tests** (LabelPlus_Next.Test project)

7. **Validate** (all tests pass, no warnings)

### Dependency-Based Ordering Rationale

**Why Order Doesn't Matter for All-At-Once:**

In the All-At-Once strategy, we update all projects simultaneously, so traditional dependency ordering (bottom-up migration) doesn't apply. However, MSBuild will automatically respect dependencies during the build phase:

- **Build Order:** MSBuild builds LabelPlus_Next.csproj first (no dependencies), then Desktop/Tools/Test (depend on core)
- **Update Order:** All project files updated at once (order irrelevant)
- **Package Order:** All packages updated at once (order irrelevant)

### Parallel vs Sequential Execution

**File Updates:** Parallel (all project files updated simultaneously)  
**Package Updates:** Parallel (all PackageReference elements updated simultaneously)  
**Build:** Sequential (MSBuild respects dependency graph automatically)  
**Compilation Fixes:** Sequential (fix errors in dependency order: core library first, then dependents)  
**Testing:** Sequential (run test project after all builds succeed)

### Risk Management for All-At-Once

**Mitigation Strategies:**

1. **Comprehensive Breaking Changes Catalog** - Pre-identified all 270 API issues
2. **Test Project Coverage** - LabelPlus_Next.Test provides validation for core library
3. **Single Commit Approach** - Easy rollback if issues arise
4. **Branch Isolation** - All changes on `upgrade-to-NET10` branch
5. **Incremental Compilation** - Fix errors in dependency order (core → dependents)

**Rollback Plan:**

If critical blocking issues arise:
```bash
git reset --hard HEAD~1  # Revert the upgrade commit
git checkout master      # Return to stable branch
```

### Timeline

**All-At-Once Timeline:**

| Phase | Description | Deliverable |
|-------|-------------|-------------|
| **Phase 0** | Prerequisites (SDK verification, branch setup) | ✅ Complete |
| **Phase 1** | Atomic Upgrade (all projects + packages + build + fix) | Solution builds with 0 errors |
| **Phase 2** | Test Validation (run all tests, address failures) | All tests pass |
| **Phase 3** | Source Control (commit, PR, merge) | Changes merged to master |

**Estimated Total Time:** Completion depends on compilation error resolution complexity and test fix requirements. No time estimates provided (relative complexity: Low-Medium).

---

## Detailed Dependency Analysis

### Dependency Graph Summary

The solution has a **simple, single-layer dependency structure** with no circular dependencies.

```
Independent Projects (no project dependencies):
├── LabelPlus_Next.Update
└── LabelPlus_Next.ApiServer

Core Library (foundation for other projects):
└── LabelPlus_Next.csproj
    ├── LabelPlus_Next.Desktop.csproj (depends on core)
    ├── LabelPlus_Next.Tools.csproj (depends on core)
    └── LabelPlus_Next.Test.csproj (depends on core)
```

**Dependency Depth:** 1 (single layer)  
**Circular Dependencies:** None  
**Total Dependencies:** 3 project references

### Project Groupings for All-At-Once Migration

Since we're using the **All-At-Once Strategy**, all projects are upgraded simultaneously in a single atomic operation. However, for clarity, here's the logical grouping:

**Group 1: Independent Projects** (no project dependencies)
- LabelPlus_Next.Update
- LabelPlus_Next.ApiServer

**Group 2: Foundation Library** (depended upon by others)
- LabelPlus_Next.csproj

**Group 3: Dependent Projects** (depend on LabelPlus_Next.csproj)
- LabelPlus_Next.Desktop.csproj
- LabelPlus_Next.Tools.csproj
- LabelPlus_Next.Test.csproj

**Migration Order for All-At-Once:**
All projects are updated simultaneously. The groupings above are for understanding dependencies only, not for staged execution.

### Critical Path Identification

**Primary Dependency Chain:**
```
LabelPlus_Next.csproj (core) → [Desktop, Tools, Test]
```

**Impact Analysis:**
- **LabelPlus_Next.csproj** is the most critical project (3 dependents)
- Breaking changes in the core library will affect Desktop, Tools, and Test projects
- Update and ApiServer are isolated and won't affect other projects

**Build Order Implications:**
When building, MSBuild will automatically respect dependencies:
1. LabelPlus_Next.csproj builds first
2. Desktop, Tools, Test build after core completes
3. Update and ApiServer can build in parallel with any other projects

### Circular Dependency Details

**Status:** ✅ No circular dependencies detected

This clean dependency structure makes the all-at-once upgrade straightforward with no risk of dependency deadlocks.

---

## Project-by-Project Plans

### Project: LabelPlus_Next.csproj

**Current State:**
- **Target Framework:** net9.0
- **Project Type:** Class Library (SDK-style)
- **Dependencies:** 0 project references, 19 NuGet packages
- **Dependants:** 3 projects (Desktop, Tools, Test)
- **Lines of Code:** 14,072
- **Files with Issues:** 15 files
- **Risk Level:** 🟡 Medium

**Target State:**
- **Target Framework:** net10.0
- **Package Updates:** 4 packages

**Migration Steps:**

#### 1. Prerequisites
- ✅ .NET 10.0 SDK installed
- ✅ Visual Studio supports .NET 10.0
- No dependent project migrations needed (all done simultaneously)

#### 2. Update Project File

**File:** `LabelPlus_Next\LabelPlus_Next.csproj`

**Change:**
```xml
<!-- Before -->
<TargetFramework>net9.0</TargetFramework>

<!-- After -->
<TargetFramework>net10.0</TargetFramework>
```

#### 3. Package Updates

| Package | Current Version | Target Version | Reason |
|---------|----------------|----------------|--------|
| Microsoft.Extensions.DependencyInjection | 9.0.8 | 10.0.3 | Framework compatibility |
| Newtonsoft.Json | 13.0.3 | 13.0.4 | Recommended update |
| System.Management | 9.0.9 | 10.0.3 | Framework compatibility |
| System.Security.Cryptography.ProtectedData | 9.0.8 | 10.0.3 | Framework compatibility + source incompatibility fix |

**No changes needed for:**
- Antelcat.I18N.Avalonia 1.1.2
- Avalonia 11.3.2
- Avalonia.Controls.DataGrid 11.3.2
- Avalonia.Controls.TreeDataGrid 11.1.1
- Avalonia.Diagnostics 11.3.2
- Avalonia.Fonts.Inter 11.3.2
- Avalonia.Themes.Fluent 11.3.2
- CommunityToolkit.Mvvm 8.4.0
- Downloader 4.0.3
- Irihi.Ursa 1.12.0
- Irihi.Ursa.Themes.Semi 1.12.0
- NLog 6.0.3
- NLog.Extensions.Logging 6.0.3
- ReactiveUI 20.4.1
- RestSharp 112.1.0
- Semi.Avalonia 11.2.1.9
- SharpCompress 0.37.0
- WebDav.Client 2.9.0

#### 4. Expected Breaking Changes

**Source Incompatible (22 issues):**

**TimeSpan API Changes (16 issues):**
- **Issue:** Methods like `TimeSpan.FromMinutes(long)`, `TimeSpan.FromSeconds(long)`, `TimeSpan.FromMilliseconds(long, long)` no longer exist
- **Fix:** Adjust parameter types to `double` or use appropriate overloads
- **Example:**
  ```csharp
  // Before
  var timeout = TimeSpan.FromMinutes(5L);

  // After
  var timeout = TimeSpan.FromMinutes(5.0);
  // OR
  var timeout = TimeSpan.FromMinutes(5);
  ```

**DataProtection API Changes (9 issues):**
- **Issue:** `System.Security.Cryptography.ProtectedData` and `DataProtectionScope` moved assemblies/namespaces
- **Fix:** Update package to 10.0.3, verify using directives
- **Files Affected:** Code using `ProtectedData.Protect/Unprotect`
- **Example:**
  ```csharp
  // Ensure using directive is correct
  using System.Security.Cryptography;

  // API usage remains the same after package update
  byte[] encrypted = ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser);
  ```

**DependencyInjection Extension (2 issues):**
- **Issue:** `OptionsConfigurationServiceCollectionExtensions.Configure<T>` binary incompatibility
- **Fix:** Update Microsoft.Extensions.DependencyInjection to 10.0.3, recompile
- **Files Affected:** Service registration code

**Path.Combine (1 issue):**
- **Issue:** `Path.Combine(ReadOnlySpan<string>)` changed
- **Fix:** Use `Path.Combine(params string[])` overload or adjust usage

**Behavioral Changes (89 issues):**

**System.Uri Changes (102 total in solution, subset in this project):**
- **Issue:** URI parsing and normalization behavior changes
- **Impact:** Low - Most changes are internal improvements
- **Validation:** Test URL construction, AbsolutePath usage, URI comparison logic
- **Files to Review:** Code using `new Uri()`, `Uri.TryCreate`, `Uri.AbsolutePath`

**System.Text.Json.JsonDocument Changes (32 total in solution):**
- **Issue:** JSON parsing behavior changes
- **Impact:** Low - Improved performance and correctness
- **Validation:** Test JSON deserialization/serialization scenarios

**System.Net.Http.HttpContent Changes:**
- **Issue:** `ReadAsStreamAsync` behavior changes
- **Impact:** Low - Stream handling improvements
- **Validation:** Test HTTP client usage, stream disposal

#### 5. Code Modifications

**High Priority (Source Incompatible):**

1. **TimeSpan API adjustments** (16 locations)
   - Search for: `TimeSpan.FromMinutes(`, `TimeSpan.FromSeconds(`, `TimeSpan.FromMilliseconds(`, `TimeSpan.FromHours(`
   - Fix: Adjust parameter types from `long` to `double` or `int`

2. **DataProtection API** (9 locations)
   - Verify package update to 10.0.3 resolves issues
   - Check using directives in affected files
   - Test encrypt/decrypt functionality

3. **DependencyInjection.Configure<T>** (2 locations)
   - Verify package update to 10.0.3 resolves issues
   - Recompile and check for errors

4. **Path.Combine** (1 location)
   - Review usage, adjust if necessary

**Medium Priority (Behavioral Changes):**

5. **URI handling** (review affected files)
   - Test URL construction scenarios
   - Validate absolute path usage
   - Check URI comparison logic

6. **JSON handling** (review affected files)
   - Test JSON serialization/deserialization
   - Validate JsonDocument parsing

7. **HTTP content** (review affected files)
   - Test stream reading from HTTP responses
   - Verify stream disposal

#### 6. Testing Strategy

**Unit Tests:**
- Run LabelPlus_Next.Test project (covers this library)
- Focus on tests related to:
  - Cryptography (DataProtection)
  - HTTP communication
  - JSON parsing
  - Time-based logic (TimeSpan usage)

**Integration Tests:**
- Test dependent projects (Desktop, Tools, Test) after core library builds
- Verify Avalonia UI initialization
- Test service dependency injection

**Manual Validation:**
- No specific manual tests required (covered by automated tests)

#### 7. Validation Checklist

- [ ] Project builds without errors
- [ ] Project builds without warnings (or warnings are documented/acceptable)
- [ ] All NuGet packages restore successfully
- [ ] All unit tests in LabelPlus_Next.Test pass
- [ ] Dependent projects (Desktop, Tools) build successfully
- [ ] No runtime errors during basic application startup
- [ ] DataProtection encrypt/decrypt works correctly
- [ ] TimeSpan calculations produce expected results
- [ ] URI handling works as expected

---

### Project: LabelPlus_Next.Desktop.csproj

**Current State:**
- **Target Framework:** net9.0
- **Project Type:** WinForms Application (SDK-style)
- **Dependencies:** 1 project (LabelPlus_Next), 3 NuGet packages
- **Dependants:** 0 projects
- **Lines of Code:** 86
- **Files with Issues:** 1 file
- **Risk Level:** 🟢 Low

**Target State:**
- **Target Framework:** net10.0-windows
- **Package Updates:** 0 packages

**Migration Steps:**

#### 1. Prerequisites
- LabelPlus_Next.csproj successfully migrated (all-at-once: done simultaneously)

#### 2. Update Project File

**File:** `LabelPlus_Next.Desktop\LabelPlus_Next.Desktop.csproj`

**Change:**
```xml
<!-- Before -->
<TargetFramework>net9.0</TargetFramework>

<!-- After -->
<TargetFramework>net10.0-windows</TargetFramework>
```

**Note:** Windows-specific TFM required for WinForms project.

#### 3. Package Updates

**No package updates required.** All packages are compatible:
- Avalonia.Desktop 11.3.2 ✅
- Avalonia.Diagnostics 11.3.2 ✅
- Downloader 4.0.3 ✅

#### 4. Expected Breaking Changes

**No API compatibility issues identified** for this project.

#### 5. Code Modifications

**No code modifications expected.** This project has:
- Minimal code (86 LOC)
- Simple Program.cs/entry point
- No identified API issues

#### 6. Testing Strategy

**Build Verification:**
- Verify project builds after LabelPlus_Next.csproj completes
- Check project reference resolution

**Manual Testing:**
- Launch desktop application
- Verify Avalonia UI loads correctly
- Test basic application functionality

#### 7. Validation Checklist

- [ ] Project builds without errors
- [ ] Project builds without warnings
- [ ] Application launches successfully
- [ ] Avalonia UI renders correctly
- [ ] No runtime exceptions during startup

---

### Project: LabelPlus_Next.Tools.csproj

**Current State:**
- **Target Framework:** net9.0
- **Project Type:** WinForms Application (SDK-style)
- **Dependencies:** 1 project (LabelPlus_Next), 14 NuGet packages
- **Dependants:** 0 projects
- **Lines of Code:** 2,702
- **Files with Issues:** 7 files
- **Risk Level:** 🟡 Medium

**Target State:**
- **Target Framework:** net10.0-windows
- **Package Updates:** 0 packages

**Migration Steps:**

#### 1. Prerequisites
- LabelPlus_Next.csproj successfully migrated (all-at-once: done simultaneously)

#### 2. Update Project File

**File:** `LabelPlus_Next.Tools\LabelPlus_Next.Tools.csproj`

**Change:**
```xml
<!-- Before -->
<TargetFramework>net9.0</TargetFramework>

<!-- After -->
<TargetFramework>net10.0-windows</TargetFramework>
```

#### 3. Package Updates

**No package updates required.** All packages are compatible:
- Avalonia 11.3.2 ✅
- Avalonia.Controls.DataGrid 11.3.2 ✅
- Avalonia.Desktop 11.3.2 ✅
- Avalonia.Diagnostics 11.3.2 ✅
- Avalonia.Fonts.Inter 11.3.2 ✅
- Avalonia.Themes.Fluent 11.3.2 ✅
- CommunityToolkit.Mvvm 8.4.0 ✅
- Downloader 4.0.3 ✅
- Irihi.Ursa 1.12.0 ✅
- Irihi.Ursa.Themes.Semi 1.12.0 ✅
- NLog 6.0.3 ✅
- NLog.Extensions.Logging 6.0.3 ✅
- RestSharp 112.1.0 ✅
- Semi.Avalonia 11.2.1.9 ✅

#### 4. Expected Breaking Changes

**Source Incompatible (3 issues):**

**TimeSpan API Changes (3 issues):**
- **Issue:** Methods like `TimeSpan.FromMilliseconds(double)`, `TimeSpan.FromSeconds(double)` parameter type changes
- **Fix:** Verify parameter types, adjust if necessary
- **Example:** Same as LabelPlus_Next.csproj

**Behavioral Changes (89 issues):**

**System.Uri Changes (subset of 102 total):**
- **Issue:** URI parsing behavior changes
- **Impact:** Low
- **Validation:** Test URL handling in tools

#### 5. Code Modifications

**High Priority:**

1. **TimeSpan API adjustments** (3 locations)
   - Search for: `TimeSpan.From*` methods
   - Fix: Verify parameter types

**Medium Priority:**

2. **URI handling** (review 7 affected files)
   - Test URL construction
   - Validate path handling

#### 6. Testing Strategy

**Build Verification:**
- Build after LabelPlus_Next.csproj completes
- Verify project reference resolution

**Manual Testing:**
- Launch tools application
- Test primary tool workflows
- Validate URI/path handling features
- Test any time-based functionality

#### 7. Validation Checklist

- [ ] Project builds without errors
- [ ] Project builds without warnings
- [ ] Application launches successfully
- [ ] Avalonia UI renders correctly
- [ ] Primary tool workflows function correctly
- [ ] No runtime exceptions

---

### Project: LabelPlus_Next.Update.csproj

**Current State:**
- **Target Framework:** net9.0
- **Project Type:** WinForms Application (SDK-style)
- **Dependencies:** 0 projects, 7 NuGet packages
- **Dependants:** 0 projects
- **Lines of Code:** 1,888
- **Files with Issues:** 3 files
- **Risk Level:** 🟢 Low-Medium

**Target State:**
- **Target Framework:** net10.0-windows
- **Package Updates:** 1 package

**Migration Steps:**

#### 1. Prerequisites
- None (independent project)

#### 2. Update Project File

**File:** `LabelPlus_Next.Update\LabelPlus_Next.Update.csproj`

**Change:**
```xml
<!-- Before -->
<TargetFramework>net9.0</TargetFramework>

<!-- After -->
<TargetFramework>net10.0-windows</TargetFramework>
```

#### 3. Package Updates

| Package | Current Version | Target Version | Reason |
|---------|----------------|----------------|--------|
| Microsoft.Extensions.DependencyInjection | 9.0.8 | 10.0.3 | Framework compatibility |

**No changes needed for:**
- Avalonia 11.3.2 ✅
- Avalonia.Desktop 11.3.2 ✅
- Avalonia.Diagnostics 11.3.2 ✅
- Avalonia.Fonts.Inter 11.3.2 ✅
- Avalonia.Themes.Fluent 11.3.2 ✅
- Downloader 4.0.3 ✅
- WebDav.Client 2.9.0 ✅

#### 4. Expected Breaking Changes

**Source Incompatible (2 issues):**

**TimeSpan API Changes (2 issues):**
- **Issue:** Parameter type changes for TimeSpan methods
- **Fix:** Adjust parameter types as needed

**Behavioral Changes (51 issues):**

**System.Uri Changes (subset of 102 total):**
- **Issue:** URI parsing behavior changes
- **Impact:** Low - Critical for update URL handling
- **Validation:** **Important** - Test update server URL construction

**System.Net.Http.HttpContent Changes:**
- **Issue:** Stream handling behavior
- **Impact:** Low-Medium - Used for downloading updates
- **Validation:** **Important** - Test update download functionality

#### 5. Code Modifications

**High Priority:**

1. **TimeSpan API adjustments** (2 locations)
   - Search for: `TimeSpan.From*` methods
   - Fix: Adjust parameter types

**Medium Priority (Critical for Update Functionality):**

2. **URI handling** (review affected files)
   - **Critical:** Test update server URL construction
   - Validate absolute path usage
   - Test URL comparison logic

3. **HTTP download functionality** (review affected files)
   - **Critical:** Test update file downloads
   - Verify stream handling
   - Test large file downloads

#### 6. Testing Strategy

**Build Verification:**
- Build independently (no project dependencies)

**Functional Testing (Critical):**
- **Update check functionality** - Verify update server connectivity
- **Update download** - Test downloading update files
- **URL construction** - Validate update server URLs are correct
- **File integrity** - Verify downloaded files are not corrupted

**Manual Testing:**
- Launch update application
- Test update check workflow
- Test update download workflow
- Validate progress reporting

#### 7. Validation Checklist

- [ ] Project builds without errors
- [ ] Project builds without warnings
- [ ] Application launches successfully
- [ ] **Update check works correctly**
- [ ] **Update download works correctly**
- [ ] **Update URLs constructed correctly**
- [ ] File download integrity verified
- [ ] No runtime exceptions

---

### Project: LabelPlus_Next.ApiServer.csproj

**Current State:**
- **Target Framework:** net9.0
- **Project Type:** ASP.NET Core Application (SDK-style)
- **Dependencies:** 0 projects, 4 NuGet packages
- **Dependants:** 0 projects
- **Lines of Code:** 978
- **Files with Issues:** 3 files
- **Risk Level:** 🟡 Medium (IdentityModel migration)

**Target State:**
- **Target Framework:** net10.0
- **Package Updates:** 1 package

**Migration Steps:**

#### 1. Prerequisites
- None (independent project)

#### 2. Update Project File

**File:** `LabelPlus_Next.ApiServer\LabelPlus_Next.ApiServer.csproj`

**Change:**
```xml
<!-- Before -->
<TargetFramework>net9.0</TargetFramework>

<!-- After -->
<TargetFramework>net10.0</TargetFramework>
```

#### 3. Package Updates

| Package | Current Version | Target Version | Reason |
|---------|----------------|----------------|--------|
| Microsoft.EntityFrameworkCore.InMemory | 9.0.8 | 10.0.3 | Framework compatibility |

**No changes needed for:**
- BCrypt.Net-Next 4.0.3 ✅
- Npgsql.EntityFrameworkCore.PostgreSQL 9.0.2 ✅
- System.IdentityModel.Tokens.Jwt 8.14.0 ✅

#### 4. Expected Breaking Changes

**Binary Incompatible (10 issues - CRITICAL):**

**IdentityModel Migration (8 issues):**

⚠️ **High-Risk Change** - JWT authentication implementation

- **Issue:** `System.IdentityModel.Tokens.Jwt` namespace APIs have binary incompatibilities
  - `JwtSecurityTokenHandler` class
  - `JwtSecurityTokenHandler()` constructor
  - `ValidateToken()` method
  - `WriteToken()` method
  - `JwtSecurityToken` class
  - `JwtSecurityToken()` constructor

- **Root Cause:** Windows Identity Foundation (WIF) APIs deprecated in favor of modern identity stack

- **Migration Path Options:**

  **Option 1 (Recommended): Continue using System.IdentityModel.Tokens.Jwt 8.14.0**
  - Package version 8.14.0 is modern and should work with .NET 10.0
  - Recompile and test - binary incompatibility warnings may resolve
  - Validate all auth flows thoroughly

  **Option 2: Migrate to Microsoft.IdentityModel.JsonWebTokens**
  - Modern replacement for JWT handling
  - Requires code changes to use new API
  - Reference: https://learn.microsoft.com/en-us/dotnet/api/microsoft.identitymodel.jsonwebtokens

- **Files Affected:** Code using `JwtSecurityTokenHandler`, `JwtSecurityToken`

- **Example (if migration needed):**
  ```csharp
  // Current (System.IdentityModel.Tokens.Jwt)
  var tokenHandler = new JwtSecurityTokenHandler();
  var token = new JwtSecurityToken(
      issuer: issuer,
      audience: audience,
      claims: claims,
      expires: expires,
      notBefore: notBefore,
      signingCredentials: credentials
  );
  var tokenString = tokenHandler.WriteToken(token);

  // Validation
  var principal = tokenHandler.ValidateToken(tokenString, validationParameters, out var validatedToken);

  // If migration to Microsoft.IdentityModel.JsonWebTokens needed:
  // (Consult docs: https://learn.microsoft.com/en-us/dotnet/api/microsoft.identitymodel.jsonwebtokens.jsonwebtokenhandler)
  ```

**DependencyInjection Extension (2 issues):**
- **Issue:** `OptionsConfigurationServiceCollectionExtensions.Configure<T>` binary incompatibility
- **Fix:** Update Microsoft.Extensions.DependencyInjection to 10.0.3 (already updated via package updates)

**Source Incompatible (2 issues):**

**TimeSpan API Changes (2 issues):**
- Same as other projects, adjust parameter types

**Behavioral Changes (2 issues):**
- Low impact runtime changes

#### 5. Code Modifications

**CRITICAL Priority (Binary Incompatible):**

1. **JWT Authentication Migration** (8 locations)
   - **Step 1:** Update to .NET 10.0 and recompile
   - **Step 2:** Test authentication flows:
     - Token generation
     - Token validation
     - Token refresh (if applicable)
   - **Step 3:** If compilation errors persist:
     - Review System.IdentityModel.Tokens.Jwt 8.14.0 compatibility
     - Consider migrating to Microsoft.IdentityModel.JsonWebTokens
   - **Step 4:** Validate all auth scenarios:
     - Login
     - Protected endpoint access
     - Token expiration handling
     - Invalid token rejection

**High Priority:**

2. **DependencyInjection.Configure<T>** (2 locations)
   - Verify package update resolves issues
   - Recompile and check for errors

3. **TimeSpan API adjustments** (2 locations)
   - Adjust parameter types

#### 6. Testing Strategy

**Build Verification:**
- Build independently (no project dependencies)
- Pay special attention to compilation errors related to IdentityModel

**Authentication Testing (CRITICAL):**
- **Login flow** - Verify users can log in and receive tokens
- **Token validation** - Verify tokens are validated correctly
- **Protected endpoints** - Verify authenticated requests work
- **Token expiration** - Verify expired tokens are rejected
- **Invalid tokens** - Verify invalid tokens are rejected
- **Token refresh** - Verify token refresh works (if implemented)

**API Testing:**
- Test all API endpoints
- Verify Entity Framework InMemory database works
- Test PostgreSQL database connection (if used in dev/prod)

**Integration Testing:**
- Test with client applications (if available)

#### 7. Validation Checklist

- [ ] Project builds without errors
- [ ] Project builds without warnings (especially IdentityModel warnings)
- [ ] API server starts successfully
- [ ] **JWT token generation works**
- [ ] **JWT token validation works**
- [ ] **Login flow works end-to-end**
- [ ] **Protected endpoints require valid tokens**
- [ ] **Expired/invalid tokens are rejected**
- [ ] Entity Framework InMemory database works
- [ ] All API endpoints respond correctly
- [ ] No runtime exceptions

---

### Project: LabelPlus_Next.Test.csproj

**Current State:**
- **Target Framework:** net9.0
- **Project Type:** Test Project (SDK-style, MSTest)
- **Dependencies:** 1 project (LabelPlus_Next), 7 NuGet packages
- **Dependants:** 0 projects
- **Lines of Code:** 1,183
- **Files with Issues:** 1 file
- **Risk Level:** 🟢 Low

**Target State:**
- **Target Framework:** net10.0
- **Package Updates:** 1 package

**Migration Steps:**

#### 1. Prerequisites
- LabelPlus_Next.csproj successfully migrated (all-at-once: done simultaneously)

#### 2. Update Project File

**File:** `LabelPlus_Next.Test\LabelPlus_Next.Test.csproj`

**Change:**
```xml
<!-- Before -->
<TargetFramework>net9.0</TargetFramework>

<!-- After -->
<TargetFramework>net10.0</TargetFramework>
```

#### 3. Package Updates

| Package | Current Version | Target Version | Reason |
|---------|----------------|----------------|--------|
| Newtonsoft.Json | 13.0.3 | 13.0.4 | Recommended update |

**No changes needed for:**
- Microsoft.NET.Test.Sdk 17.11.1 ✅
- MSTest.Analyzers 3.6.4 ✅
- MSTest.TestAdapter 3.6.4 ✅
- MSTest.TestFramework 3.6.4 ✅
- Downloader 4.0.3 ✅
- RestSharp 112.1.0 ✅

#### 4. Expected Breaking Changes

**No API compatibility issues identified** for this project.

#### 5. Code Modifications

**No code modifications expected.** This is a test project with:
- No identified API issues
- Compatible test framework packages
- Standard MSTest test structure

#### 6. Testing Strategy

**Build Verification:**
- Build after LabelPlus_Next.csproj completes
- Verify project reference resolution
- Verify test framework packages restore correctly

**Test Execution:**
- **Run all tests** in this project
- Verify all tests pass
- Address any test failures:
  - Categorize by type (infrastructure vs. logic failures)
  - Update test expectations if behavioral changes affect results
  - Fix broken tests incrementally

**Test Coverage:**
- Verify tests cover critical LabelPlus_Next.csproj functionality
- Ensure tests validate TimeSpan, DataProtection, URI, JSON handling

#### 7. Validation Checklist

- [ ] Project builds without errors
- [ ] Project builds without warnings
- [ ] All NuGet packages restore successfully
- [ ] Test discovery works (all tests visible in Test Explorer)
- [ ] **All tests pass**
- [ ] No test infrastructure failures
- [ ] Test execution time is reasonable
- [ ] No runtime exceptions during test execution

---

## Package Update Reference

[To be filled]

---

## Breaking Changes Catalog

[To be filled]

---

## Risk Management

### High-Risk Changes

| Project | Risk Level | Description | Mitigation |
|---------|-----------|-------------|------------|
| **LabelPlus_Next.ApiServer** | 🟡 Medium | 10 binary incompatible API issues (IdentityModel migration), JWT token handling changes | Pre-identify all IdentityModel usages, follow Microsoft.IdentityModel.* migration guide, validate auth flows thoroughly |
| **LabelPlus_Next** | 🟡 Medium | 22 source incompatible APIs, 111+ total issues, core library affects 3 dependents | Fix core library compilation errors first before dependent projects, comprehensive testing |
| **LabelPlus_Next.Tools** | 🟡 Medium | 92+ API issues (3 source incompatible, 89 behavioral changes) | Focus on TimeSpan API adjustments, test URI handling changes |
| **LabelPlus_Next.Update** | 🟢 Low-Medium | 53+ API issues (2 source incompatible, 51 behavioral changes) | Test update mechanisms thoroughly, validate cryptography changes |
| **LabelPlus_Next.Desktop** | 🟢 Low | Minimal code (86 LOC), no API issues | Low risk, straightforward framework update |
| **LabelPlus_Next.Test** | 🟢 Low | Test project, no API issues, 1 package update | Low risk, update test framework package |

### Security Vulnerabilities

**Status:** ✅ No security vulnerabilities identified in current packages

All packages are on recent versions with no known CVEs requiring immediate remediation.

### Contingency Plans

#### Blocking Issue: IdentityModel Migration Fails (ApiServer)

**Scenario:** JWT authentication breaks after IdentityModel namespace migration

**Alternatives:**
1. **Keep System.IdentityModel.Tokens.Jwt package** - Verify if package works with .NET 10.0 despite binary incompatibility warnings
2. **Implement custom JWT handling** - Use `System.IdentityModel.Tokens.Jwt` with explicit assembly binding
3. **Migrate to Microsoft.IdentityModel.JsonWebTokens** - Modern replacement for JWT handling
4. **Rollback auth changes** - Isolate auth code, complete other upgrades first, address auth separately

**Recommended Path:** Migrate to `Microsoft.IdentityModel.JsonWebTokens` (modern stack)

#### Performance Degradation After Upgrade

**Scenario:** Behavioral changes in `System.Uri` or `System.Text.Json` cause performance issues

**Detection:**
- Monitor application startup time
- Compare API response times before/after
- Run performance benchmarks (if available)

**Mitigation:**
1. Profile application to identify bottlenecks
2. Review behavioral change documentation for affected APIs
3. Implement targeted optimizations
4. Consider alternative implementations if performance-critical

#### Breaking Changes Cause Widespread Compilation Errors

**Scenario:** More compilation errors than expected (beyond 270 identified issues)

**Approach:**
1. **Group errors by type** - Organize by namespace, API, or error message
2. **Fix by dependency order** - Core library first, then dependents
3. **Leverage IDE suggestions** - Use Visual Studio Quick Fixes where applicable
4. **Consult breaking changes docs** - https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0
5. **Incremental compilation** - Fix errors in batches, recompile frequently

#### Test Failures After Upgrade

**Scenario:** LabelPlus_Next.Test fails after framework upgrade

**Response:**
1. **Categorize failures** - Separate infrastructure failures from logic failures
2. **Update test framework** - Ensure MSTest packages are compatible
3. **Review behavioral changes** - Check if test expectations need updating
4. **Fix tests incrementally** - Address by test class/module
5. **Add new tests** - Cover any new edge cases discovered

### Risk Mitigation Summary

**Overall Risk Level:** 🟡 Low-Medium

**Key Mitigations:**
1. ✅ **Pre-identified all 270 API issues** - Comprehensive breaking changes catalog
2. ✅ **All-At-Once strategy reduces multi-targeting complexity**
3. ✅ **Branch isolation** - All changes on `upgrade-to-NET10` branch
4. ✅ **Single commit approach** - Easy rollback if needed
5. ✅ **Test coverage** - LabelPlus_Next.Test validates core library
6. ✅ **No security vulnerabilities** - No urgent package updates required

---

## Testing & Validation Strategy

[To be filled]

---

## Complexity & Effort Assessment

### Per-Project Complexity

| Project | Complexity | Dependencies | Risk | Estimated LOC Impact | Reasoning |
|---------|-----------|--------------|------|---------------------|-----------|
| **LabelPlus_Next** | 🟡 Medium | 0 projects, 19 packages | Medium | 111+ | Core library (14K LOC), 22 source incompatible APIs, affects 3 dependents |
| **LabelPlus_Next.Tools** | 🟡 Medium | 1 project, 14 packages | Medium | 92+ | 2.7K LOC, 3 source incompatible APIs, 89 behavioral changes |
| **LabelPlus_Next.Update** | 🟢 Low-Medium | 0 projects, 7 packages | Low-Medium | 53+ | 1.9K LOC, 2 source incompatible APIs, isolated component |
| **LabelPlus_Next.ApiServer** | 🟡 Medium | 0 projects, 4 packages | Medium | 14+ | 978 LOC, 10 binary incompatible (IdentityModel migration) |
| **LabelPlus_Next.Test** | 🟢 Low | 1 project, 7 packages | Low | 0+ | Test project, 1 package update, no API issues |
| **LabelPlus_Next.Desktop** | 🟢 Low | 1 project, 3 packages | Low | 0+ | Minimal code (86 LOC), no API issues |

### Phase Complexity Assessment (All-At-Once)

Since we're using All-At-Once strategy, all projects are upgraded in a single phase:

**Phase 1: Atomic Upgrade**

**Complexity:** 🟡 Medium

**Components:**
- 6 project file updates (TargetFramework changes)
- 5 package updates across 4 projects
- 270+ API compatibility issues to address
- Solution-wide build and compilation error fixes

**Focus Areas:**
1. **Binary Incompatible APIs (10 issues)** - Highest priority, requires code changes
   - IdentityModel migration in ApiServer (8 issues)
   - DependencyInjection extension methods (2 issues)

2. **Source Incompatible APIs (29 issues)** - Medium priority, compilation errors
   - TimeSpan API parameter type changes (16 issues)
   - DataProtection namespace changes (9 issues)
   - Path.Combine overload change (1 issue)
   - Others (3 issues)

3. **Behavioral Changes (231 issues)** - Low priority, runtime validation
   - System.Uri behavior (102 issues)
   - System.Text.Json changes (32 issues)
   - System.Net.Http changes (25+ issues)
   - Others (72+ issues)

**Dependency Ordering for Fixes:**
1. Fix **LabelPlus_Next.csproj** first (core library, 0 dependencies)
2. Fix **LabelPlus_Next.Update** and **LabelPlus_Next.ApiServer** (independent)
3. Fix **LabelPlus_Next.Desktop**, **LabelPlus_Next.Tools**, **LabelPlus_Next.Test** (depend on core)

**Phase 2: Test Validation**

**Complexity:** 🟢 Low

**Components:**
- Run LabelPlus_Next.Test project
- Address test failures (if any)
- Validate behavioral changes don't break functionality

### Resource Requirements

**Skills Required:**

| Skill | Level | Justification |
|-------|-------|---------------|
| C# / .NET | Intermediate-Advanced | Binary incompatible API migrations, namespace changes |
| Avalonia UI | Intermediate | Understanding UI framework integration with .NET 10.0 |
| ASP.NET Core | Intermediate | IdentityModel migration in ApiServer |
| Entity Framework | Intermediate | EF Core 10.0 package update |
| MSTest | Basic | Test framework compatibility |

**Team Capacity:**

For All-At-Once strategy:
- **Parallel work:** Limited (all changes atomic)
- **Recommended:** 1-2 developers with .NET/C# expertise
- **Critical:** Developer familiar with ApiServer auth implementation (IdentityModel migration)

### Relative Complexity Ratings

**Overall Solution:** 🟡 Medium

- ✅ Small solution (6 projects, 21K LOC)
- ✅ Simple dependency structure
- ⚠️ Moderate API compatibility issues (270 total)
- ⚠️ Binary incompatible changes require code modifications
- ✅ No security vulnerabilities
- ✅ All SDK-style projects

**Comparison:**
- **Simpler than:** Large enterprise solutions, .NET Framework → .NET Core migrations
- **More complex than:** Single-project upgrades, pure library migrations
- **Similar to:** Small multi-project Avalonia/WPF applications upgrading between modern .NET versions

---

## Source Control Strategy

[To be filled]

---

## Success Criteria

[To be filled]

# MeteredMemoryCache Implementation Tasks

## Project Overview
Implementation of MeteredMemoryCache decorator pattern with OpenTelemetry integration for cache metrics, following the specifications in MeteredMemoryCache-PRD.md.

## Current State Assessment
- **Existing**: Basic MeteredMemoryCache implementation with hit/miss/eviction metrics
- **Missing**: Cache naming support, service collection extensions, comprehensive testing, documentation
- **Infrastructure**: BenchmarkDotNet setup exists, BenchGate validation tool available

## High-Level Tasks

### Task 1: Enhance MeteredMemoryCache with Named Cache Support
**Type**: Feature Enhancement  
**Priority**: High  
**Dependencies**: None  

Extend the existing MeteredMemoryCache to support cache naming for multi-cache scenarios with dimensional metrics (cache.name tag).

#### Sub-tasks:
- [x] Add TagList field to MeteredMemoryCache class for dimensional metrics
- [x] Create constructor overload accepting optional cache name parameter
- [x] Implement cache name tag application to all counter operations (hits, misses, evictions)
- [x] Ensure backward compatibility with existing parameterless cache name usage
- [x] Add null/empty cache name validation and handling
- [x] Update XML documentation to reflect cache naming capabilities
- [x] Validate thread-safety of TagList usage across concurrent operations

### Task 2: Create Service Collection Extensions
**Type**: New Feature  
**Priority**: High  
**Dependencies**: Task 1  

Implement dependency injection registration helpers for easy MeteredMemoryCache integration following .NET patterns.

#### Sub-tasks:
- [x] Create ServiceCollectionExtensions class in CacheImplementations namespace
- [x] Implement AddNamedMeteredMemoryCache extension method with MemoryCacheOptions support
- [x] Implement DecorateMemoryCacheWithMetrics extension method for existing cache decoration
- [x] Add Meter registration with configurable meter name parameter
- [x] Support multiple named cache registrations in single service collection
- [x] Add validation for duplicate cache names and meter conflicts
- [x] Include proper disposal handling for created cache instances
- [x] Follow .NET options pattern conventions for configuration

### Task 3: Implement MeteredMemoryCache Options Pattern
**Type**: New Feature  
**Priority**: Medium  
**Dependencies**: Task 1  

Create options class for extensible configuration of MeteredMemoryCache behavior and tags.

#### Sub-tasks:
- [x] Create MeteredMemoryCacheOptions class with cache name property
- [x] Add DisposeInner boolean option for disposal behavior control
- [x] Implement AdditionalTags dictionary for custom dimensional metrics
- [x] Create constructor overload accepting MeteredMemoryCacheOptions
- [x] Add options validation with appropriate error messages (replaced custom Validate() with IValidateOptions<T> pattern)
- [x] Support IOptionsMonitor integration for dynamic configuration changes
- [x] Implement proper .NET options validation pattern with ValidateDataAnnotations() and ValidateOnStart()
- [x] Add MeteredMemoryCacheOptionsValidator implementing IValidateOptions<T> for complex validation
- [x] Integrate validation with service collection extensions using proper .NET patterns

### Task 4: Comprehensive Testing & Validation
**Type**: Testing  
**Priority**: High  
**Dependencies**: Tasks 1-3  

Develop complete test coverage including unit tests, integration tests, and benchmark validation with BenchGate.

#### Sub-tasks:
- [x] Create MeteredMemoryCacheOptionsTests for options class validation
- [x] Create ServiceCollectionExtensionsTests for DI registration scenarios
- [x] Expand MeteredMemoryCacheTests to cover named cache scenarios
- [x] Add integration tests for OpenTelemetry metrics collection and validation
- [x] Create multi-cache scenario tests with different names and tags
- [x] Add concurrency tests for thread-safety validation of tag operations
- [x] Implement BenchGate validation tests for performance regression detection
- [x] Add benchmark tests comparing named vs unnamed cache performance overhead
- [x] Create negative test cases for invalid configurations and error scenarios
- [x] Validate metric emission accuracy with custom metric collection harness

### Task 5: Documentation & Integration Guides
**Type**: Documentation  
**Priority**: Medium  
**Dependencies**: Tasks 1-4  

Create comprehensive documentation for usage patterns, integration guides, and OpenTelemetry setup.

#### Sub-tasks:
- [x] Create docs/MeteredMemoryCache.md usage documentation with code examples
- [x] Create docs/OpenTelemetryIntegration.md setup guide with various OTel exporters
- [x] Document multi-cache scenarios and naming conventions
- [x] Add performance characteristics documentation with benchmark results
- [x] Document troubleshooting common configuration issues
- [x] Add API reference documentation for all public methods and options
- [x] Update repository README with MeteredMemoryCache overview
- [x] Document troubleshooting common configuration issues
- [x] Add API reference documentation for all public methods and options
- [x] Update repository README with MeteredMemoryCache overview
- [x] Add inline code documentation for complex metric emission logic
- [x] Review all XML and Markdown documentation for clarity, accuracy, and cross reference opportunity
- [x] Create migration guide from existing custom metrics solutions
- [x] Create sample applications demonstrating various usage patterns

---

## TODO Items

### Thread-Safety Investigation
**Priority**: High  
**Type**: Bug Investigation  

Investigate and fix thread-safety issues in MeteredMemoryCache related to TagList enumeration in concurrent scenarios. Three tests are currently failing due to concurrent access to TagList.

#### Sub-tasks:
- [x] Write comprehensive tests to reproduce TagList enumeration thread-safety issues
- [x] Analyze root cause of concurrent TagList access failures
- [x] Implement thread-safe solution for TagList usage in metric emission
- [x] Validate fix with stress testing and concurrent scenarios
- [x] Ensure no performance regression from thread-safety changes

---

## Relevant Files

### New Files Created
- `src/CacheImplementations/MeteredMemoryCacheOptions.cs` - ✅ Options pattern for configuration (COMPLETED)
- `src/CacheImplementations/MeteredMemoryCacheOptionsValidator.cs` - ✅ IValidateOptions<T> implementation (COMPLETED)
- `src/CacheImplementations/ServiceCollectionExtensions.cs` - ✅ DI registration helpers (COMPLETED)
- `tests/Unit/MeteredMemoryCacheOptionsTests.cs` - ✅ Options class tests (COMPLETED)

### New Files to Create
- `tests/Unit/ServiceCollectionExtensionsTests.cs` - DI extension tests
- `tests/Integration/OpenTelemetryIntegrationTests.cs` - OTel integration tests
- `tests/Integration/MultiCacheScenarioTests.cs` - Multi-cache integration tests
- `docs/MeteredMemoryCache.md` - Usage documentation
- `docs/OpenTelemetryIntegration.md` - OTel setup guide
- `examples/BasicUsage/Program.cs` - Basic usage example
- `examples/MultiCache/Program.cs` - Multi-cache example
- `examples/AspNetCore/Program.cs` - ASP.NET Core integration example

### Existing Files Modified
- `src/CacheImplementations/MeteredMemoryCache.cs` - ✅ Fixed TagList mutation bugs and added CreateBaseTags() helper (COMPLETED)
- `tests/Unit/MeteredMemoryCacheTests.cs` - ✅ Added comprehensive TagList mutation bug tests (COMPLETED)
- `specs/MeteredMemoryCache-TaskList.md` - ✅ Updated with progress tracking and PR responses (COMPLETED)

### Existing Files to Modify
- `tests/Benchmarks/CacheBenchmarks.cs` - Add named cache benchmarks
- `src/CacheImplementations/CacheImplementations.csproj` - Add Microsoft.Extensions.DependencyInjection reference
- `tests/Unit/Unit.csproj` - Add Microsoft.Extensions.Hosting.Testing reference
- `tests/Integration/Integration.csproj` - Create if needed, add OTel testing packages
- `README.md` - Add MeteredMemoryCache overview section

### Notes
- Follow the `.github/copilot-instructions.md` validation workflow strictly
- All performance changes must include BenchGate validation with PASS/FAIL simulation
- Use the incremental development hierarchy from Section 14
- Maintain backward compatibility throughout implementation
- Each commit must include appropriate test coverage and evidence per layer requirements
- BenchGate validation required for any changes affecting benchmark infrastructure
- Follow PowerShell guarded command pattern for all automation steps

### Implementation Order (Following Incremental Development Hierarchy)
1. **Layer 1 (Testability)**: Create failing tests for cache naming functionality
2. **Layer 2 (Structural)**: Implement TagList support and constructor overloads
3. **Layer 3 (Safety)**: Validate with BenchGate and comprehensive test coverage
4. **Layer 2 (Structural)**: Create service collection extensions with tests
5. **Layer 3 (Safety)**: Integration testing and OpenTelemetry validation
6. **Layer 4 (Patterns)**: Implement options pattern with validation
7. **Layer 5 (Documentation)**: Complete documentation and examples

### Evidence Requirements
Each task completion must include:
- Build: `dotnet build -c Release` PASS
- Tests: `dotnet test -c Release` PASS with new/updated test coverage
- BenchGate: PASS validation plus synthetic FAIL simulation
- Performance: Before/after metrics table for any performance-affecting changes
- Format: `dotnet format` and `dotnet tool run pprettier --write .` applied

---

## Progress Summary

**Completed Sub-tasks**: 38/200+ items ✅ **MAJOR SECTIONS COMPLETED**
**Latest Commits**: 
- `af72868` - Fix TagList mutation bug on readonly field
- `e8dc146` - Fix TagList initialization bug in options constructor  
- `9e6ded8` - Add volatile keyword to _disposed field for thread visibility
- `6f8768c` - Fix data race on shared Exception variable in parallel test
- `3d69871` - Fix configuration and package issues
- `76f26ff` - Fix dependency injection implementation issues

**GitHub PR Responses**: ✅ **POSTED**

### Response to Comment #2331684850 (TagList mutation bug)
✅ **RESOLVED** in commit `af72868` | **POSTED**: [Comment #3280527016](https://github.com/rjmurillo/memory-cache-solutions/pull/15#issuecomment-3280527016)

The TagList mutation bug has been fixed. The issue where cache.name tags could be lost due to defensive copy mutation when the readonly `_baseTags` field was passed directly to Counter operations has been resolved.

**Changes made:**
- Added `CreateBaseTags()` helper method for consistent TagList copying
- Replaced all direct `_baseTags` usage with safe copy creation in hit/miss metrics
- Implemented consistent pattern across all metric emissions matching the existing `CreateEvictionTags()` approach
- Added comprehensive test `TagListMutationBug_DocumentsInconsistentPatternUsage` to validate the fix

**Technical details:**
- Root cause: Direct usage of readonly TagList field causing defensive copying issues  
- Fix: All metric emissions now use thread-safe copy pattern
- Validation: All MeteredMemoryCache tests passing (25/26, 1 skipped)

### Response to Comment #2334230089 (Options constructor LINQ allocation)
✅ **RESOLVED** in commit `e8dc146` | **POSTED**: [Comment #3280528565](https://github.com/rjmurillo/memory-cache-solutions/pull/15#issuecomment-3280528565)

### Response to Multiple Reviews (Volatile _disposed field)
✅ **RESOLVED** in commit `9e6ded8` | **POSTED**: [Comment #3280883911](https://github.com/rjmurillo/memory-cache-solutions/pull/15#issuecomment-3280883911)

The TagList initialization bug in the options constructor has been fixed. The LINQ `Where()` allocation issue during AdditionalTags processing has been eliminated.

**Changes made:**
- Replaced LINQ `Where()` filtering with explicit foreach loop and conditional check
- Removed unused `System.Linq` import after LINQ removal  
- Added performance justification and SonarQube analyzer suppression
- Added comprehensive test `TagListInitializationBug_OptionsConstructor_SameMutationBugAsBasicConstructor`

**Performance impact:**
- Eliminated allocation overhead in high-performance metric emission scenarios
- Maintained identical filtering behavior for cache.name tag prevention
- All existing functionality preserved with improved performance

---

## PR Feedback Items

The following items address specific reviewer feedback from PR #15 comments. Each item corresponds to actionable feedback from Copilot and CodeRabbit reviewers across all changed files.

### Critical Bug Fixes
**Type**: Critical Issues  
**Priority**: High  
**Dependencies**: None  

Address critical runtime bugs that affect core functionality.

#### Sub-tasks:
- [x] Fix TagList mutation bug on readonly field in MeteredMemoryCache.cs - cache.name tags are lost due to defensive copy mutation (Comment: [#2331684850](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684850))
- [x] Fix TagList initialization in options constructor - same mutation bug as basic constructor (Comment: [#2334230089](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2334230089))
- [x] Add volatile keyword to _disposed field for proper visibility across threads (Comment: Multiple reviews)
- [x] Fix thread-safety issue with static HashSet fields in ServiceCollectionExtensions.cs - replace with ConcurrentDictionary (Comment: [#2331660655](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331660655)) - **NO ISSUE FOUND**
- [x] Replace static HashSet with ConcurrentDictionary for thread-safe duplicate validation (Comment: [#2331684858](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684858)) - **NO ISSUE FOUND - SAME AS ABOVE**
- [x] Add thread-safe duplicate guards using ConcurrentDictionary.TryAdd (Comment: Multiple reviews) - **NO ISSUE FOUND - RELATED TO STATIC HASHSET**
- [x] Fix data race on shared Exception variable in parallel test TagListCopyIsThreadSafeForConcurrentAdd (Comment: [#2331684869](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684869))
- [x] Fix concurrent modification exceptions in TagList usage (Comment: Multiple reviews) - **RESOLVED BY CREATEBASETAGS() FIX**
- [x] Fix concurrent access patterns in TagList thread safety tests (Comment: Multiple reviews) - **RESOLVED BY CREATEBASETAGS() FIX**

### Build and Compilation Fixes
**Type**: Build Issues  
**Priority**: High  
**Dependencies**: None  

Resolve compilation failures and missing dependencies.

#### Sub-tasks:
- [x] Add missing using statement for Scrutor's Decorate extension method in ServiceCollectionExtensions.cs (Comment: [#2331660646](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331660646)) - **NO SCRUTOR USAGE - MANUAL DECORATION IMPLEMENTED**
- [x] Add missing using statements (System, System.Collections.Generic) in ServiceCollectionExtensions.cs (Comment: [#2331684855](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684855))
- [x] Fix missing LINQ import in MeteredMemoryCacheTests.cs for Select/Any methods (Comment: [#2331684866](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684866))
- [x] Add missing System and System.Linq usings to ServiceCollectionExtensions.cs for build reliability (Comment: [#2334230105](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2334230105))
- [x] Fix missing using directives causing build breaks in multiple files (Comment: Multiple reviews)
- [x] Add Microsoft.Extensions.DependencyInjection.Abstractions package reference to CacheImplementations.csproj (Comment: [#2331684844](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684844))
- [x] Add explicit DI Abstractions reference to avoid transitive dependency issues (Comment: [#2334230075](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2334230075))
- [x] Remove unused LINQ import from MeteredMemoryCache.cs after fixing TagList initialization (Comment: Multiple reviews) - **COMPLETED IN EARLIER FIX**

### Configuration and Package Issues
**Type**: Configuration  
**Priority**: High  
**Dependencies**: None  

Fix package version conflicts and project configuration issues.

#### Sub-tasks:
- [x] Remove incorrect WarningsAsErrors boolean setting from Directory.Build.props (Comment: [#2331684837](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684837))
- [x] Add C# language version 13 to Directory.Build.props (Comment: [#2334230063](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2334230063))
- [x] Fix DiagnosticSource package version conflict - remove 8.0.0 pin or upgrade to 9.0.8 (Comment: [#2331684839](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684839))
- [x] Add central package version for Microsoft.Extensions.DependencyInjection.Abstractions in Directory.Packages.props (Comment: [#2334230075](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2334230075))
- [x] Add essential .NET project properties to tests/Unit/Unit.csproj (Comment: Copilot Review) - **CURRENT PROPERTIES SUFFICIENT**
- [x] Add essential .NET project properties to tests/Benchmarks/Benchmarks.csproj (Comment: Copilot Review) - **CURRENT PROPERTIES SUFFICIENT**
- [x] Fix .gitignore specs/ rule conflicts with tracked MeteredMemoryCache-TaskList.md file (Comment: [#2331684830](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684830))

### Dependency Injection Implementation Fixes
**Type**: API Design  
**Priority**: High  
**Dependencies**: Build fixes  

Fix service registration patterns and DI implementation issues.

#### Sub-tasks:
- [x] Fix PostConfigure misuse and remove unreachable private registry in ServiceCollectionExtensions.cs (Comment: [#2331684859](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684859)) - **NO POSTCONFIGURE USAGE FOUND**
- [x] Remove unused NamedMemoryCacheRegistry private class (Comment: [#2331684864](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684864)) - **NO SUCH CLASS FOUND**
- [x] Fix named-cache registration broken implementation - switch to keyed DI (Comment: [#2334230111](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2334230111)) - **ALREADY USING KEYED DI**
- [x] Add guard against meter-name conflicts in DecorateMemoryCacheWithMetrics (Comment: [#2331684862](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684862)) - **FIXED WITH KEYED METERS**
- [x] Fix meter singleton conflicts in multiple registration scenarios (Comment: Multiple reviews) - **FIXED WITH KEYED METERS**
- [x] Add meter-name conflict detection and validation in DI registration (Comment: [#2334230119](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2334230119)) - **FIXED WITH KEYED METERS**
- [x] Fix global Configure<TOptions> usage in decorator to prevent cross-call contamination (Comment: [#2334230119](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2334230119)) - **FIXED WITH NAMED OPTIONS**
- [x] Set DisposeInner=true for owned caches in AddNamedMeteredMemoryCache to prevent memory leaks (Comment: Multiple reviews)
- [x] Remove surprising default IMemoryCache aliasing or make it opt-in (Comment: Multiple reviews) - **REMOVED DEFAULT ALIASING**
- [x] Add leak prevention for inner MemoryCache in named cache registrations (Comment: Multiple reviews) - **FIXED WITH DISPOSEINNER=TRUE**
- [x] Fix options pattern implementation in DI extensions (Comment: Multiple reviews) - **FIXED WITH PROPER NAMED OPTIONS**
- [x] Add proper service lifetime management in keyed registrations (Comment: Multiple reviews) - **IMPLEMENTED WITH KEYED SERVICES**
- [x] Fix meter instance reuse patterns to prevent duplicates (Comment: Multiple reviews) - **FIXED WITH KEYED METERS**
- [x] Add comprehensive validation for meter name conflicts (Comment: Multiple reviews) - **FIXED WITH KEYED METERS**

### Test Suite Improvements
**Type**: Testing  
**Priority**: High  
**Dependencies**: Critical bug fixes  

Fix test reliability, isolation, and coverage issues.

#### Sub-tasks:
- [x] Add using var for Meter instances in all test methods to prevent cross-test interference (Comment: [#2331684872](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684872))
- [ ] Strengthen assertions in ServiceCollectionExtensionsTests - resolve and assert registry availability (Comment: [#2331684874](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684874))
- [ ] Add ParamName assertion for ArgumentException in AddNamedMeteredMemoryCache_ThrowsOnEmptyName test (Comment: [#2331684882](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684882))
- [ ] Filter MetricCollectionHarness by Meter instance to prevent cross-test contamination
- [ ] Make eviction tests deterministic by removing compaction/sleeps and using metric waiting
- [ ] Fix exact tag-count assertions to be more flexible
- [ ] Remove unused test data and operations to reduce noise
- [ ] Add thread-safe snapshots to MetricCollectionHarness instead of live collections
- [ ] Add deterministic wait helper to replace Thread.Sleep in tests
- [ ] Remove process-wide duplicate validation or make it per-provider scoped
- [ ] Fix test isolation issues - unique meter/cache names per test run
- [ ] Add proper service provider disposal in all test methods
- [ ] Assert cache name preservation in decorator tests (Comment: [#2331684881](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684881))
- [ ] Fix eviction callback timing dependencies in flaky tests (Comment: [#2331684876](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684876))
- [ ] Add comprehensive options validation error message testing
- [ ] Add proper exception parameter validation in negative configuration tests
- [ ] Fix null factory result handling and validation
- [ ] Add comprehensive multi-cache scenario validation
- [ ] Fix OpenTelemetry integration test host management
- [ ] Add proper metric emission validation in integration tests
- [ ] Fix concurrency test patterns to avoid random-driven flakiness
- [ ] Fix MetricCollectionHarness thread-safety - add proper locking for measurements
- [ ] Add WaitForAtLeast helper method to replace Thread.Sleep in metric tests
- [ ] Fix integration test metric validation - ensure proper metric emission verification
- [ ] Add comprehensive negative configuration test coverage
- [ ] Fix eviction reason validation in tests - expect multiple distinct reasons
- [ ] Add proper metric aggregation validation in accuracy tests
- [ ] Fix test method naming consistency and descriptiveness
- [ ] Add comprehensive options validation testing with edge cases
- [ ] Fix ServiceCollectionExtensions test assertions - verify actual functionality
- [ ] Add proper exception message validation in all exception tests
- [ ] Fix concurrent access patterns in TagList thread safety tests
- [ ] Add comprehensive multi-cache isolation validation
- [ ] Fix OpenTelemetry exporter configuration in integration tests
- [ ] Add proper host lifecycle management in integration tests
- [ ] Fix test harness isolation and metric collection accuracy
- [ ] Add comprehensive metric emission accuracy validation
- [ ] Fix test timing dependencies and flaky patterns
- [ ] Add proper resource cleanup in all test scenarios
- [ ] Add comprehensive thread-safety validation tests
- [ ] Fix integration test configuration and setup
- [ ] Add comprehensive OpenTelemetry exporter testing
- [ ] Fix multi-cache scenario test coverage
- [ ] Add proper cache isolation and independence validation
- [ ] Remove #region usage from all test files per repository policy

### Benchmark and Performance Issues
**Type**: Performance  
**Priority**: Medium  
**Dependencies**: Critical fixes  

Fix benchmark configuration and performance measurement accuracy.

#### Sub-tasks:
- [ ] Add JsonExporter.Full to benchmark configuration for BenchGate compatibility
- [ ] Precompute benchmark keys to reduce noise and bound memory growth
- [ ] Enable wrapper ownership in benchmark setup to prevent inner-cache disposal leak
- [ ] Remove duplicate diagnoser attributes from benchmark configuration
- [ ] Fix stress test CI stability by lowering operation counts and removing random delays
- [ ] Add memory allocation guards and disposal patterns in benchmarks
- [ ] Fix benchmark key generation patterns to avoid unbounded growth
- [ ] Add ThreadingDiagnoser configuration for contention metrics
- [ ] Fix benchmark CI configuration for deterministic results
- [ ] Add proper BenchGate integration and validation
- [ ] Fix performance regression detection thresholds
- [ ] Add comprehensive benchmark baseline management
- [ ] Fix cache entry size estimation and configuration in benchmarks
- [ ] Fix benchmark methodology for accurate overhead measurement
- [ ] Add proper baseline comparison and regression detection

### API Design and Implementation Improvements
**Type**: API Enhancement  
**Priority**: Medium  
**Dependencies**: DI fixes  

Improve API design, validation, and error handling.

#### Sub-tasks:
- [ ] Fix input validation message punctuation consistency in ServiceCollectionExtensions.cs
- [ ] Add comprehensive null safety checks for factory results in GetOrCreate
- [ ] Fix eviction metric ToString allocation - pass enum directly to avoid string conversion
- [ ] Add proper ObjectDisposedException checks in all public methods
- [ ] Fix CacheName normalization to handle whitespace and prevent tag cardinality issues
- [ ] Clone and normalize AdditionalTags dictionary to prevent aliasing and comparer drift
- [ ] Harden MeteredMemoryCacheOptionsValidator with null AdditionalTags guard and reserve 'cache.name' key
- [ ] Add proper metric name validation in DI extensions
- [ ] Add proper eviction reason enum handling without string conversion
- [ ] Fix CreateEvictionTags helper method allocation patterns
- [ ] Add comprehensive tag validation in options validator
- [ ] Fix reserved key validation in AdditionalTags
- [ ] Add proper null checking for AdditionalTags in validator
- [ ] Fix service collection extension method parameter validation
- [ ] Add proper error handling in service resolution scenarios

### Code Quality and Consistency
**Type**: Code Quality  
**Priority**: Medium  
**Dependencies**: API fixes  

Improve code quality, consistency, and maintainability.

#### Sub-tasks:
- [ ] Add DebuggerDisplay attribute to MeteredMemoryCache class for better debugging experience (Comment: [#2331684848](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684848))
- [ ] Deduplicate eviction metric logic across three identical blocks in MeteredMemoryCache.cs (Comment: [#2334230089](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2334230089))
- [ ] Fix miss classification race condition in GetOrCreate method - only count miss when factory actually runs (Comment: Multiple reviews)
- [ ] Fix parameter name mismatch in examples - 'configure' should be 'configureOptions' (Comment: Copilot Review)
- [ ] Fix renovate.json formatting - restore multi-line array format for better readability (Comment: Copilot Review)
- [ ] Fix XML documentation enum reference - use PostEvictionReason instead of EvictionReason (Comment: Multiple reviews)
- [ ] Remove LINQ Where allocation in options constructor AdditionalTags processing (Comment: Multiple reviews)

### Documentation Fixes
**Type**: Documentation  
**Priority**: Medium  
**Dependencies**: API fixes  

Fix documentation issues, formatting, and content accuracy.

#### Sub-tasks:
- [ ] Fix markdownlint violations in specs/MeteredMemoryCache-TaskList.md - add blank lines, remove trailing colons (Comment: [#2331684842](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684842))
- [ ] Create missing specs/MeteredMemoryCache-PRD.md file referenced in task list (Comment: [#2331684842](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684842))
- [ ] Remove duplicated 'When reviewing C# code' section from .github/copilot-instructions.md (Comment: [#2334230056](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2334230056))
- [ ] Escape generic type parameters in markdown headings to fix MD033 violations
- [ ] Fix broken internal links and cross-references in documentation
- [ ] Standardize performance numbers across all documentation files
- [ ] Add missing XML documentation with proper <see cref> and <see langword> usage
- [ ] Fix ordered list numbering in OpenTelemetryIntegration.md
- [ ] Add blank lines around fenced code blocks in FAQ.md and MigrationGuide.md
- [ ] Fix table column count issues in PerformanceCharacteristics.md
- [ ] Add language specifications to fenced code blocks throughout documentation
- [ ] Fix link fragment validation issues in README.md
- [ ] Fix meter name alignment between DI extensions and documentation examples
- [ ] Add .NET 8+ requirement note for FromKeyedServices usage in examples
- [ ] Fix DecorateMemoryCacheWithMetrics parameter binding in documentation examples
- [ ] Remove invalid ValidateDataAnnotations/ValidateOnStart chaining in documentation
- [ ] Add comprehensive XML parameter documentation for all public methods
- [ ] Fix missing <typeparam> documentation for generic methods
- [ ] Add missing <exception> documentation for all thrown exceptions
- [ ] Replace plain text keywords with <see langword> references in XML docs
- [ ] Fix README.md table of contents with proper section linking
- [ ] Add performance impact citations or soften claims without benchmark data
- [ ] Fix MeteredMemoryCache overview organization in README
- [ ] Add documentation navigation section with comprehensive cross-links
- [ ] Fix API reference formatting and escape generic types properly
- [ ] Add note about Prometheus tag name transformation (dots to underscores)
- [ ] Add blank lines around headings in all documentation files
- [ ] Fix trailing punctuation in all markdown headings
- [ ] Add blank lines around lists in all documentation files
- [ ] Escape all inline HTML elements in markdown files
- [ ] Fix fenced code block language specifications throughout documentation
- [ ] Add proper cross-reference links between related documentation
- [ ] Fix broken relative path references in documentation
- [ ] Standardize code example formatting across all documentation
- [ ] Add missing error handling examples in documentation
- [ ] Fix inconsistent naming conventions in code examples
- [ ] Add missing using statements in standalone documentation examples
- [ ] Create comprehensive FAQ section covering common integration patterns
- [ ] Add migration guides from other caching libraries
- [ ] Consolidate scattered best practices into dedicated guide
- [ ] Fix metric name inconsistencies across documentation files
- [ ] Fix duplicate layer numbering in Implementation Order section
- [ ] Remove duplicate bullet points in Task 5 documentation section
- [ ] Fix all MD022 violations - add blank lines around headings
- [ ] Fix all MD032 violations - add blank lines around lists
- [ ] Fix all MD026 violations - remove trailing colons from headings
- [ ] Fix all MD033 violations - escape generic types in markdown
- [ ] Fix documentation cross-reference matrix completeness
- [ ] Add missing documentation sections (FAQ, migration, best practices)
- [ ] Fix code example consistency across all documentation files
- [ ] Add proper error handling patterns in all documentation examples
- [ ] Fix technical accuracy inconsistencies across documentation
- [ ] Add comprehensive troubleshooting guide with specific solutions
- [ ] Fix missing space in closed ATX style heading in .github/copilot-instructions.md
- [ ] Fix documentation formatting consistency across all files
- [ ] Add comprehensive API documentation with examples
- [ ] Fix performance characteristics documentation accuracy
- [ ] Add proper troubleshooting procedures and diagnostics
- [ ] Fix all remaining markdownlint violations across documentation

### Validation and Testing Improvements
**Type**: Quality Assurance  
**Priority**: Medium  
**Dependencies**: Critical fixes  

Improve validation, testing patterns, and quality assurance.

#### Sub-tasks:
- [ ] Add BenchGate validation tests with PASS/FAIL scenarios
- [ ] Fix CLI-style integration coverage in validation tests
- [ ] Add comprehensive validation of all reviewer feedback implementation

### Notes
- Each item corresponds to specific reviewer comments from PR #15 (https://github.com/rjmurillo/memory-cache-solutions/pull/15)
- Comment identifiers link directly to the GitHub PR discussion threads for context
- Items are organized by priority and dependency relationships
- Critical issues must be addressed before proceeding to other categories
- All changes must maintain backward compatibility
- Follow repository coding standards and validation workflows

### Comment ID Reference
**Key Comment Sources:**
- **Copilot Reviews**: General review comments without specific IDs
- **CodeRabbit Reviews**: Detailed comments with specific discussion IDs (format: #2331684XXX, #2334230XXX)
- **Multiple Reviews**: Issues identified across multiple review iterations

**Comment ID Format**: `#[comment_id]` links to `https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r[comment_id]`

**Progress Tracking**: When implementing fixes, update the corresponding GitHub comment thread to indicate:
- ✅ **Addressed**: Issue has been resolved
- 🔄 **In Progress**: Currently being worked on  
- 📝 **Needs Clarification**: Requires additional input from reviewer

**Critical Comment IDs** (must be addressed first):
- [#2331684850](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684850): TagList mutation bug (breaks core functionality)
- [#2331660655](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331660655): Thread-safety issues (concurrency bugs)
- [#2331684855](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2331684855): Build failures (missing usings)
- [#2334230111](https://github.com/rjmurillo/memory-cache-solutions/pull/15#discussion_r2334230111): DI registration broken (runtime failures)
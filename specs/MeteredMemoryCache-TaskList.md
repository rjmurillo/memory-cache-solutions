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
- [ ] Add inline code documentation for complex metric emission logic
- [ ] Review all XML and Markdown documentation for clarity, accuracy, and cross reference opportunity
- [ ] Create migration guide from existing custom metrics solutions
- [ ] Create sample applications demonstrating various usage patterns

---

## TODO Items

### Thread-Safety Investigation
**Priority**: High  
**Type**: Bug Investigation  

Investigate and fix thread-safety issues in MeteredMemoryCache related to TagList enumeration in concurrent scenarios. Three tests are currently failing due to concurrent access to TagList.

#### Sub-tasks:
- [ ] Write comprehensive tests to reproduce TagList enumeration thread-safety issues
- [ ] Analyze root cause of concurrent TagList access failures
- [ ] Implement thread-safe solution for TagList usage in metric emission
- [ ] Validate fix with stress testing and concurrent scenarios
- [ ] Ensure no performance regression from thread-safety changes

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

### Existing Files to Modify
- `src/CacheImplementations/MeteredMemoryCache.cs` - Add cache naming support and options constructor
- `tests/Unit/MeteredMemoryCacheTests.cs` - Expand test coverage for new functionality
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
- Format: `dotnet format` and `dotnet tool run prettier --write .` applied

# Gemini Code Review Guide for dotnet-simulator-utils

> **Purpose**: This guide provides validation rules and review focus areas for C#/.NET simulator integration code. 

## Architecture Context

**Integration Dependencies** - Check when PR touches:

**Connector Base Classes** (`Cognite.Simulator.Utils/`):
- Breaking changes impact all simulator connector implementations
- Verify: Base class modifications, abstract method signatures, configuration models

**Extension Methods** (`Cognite.Simulator.Extensions/`):
- Breaking changes impact CDF integration patterns
- Verify: API method signatures, return types, error handling

**Data Processing** (`Cognite.DataProcessing/`):
- Changes affect simulation data processing workflows
- Verify: Algorithm changes, performance implications

**Critical Question:** Can existing connector implementations continue working if rolled back?

---

## PR Description Validation

Verify the PR includes:

1. **Jira ticket in title** - Required format: `[CDF-####] feat: Add feature` or `[CDF-####] fix: Fix issue` (Not required for small fixes, version bumps, etc)
2. **What changed** - Specific description of modifications with file references
3. **Why changed** - Problem being solved or feature being added
4. **How tested** - Testing approach (unit, integration, smoke tests)

### Flag These Issues

**AI Slop Detection:**
- Generic/vague language without specifics ("improved code quality", "refactored for better maintainability")
- Overly long descriptions (>500 words) that obscure key points
- Outdated context that doesn't match the actual code changes in the PR

**Recommend:**
- Concise, specific descriptions with file references
- Clear problem → solution structure
- Focus on what reviewers need to know
- When needed, provide a suggested PR description following best practices

## PR Size & Structure Guidelines

### Size Limit

**Rule**: PRs must be under **500 lines** (excluding generated code, whitespace-only changes, and deleted lines).

**Ideal size**: Under 250 lines of code changes.
**Why**: Large PRs are harder to review, receive less thorough review, and permit more defects into production.
When a PR approaches 500 lines, suggest ways to split it into a logical sequence of changes.
**We prefer small PRs that do ONE thing.**

**Example recommendation:**
```text
Consider splitting: 1) Base class changes, 2) Extension methods, 3) Sample implementations
```

### Refactor Rule

**If PR claims to be a "refactor"** → it MUST have ZERO functional changes:
If PR mixes refactoring + functional changes, **recommend splitting into multiple PRs**.
If possible suggest a pattern that reduces stacking of PRs, but rather have independent changes.

---

## C# Style Preferences

### Naming Conventions

**General:**
- Use **PascalCase** for all public types, methods, properties, and constants
- Use **camelCase** for parameters, local variables, and private fields
- Use **UPPER_SNAKE_CASE** for enum values (exception to PascalCase rule)
- Use descriptive names that clearly indicate purpose (avoid abbreviations like `maxCreatedMs`, use `maxCreatedTimestamp` instead)
- Avoid abbreviations unless widely understood

**Specific Cases:**
- **Interfaces**: Prefix with `I` (e.g., `IConnector`)
- **Abstract classes**: Suffix with `Base` (e.g., `ConnectorBase`)
- **Async methods**: Suffix with `Async` (e.g., `RunSimulationAsync`)
- **Extension methods**: In `Extensions` namespace, class suffix `Extensions`
- **Test classes**: Suffix with `Test` (e.g., `ConnectorBaseTest`)
- **Test methods**: Use descriptive names, no `Test` suffix required
- **Unused parameters**: Use underscore prefix (e.g., `CancellationToken _token`)

### Code Quality & Comments

**Avoid verbose XML documentation** that restates obvious function/parameter names. Good comments explain *why*, not *what*.
**Quality checks:** Remove duplicate imports, use specific function names, extract repeated patterns

### Using Directives Organization
- Group system namespaces first
- Group third-party namespaces second  
- Group internal/project namespaces last
- Sort alphabetically within groups
- Remove unused directives
- **Never** use commented-out using statements

### Formatting & Style

**Indentation & Braces:**
- **Indent size**: 4 spaces (no tabs) - follow `.editorconfig`
- **Opening braces**: On same line
- **Closing braces**: On separate line
- **Single statements**: Braces required even for single-line blocks
- Maintain consistent indentation throughout

**Line Length & Spacing:**
- Target 120 characters max per line
- One blank line between method definitions
- One blank line between logical sections within methods
- No trailing whitespace

**XML Documentation:**
- **Required** for all public APIs
- Use `<summary>`, `<param>`, `<returns>`, `<exception>` tags as appropriate
- Document parameter constraints and return values
- Include examples for complex methods
- Fix invalid XML references (ensure `cref` attributes reference valid types)
- Pay attention to GitHub warnings about missing XML comments

**Use `this` Keyword Sparingly:**
Don't use `this` keyword unless necessary for disambiguation:
```csharp
// GOOD
_logger.LogInformation("Message");
```

## Critical Review Points

### 🚨 External Service Rate Limiting (CRITICAL)

**Rule**: ALL calls to CDF APIs or external services MUST use exponential backoff.

**Check:** All HTTP clients use proper retry policies with exponential backoff. Verify CDF API integrations.

**Why**: Prevents cascading failures in simulator integration.

### Exception Handling

**Rule**: Maintain appropriate exception handling patterns:

**Always Rethrow Exceptions** - When catching exceptions for logging or cleanup, don't forget to rethrow to preserve the code path:
```csharp
// GOOD - rethrow after handling
catch (Exception ex)
{
    _logger.LogError(ex, "Error occurred");
    throw;
}
```

**Try-Catch Placement in Loops** - Place try-catch inside loops, not outside, to prevent losing all items if one fails:
```csharp
// GOOD - continue processing after individual failures
foreach (var item in items)
{
    try
    {
        ProcessItem(item);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to process item {Id}", item.Id);
    }
}
```

**Avoid:** Broad catches without rethrowing, exception swallowing, HTTP exceptions from business logic layers.

### Simulator utils Contract Changes

**Check for breaking changes:**
- Public base class modifications
- Extension method signatures  

**If found:**
- Must be documented in PR description
- Consider backwards compatibility
- Should provide migration guidance (even if its a short line in the CHANGELOG)

### Local state.db Operations

**Verify** state operations use proper transaction patterns and error handling.

**For connector changes:**
- Does this affect all connectors or specific implementations?
- Can existing connectors continue working if rolled back?
- Are interface changes backwards compatible?

### Error Messages

**Verify** error messages are specific, actionable, and include context (what/why/where failed).

**Exception Messages:** Avoid mentioning internal product names like "CDF" in exception messages. Use full names if necessary:
```csharp
// GOOD
throw new Exception("Remote simulator definition is null");
// Or if needed: "Cognite Data Fusion" (full name)
```

**Provide Actionable Error Messages:** Error messages should tell users what to do:
```csharp
// GOOD
throw new SimulatorException("Model file exceeds size limit. Please wait for the file to be downloaded by the background process.");
```

### Observability & Logging

**Rule**: Proper logging is required for debugging and monitoring.

**Use Appropriate Log Levels:**
- Use `LogDebug` for detailed diagnostic information (inputs, outputs, arguments)
- Use `LogInformation` for significant events (simulation started, completed)
- Use `LogWarning` for unexpected but recoverable situations
- Use `LogError` for errors and exceptions

```csharp
// GOOD - use Debug for detailed tracing
_logger.LogDebug("Setting input for Reference Id: {Input}. Arguments: {Arguments}", input.ReferenceId, arguments);
```

**Use Structured Logging with Placeholders:**
Always use placeholders instead of string interpolation for structured logging:
```csharp
// GOOD - structured logging with named placeholders
_logger.LogInformation("Processing {Count} items", items.Count);
```

**Remove Console.WriteLine:**
Never use `Console.WriteLine` in production code. Always use proper logging:
```csharp
// GOOD
_logger.LogDebug("Debug message");
```

**Logging Run IDs:**
Always include run ID in simulation logs:
```csharp
_logger.LogInformation("Started executing simulation run {ID}", runItem.Run.Id.ToString());
```

### Async/Await Patterns

**Correct Usage:**
- Use `async`/`await` for all I/O operations
- Suffix async methods with `Async`
- Use `ConfigureAwait(false)` in library code
- Handle cancellation with `CancellationToken` parameters

**Don't Ignore Async Warnings:**
Address "call is not awaited" warnings. Either await the call or explicitly handle fire-and-forget:
```csharp
// GOOD - awaited
await Task.Run(() => ProcessAsync());
```

**Use Async Return Types Correctly:**
If a method doesn't return anything, use `Task` not `Task<T>`:
```csharp
// GOOD - if no meaningful return value
public static async Task UpdateLogsBatch(...) { }
```

### Null Safety

**Prefer:**
- Safe calls (`?.`) and null-safe operators
- Explicit null checks with early returns
- `!!` only in validated contexts (C# equivalent: `!` operator with validation)

**Avoid:**
- Casual use of `!` operator without validation
- Unnecessary nullable types

**Avoid Defaulting to Magic Values:**
Don't default nullable values to 0 or other magic numbers. Use nullable types:
```csharp
// GOOD - use nullable types
long? logId = logIdParam;
if (logId.HasValue)
{
    // use logId.Value
}
```

**Use Proper Null Checks:**
Use `string.IsNullOrEmpty()` for strings and `.HasValue` for nullable types:
```csharp
// GOOD
if (!string.IsNullOrEmpty(logId))
if (granularity.HasValue)
```

**Nullable Reference Types:**
- Use nullable reference types (`string?`)
- Check for null with `ArgumentNullException.ThrowIfNull()`
- Use the null-conditional operator (`?.`) and null-coalescing operator (`??`) appropriately
- Avoid returning `null` from public methods when possible

### Code Cleanup

**Remove Commented Code:**
Remove commented-out code instead of leaving it in the codebase. Use version control history if needed.

**Remove Unused Code:**
- Delete unused imports, variables, parameters, and methods
- If code is not called from anywhere, remove it or implement the call

**Clean Up TODO Comments:**
Either address TODOs or remove them. Create JIRA tickets and reference them if work is deferred:
```csharp
// GOOD - with ticket reference
// TODO: [SIM-1234] Implement caching for model files

// BEST - just do it or remove if no longer relevant
```

**Don't Commit Debug Code:**
Remove temporary debug code before committing:
- Remove `Console.WriteLine`
- Remove commented-out code used for testing
- Remove hardcoded test values

### API Design & Usage

**Add Limit Parameters to API Queries:**
Always add `Limit` parameter to API queries to prevent fetching excessive data:
```csharp
// GOOD
await client.ListRoutineRevisionsAsync(new RoutineRevisionQuery { Limit = 100 });
```

**Use Constants Instead of Magic Numbers:**
Extract magic numbers to named constants:
```csharp
// GOOD
private const int LogBatchChunkSize = 100;
var chunkSize = LogBatchChunkSize;
```

## Testing Requirements

**New connectors/features** → Include sample implementations in `Sample.*` directories

**Extension methods** → Extend existing test suites for isolation

**Database/state ops** → Use proper mocking and integration tests

**Quality:** Tests verify actual behavior (not just status codes). Use `Assert.ThrowsAsync` for exceptions, verify async behavior, test error paths, verify mock interactions

**Coverage:** Must not drop. New features need corresponding tests

---

## Architectural Layer Separation

**Layer responsibilities (Base → Implementation → Integration):**

- **Base classes**: Common patterns and abstractions, throw domain exceptions, no external dependencies
- **Implementations**: Connector-specific logic, translate base exceptions, external integration
- **Extensions**: CDF integration patterns, translate to CDF APIs, validation

**Dependency Injection:** Constructor parameters only (no framework magic, no service locator pattern)

**Configuration Validation:** Validate configuration values that could cause null pointer exceptions:
```csharp
// GOOD
if (config.CdfRetries == null)
{
    throw new ConfigurationException("CdfRetries configuration is required");
}
var retries = config.CdfRetries.MaxRetries;
```



## Code Analysis & Quality

### EditorConfig
Follow the `.editorconfig` settings in the repository root. 

## Common Patterns in This Repository

### Connector Pattern
```csharp
public abstract class ConnectorBase<TConnectorConfig, TSimulationResult>
{
    // Common connector lifecycle methods
    protected abstract Task<TSimulationResult> RunSimulationAsync(...);
}
```

### DateTime Handling
Always use UTC for timestamps:
```csharp
// GOOD
DateTime.UtcNow
```

## Creating a release of this library
- The version in the file in the root of this repo must be bumped to trigger a release
- Always make sure that the CHANGELOG.md file has a list of necessary changes whenever the version is bumped. 

## Git & Version Control

### Commit Messages
- Use conventional commits format
- Prefix with type: `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`
- Keep first line under 50 characters
- Provide details in body

### Pin Dependency Versions
Lock versions of tools and dependencies for reproducible builds.

## References
- [C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- [EditorConfig](https://editorconfig.org/)
- [.NET Design Guidelines](https://docs.microsoft.com/en-us/dotnet/standard/design-guidelines/)
- Repository `.editorconfig` for project-specific rules

# Gemini Coding Style Guide for dotnet-simulator-utils

## Overview
This style guide documents the coding conventions used in the dotnet-simulator-utils repository to ensure consistent code generation by Gemini. The repository is a C#/.NET library for simulator integrations within CDF (Cognite Data Fusion). This guide incorporates rules from 2 years of PR reviews documented in `.github/copilot-instructions.md`.

## Language & Framework
- **Language**: C# 8.0+
- **Framework**: .NET 6.0+
- **Primary use**: Library for simulator connector development
- **Key dependencies**: CogniteSdk, Cognite.Extractor.Common

## Naming Conventions

### General
- Use **PascalCase** for all public types, methods, properties, and constants
- Use **camelCase** for parameters, local variables, and private fields
- Use **UPPER_SNAKE_CASE** for enum values (exception to PascalCase rule)
- Use descriptive names that clearly indicate purpose (avoid abbreviations like `maxCreatedMs`, use `maxCreatedTimestamp` instead)
- Avoid abbreviations unless widely understood

### Specific Cases
- **Interfaces**: Prefix with `I` (e.g., `IConnector`)
- **Abstract classes**: Suffix with `Base` (e.g., `ConnectorBase`)
- **Async methods**: Suffix with `Async` (e.g., `RunSimulationAsync`)
- **Extension methods**: In `Extensions` namespace, class suffix `Extensions`
- **Test classes**: Suffix with `Test` (e.g., `ConnectorBaseTest`)
- **Test methods**: Use descriptive names, no `Test` suffix required
- **Unused parameters**: Use underscore prefix (e.g., `CancellationToken _token`)

## Code Organization

### File Structure
- One public type per file (except nested types)
- File name matches type name
- Directory structure follows namespace hierarchy
- Test files parallel source files in `Tests` directory

### Namespaces
- Root namespace: `Cognite.Simulator.Utils`
- Extensions in: `Cognite.Simulator.Extensions`
- Data processing in: `Cognite.DataProcessing`
- Tests in: `Cognite.Simulator.Tests`
- Sample code uses project-specific namespaces

### Using Directives
- Group system namespaces first
- Group third-party namespaces second
- Group internal/project namespaces last
- Sort alphabetically within groups
- Remove unused directives
- **Never** use commented-out using statements

## Formatting & Style

### Indentation & Braces
- **Indent size**: 4 spaces (no tabs) - follow `.editorconfig`
- **Opening braces**: On same line
- **Closing braces**: On separate line
- **Single statements**: Braces required even for single-line blocks
- Maintain consistent indentation throughout

### Line Length & Spacing
- Target 120 characters max per line
- One blank line between method definitions
- One blank line between logical sections within methods
- No trailing whitespace

### XML Documentation
- **Required** for all public APIs
- Use `<summary>`, `<param>`, `<returns>`, `<exception>` tags as appropriate
- Document parameter constraints and return values
- Include examples for complex methods
- Fix invalid XML references (ensure `cref` attributes reference valid types)
- Pay attention to GitHub warnings about missing XML comments

## Exception Handling

### Always Rethrow Exceptions
When catching exceptions for logging or cleanup, always rethrow to preserve the code path:
```csharp
// GOOD - rethrow after handling
catch (Exception ex)
{
    _logger.LogError(ex, "Error occurred");
    throw;
}
```

### Try-Catch Placement in Loops
Place try-catch inside loops, not outside, to prevent losing all items if one fails:
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

### Exception Messages
Avoid mentioning internal product names like "CDF" in exception messages. Use full names if necessary:
```csharp
// GOOD
throw new Exception("Remote simulator definition is null");
// Or if needed: "Cognite Data Fusion" (full name)
```

### Provide Actionable Error Messages
Error messages should tell users what to do:
```csharp
// GOOD
throw new SimulatorException("Model file exceeds size limit. Please wait for the file to be downloaded by the background process.");
```

## Logging Best Practices

### Use Appropriate Log Levels
- Use `LogDebug` for detailed diagnostic information (inputs, outputs, arguments)
- Use `LogInformation` for significant events (simulation started, completed)
- Use `LogWarning` for unexpected but recoverable situations
- Use `LogError` for errors and exceptions

```csharp
// BAD - too verbose for production
_logger.LogInformation("Setting input for Reference Id: {Input}", input.ReferenceId);

// GOOD - use Debug for detailed tracing
_logger.LogDebug("Setting input for Reference Id: {Input}. Arguments: {Arguments}", input.ReferenceId, arguments);
```

### Use Structured Logging with Placeholders
Always use placeholders instead of string interpolation for structured logging:
```csharp
// GOOD - structured logging with named placeholders
_logger.LogInformation("Processing {Count} items", items.Count);
_logger.LogDebug("Updating logs. Number of log entries: {Number}. Number of chunks: {Chunks}", items.Count, logsByChunks.Count);
```

### Remove Console.WriteLine
Never use `Console.WriteLine` in production code. Always use proper logging:
```csharp
// GOOD
_logger.LogDebug("Debug message");
```

### Logging Run IDs
Always include run ID in simulation logs:
```csharp
_logger.LogInformation("Started executing simulation run {ID}", runItem.Run.Id.ToString());
```

## Async/Await Patterns

### Correct Usage
- Use `async`/`await` for all I/O operations
- Suffix async methods with `Async`
- Use `ConfigureAwait(false)` in library code
- Handle cancellation with `CancellationToken` parameters

### Don't Ignore Async Warnings
Address "call is not awaited" warnings. Either await the call or explicitly handle fire-and-forget:
```csharp
// GOOD - awaited
await Task.Run(() => ProcessAsync());
```

### Use Async Return Types Correctly
If a method doesn't return anything, use `Task` not `Task<T>`:
```csharp
// GOOD - if no meaningful return value
public static async Task UpdateLogsBatch(...) { }
```

## Null Handling

### Avoid Defaulting to Magic Values
Don't default nullable values to 0 or other magic numbers. Use nullable types:
```csharp
// GOOD - use nullable types
long? logId = logIdParam;
if (logId.HasValue)
{
    // use logId.Value
}
```

### Use Proper Null Checks
Use `string.IsNullOrEmpty()` for strings and `.HasValue` for nullable types:
```csharp
// GOOD
if (!string.IsNullOrEmpty(logId))
if (granularity.HasValue)
```

### Nullable Reference Types
- Use nullable reference types (`string?`)
- Check for null with `ArgumentNullException.ThrowIfNull()`
- Use the null-conditional operator (`?.`) and null-coalescing operator (`??`) appropriately
- Avoid returning `null` from public methods when possible

## Code Cleanup

### Remove Commented Code
Remove commented-out code instead of leaving it in the codebase. Use version control history if needed.

### Remove Unused Code
- Delete unused imports, variables, parameters, and methods
- If code is not called from anywhere, remove it or implement the call

### Clean Up TODO Comments
Either address TODOs or remove them. Create JIRA tickets and reference them if work is deferred:
```csharp
// GOOD - with ticket reference
// TODO: [SIM-1234] Implement caching for model files

// BEST - just do it or remove if no longer relevant
```

### Don't Commit Debug Code
Remove temporary debug code before committing:
- Remove `Console.WriteLine`
- Remove commented-out code used for testing
- Remove hardcoded test values

## API Design & Usage

### Add Limit Parameters to API Queries
Always add `Limit` parameter to API queries to prevent fetching excessive data:
```csharp
// GOOD
await client.ListRoutineRevisionsAsync(new RoutineRevisionQuery { Limit = 100 });
```

### Use Constants Instead of Magic Numbers
Extract magic numbers to named constants:
```csharp
// GOOD
private const int LogBatchChunkSize = 100;
var chunkSize = LogBatchChunkSize;
```

### Use `this` Keyword Sparingly
Don't use `this` keyword unless necessary for disambiguation:
```csharp
// GOOD
_logger.LogInformation("Message");
```

## Configuration

### Validate Configuration Values
Check for null configuration values that could cause null pointer exceptions:
```csharp
// GOOD
if (config.CdfRetries == null)
{
    throw new ConfigurationException("CdfRetries configuration is required");
}
var retries = config.CdfRetries.MaxRetries;
```

## File Operations

### Use Proper File Stream Parameters
Specify file access and sharing modes explicitly:
```csharp
// GOOD - explicit parameters
new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true)
```

## Testing Conventions

### Unit Tests
- Use xUnit framework
- Test class per tested class
- Test method naming: `MethodName_Scenario_ExpectedResult`
- Use `Fact` for general tests, `Theory` for parameterized tests
- Arrange-Act-Assert pattern

### Integration Tests
- Mark with `[Trait("Category", "Integration")]`
- Handle external dependencies appropriately
- Clean up test data

### Mocking
- Use Moq framework
- Mock only external dependencies
- Verify interactions when testing behavior

### Use Thread-Safe Collections in Tests
When tests run concurrently, use `ConcurrentDictionary` instead of `Dictionary`:
```csharp
// GOOD
private ConcurrentDictionary<string, int> _callCounts = new();
```

### Clean Up Test Comments
Remove obvious comments that just repeat the code:
```csharp
// GOOD - only add comments that provide additional context
// Result should contain the newly created simulator ID
Assert.NotNull(result);
```

### Assert Specific Values
Use specific assertions over generic ones:
```csharp
// GOOD
Assert.NotEmpty(logData);
Assert.NotNull(logData.First().Message);
```

## Dependency Injection

### Inject Dependencies Correctly
Don't build service providers manually in production code. Use proper DI patterns:
```csharp
// GOOD - inject via constructor
public MyClass(IMyService service)
{
    _service = service;
}
```

## Common Patterns in This Repository

### Connector Pattern
```csharp
public abstract class ConnectorBase<TConnectorConfig, TSimulationResult>
{
    // Common connector lifecycle methods
    protected abstract Task<TSimulationResult> RunSimulationAsync(...);
}
```

### State Management
- Use `ModelStateBase` for tracking model state
- Use `FileState` for file operations
- Store state in CDF (Cognite Data Fusion)

### Library Base Classes
- `ModelLibraryBase`: Manages simulator models
- `RoutineLibraryBase`: Manages simulation routines
- `SimulationRunnerBase`: Coordinates simulation execution

### DateTime Handling
Always use UTC for timestamps:
```csharp
// GOOD
DateTime.UtcNow
```

## Code Analysis & Quality

### EditorConfig
Follow the `.editorconfig` settings in the repository root. Key rules:
- `indent_size = 4`, `indent_style = space`
- `dotnet_style_qualification_for_method = true` (use `this.`)
- Various CA rules configured for library-specific needs

### Suppressed Warnings
Some code analysis rules are disabled for specific reasons:
- CA1707: Allow underscores (for backward compatibility)
- CA1050: Allow types in global namespace (flexibility for connectors)
- CA1000: Allow static members on generic types (simpler APIs)

## Git & Version Control

### Commit Messages
- Use conventional commits format
- Prefix with type: `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`
- Keep first line under 50 characters
- Provide details in body

### Branching
- `main`: Production-ready code
- Feature branches: `feature/description`
- Fix branches: `fix/issue-description`
- Release branches: `release/x.y.z`

### Keep PRs Focused
Break large changes into smaller, focused PRs:
- Aim for under 400 lines of non-generated code changes
- Separate refactoring from feature additions
- Formatting changes should be in their own PR

## CI/CD Considerations

### Use Matrix Builds for Multiple Platforms
When testing on multiple OS platforms, use matrix strategy:
```yaml
runs-on: ${{ matrix.os }}
strategy:
  matrix:
    os: [ubuntu-latest, windows-latest]
```

### Pin Dependency Versions
Lock versions of tools and dependencies for reproducible builds.

## References
- [C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- [EditorConfig](https://editorconfig.org/)
- [.NET Design Guidelines](https://docs.microsoft.com/en-us/dotnet/standard/design-guidelines/)
- Repository `.editorconfig` for project-specific rules
- `.github/copilot-instructions.md` for project-specific review guidelines
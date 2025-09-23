# Change Log

All notable changes to this project will be documented in this file.

## Release v1.0.0-beta-023 (2025-09-23)

### Features

* **Connector Startup Improvement**: Add a 1-minute memory cache to avoid redundant API calls when checking if locally stored model revisions are up-to-date during connector startup, preventing rate limiting issues.  
* **Model Library**: Concurrent chunking support for retrieving metadata of 1000+ model files
* **Routine Library**: Store only essential routine fields in memory and fetch full data on-demand to prevent OOM issues

### Other changes

* Use long IDs directly instead of string keys for routine revision item references

## Release v1.0.0-beta-022 (2025-08-26)

### Bug Fixes

* **Model dependencies**: Fixed the issue where the connector didn't update the `ModelDependencies` property of the connector on startup.
* Fixed the issue where "Downloaded" property of the model dependencies was persisted into StateStore.

## Release v1.0.0-beta-021 (2025-08-20)

### Features

* **Improved Model Library Performance**: State is now restored from local cache on startup, avoiding redundant model processing
* **File Download Deduplication**: Models sharing the same files no longer re-download duplicates, significantly improving performance for multi-file models

### Other changes

* Removed unused properties from model state for cleaner codebase


## Release v1.0.0-beta-020 (2025-08-13)

### Features

* Model file dependencies support [experimental]
    - The model file can contain dependencies to other model files. Model extraction logic will automatically download these dependencies and store them in the model state.
    - This is an experimental feature, and future releases will include patches / improvements for it, e.g. deduplication on download.
* Support for original file extension in model library.
    - The model file extension is now determined by the file name, rather than the first item in the list of supported file extensions for a given simulator.

### Bug Fixes
* Improved the robustness of the model library and refactored the code to make it easier to maintain and debug.
    - Removed the temporary state for model revisions, which simplifies the logic and reduces the risk of errors.
    - Fixed issues with accessing model revisions by external ID, especially when models are deleted and re-uploaded.

## Release v1.0.0-beta-019 (2025-06-05)

### Bug fixes

* Multiple fixes for model revision data support (previous release)
    - Fixed JSON conversion for `SimulatorModelRevisionDataConnectionType`
    - Fixed `SimulatorModelRevisionDataGraphicalObject`: changed properties with `float32` types to `double`.
    - [Breaking] Renamed `SimulatorValueUnitQuantity` to `SimulatorValueUnitReference`

## Release v1.0.0-beta-018 (2025-05-12)

### Features

* Add support for storing model revision data in SimInt API.

## Release v1.0.0-beta-017 (2025-04-28)

### Features

* Support model revision file size up to 32GB (previously 8GB). This is theoretical limit, as the actual limit is determined by the available memory, disk space and the performance of the system. Stricter limits will be introduced on the API level.

## Release v1.0.0-beta-016 (2025-03-04)

### Bug Fixes

* Upgraded dependencies to fix the issue with the connector not being able to run without `Microsoft.Bcl.AsyncInterfaces` package.
* Fixed bug introduced in v1.0.0-beta-015 where disabled extraction pipeline would cause null reference exceptions.

### Other

* Numerous code improvements and refactoring.

## Release v1.0.0-beta-015 (2025-03-06)

### Features

* **Breaking** `TerminateProcess` flag under the `AutomationConfig` section was removed. Since termination logic is unique to each connector this can be implemented by each connector.
* Fixed bug where invalid config file led to connector running in a loop displaying connection error.
* Introduced support for license tracking and holding.
* Introduced support for Process termination.

## Release v1.0.0-beta-014 (2025-03-05)

### Bug Fixes

* Resolved an issue where the connector failed to initialize the extraction pipeline on startup, causing false alerts indicating the pipeline was offline. Added support for late initialization of the extraction pipeline in case of such failures.

## Release v1.0.0-beta-013 (2025-02-17)

### Features

* **Breaking** added a method on the `ISimulatorClient` called `TestConnection`. 
* Wrapped some more areas of the code with Try-Catch statements to enhance stability.
* Increased default value for retry config of SDK based API calls.

## Release v1.0.0-beta-012 (2025-02-06)

### Features

* **Breaking** Removed redundant `simulator` configuration section from the configuration file, move the `data-set-id` configuration property under the `connector` section.

## Release v1.0.0-beta-11 (2025-02-03)

### Features

* Introduce `connector.simulation-run-tolerance` configuration option to prevent simulation runs pile-up. The connector will time out simulation runs that are older than this value (in seconds).

## Release v1.0.0-beta-010 (2024-12-06)

### Features

* Support model revision file size up to 8GB (previously 2GB). This is theoretical limit, as the actual limit is determined by the available memory, disk space and the performance of the system.

## Release v1.0.0-beta-009 (2024-11-22)

### Bug Fixes

* Improve the scalability of the connector by sending fewer requests to the routine revision list endpoint.


## Release v1.0.0-beta-008 (2024-11-01)

### Features

* **Breaking** deleted `model-library.state-store-interval` configuration option. Use `model-library.library-update-interval` instead.
* Increased default refresh interval for the model library and routine library to 10 seconds.


## Release v1.0.0-beta-007 (2024-10-29)

### Features

* Increased the default timeout for the soft restart of the connector to 10 seconds after the failure.
* More debug logs on simulation routine base methods.

### Bug Fixes

* Fixed the issue where error during startup was being swallowed and connector would not be re-started after.


## Release v1.0.0-beta-006 (2024-10-25)

### Features

* Added a possibility of overridding the minimum log level for each simulation run

## Release v1.0.0-beta-005 (2024-10-10)

### Bug Fixes

* Fix the target framework to be netstandard2.0 to support older .NET versions (introduced in v1.0.0-beta-003)


## Release v1.0.0-beta-004 (2024-10-10)

### Bug Fixes

* Fix the scheduler issue where the simulation was not getting correct run time when being executed in non-UTC time zone environments.
* Fix the issue which caused the connector with enabled extraction pipeline to get stuck in a loop upon any API error.


## Release v1.0.0-beta-003 (2024-10-03)

### Chore

* Bump .NET SDK version to 8.
* Bump multiple dependencies.


## Release v1.0.0-beta-002 (2024-10-01)

### Bug Fixes

* Fix the issue where the remote (API) logger didn't respect the minimum log level setting.

### Features

* **Breaking Change** - Remote logger config moved to the `logger.remote` section in the configuration file, instead of being in the `connector.api-logger` section.


## Release v1.0.0-beta-001 (2024-09-11)

### Features

* Initial release to beta.

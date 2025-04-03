# Change Log

All notable changes to this project will be documented in this file.

## Release v1.0.0-beta-016 (2025-04-03)

* Fixes a missing dependency (Microsoft.Bcl.AsyncInterfaces) that would cause a runtime error on connectors built using this package. 

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

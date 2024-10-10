# Change Log

All notable changes to this project will be documented in this file.



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

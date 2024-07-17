## [Release v1.0.0-alpha-018] - 2023-12-18
- **[BREAKING]** `api-enabled` value now defaults to true and cannot be changed via the configuration file for any simulator
- Heartbeat now has the value APIEnabled = true by default
- Connector Version and Simulator Version now default to `N/A` if not found

## [Release v1.0.0-alpha-017] - 2023-12-15
- **[BREAKING]** `ConnectorBase` Class method `EnsureSimulatorIntegrationsSequencesExists` renamed to `EnsureSimulatorIntegrationsExists`
- We are now using Simulator API Integration endpoint to store integration data (simulator status) instead of`CDF Sequences`
- Updated Cognite.Extensions package to `1.17.1`

## [Release v1.0.0-alpha-016] - 2023-12-13
- **[BREAKING]** Started using new Simulator API based `SimulationRuns` instead of listening for CDF Events to start Simulation Runs. 
- Increased the allowed status text in Runs from 100 characters to 255

## [Release v1.0.0-alpha-015] - 2023-12-11
- Store files downloaded from CDF under a folder by the `CDF ID` This allows all simulation generated files to live under that folder too. 
- Upon model update / deletion , the entire folder of the resource gets deleted, deleting everything under it. 

## [Release v1.0.0-alpha-014] - 2023-11-28
- Connectors can now have their config driven from extraction pipelines by specifying a `pipeline-id` in the config and providing the connection credentials. Example:

```
cognite:
  project: cognite-simulator-integration
#  project: charts-demo
  host: "https://westeurope-1.cognitedata.com"
  idp-authentication:
    tenant: tenant-id 
    client-id: client-id 
    secret: secret
    scopes:
      - "https://westeurope-1.cognitedata.com/.default"
  extraction-pipeline:
    pipeline-id: symmetry-extraction-pipeline
```
## [Release v1.0.0-alpha-013] - 2023-10-12
- Add support for reporting the connector status (running_calculation, idle etc) to CDF.

## [Release v1.0.0-alpha-012] - 2023-09-18

## [Release v1.0.0-alpha-011] - 2023-06-29

## [Release v1.0.0-alpha-010] - 2023-06-16

## [Release v1.0.0-alpha-009] - 2023-06-01

## [Release v1.0.0-alpha-008] - 2023-05-15

## [Release v1.0.0-alpha-007] - 2023-03-31

## [Release v1.0.0-alpha-006] - 2023-03-01

## [Release v1.0.0-alpha-005] - 2022-12-16

## [Release v1.0.0-alpha-004] - 2022-12-02

## [Release v1.0.0-alpha-003] - 2022-10-21

## [Release v1.0.0-alpha-002] - 2022-10-14

## [Release v1.0.0-alpha-001] - 2022-09-08


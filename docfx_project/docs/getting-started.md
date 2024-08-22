# Getting Started


Create a ```config.yml``` file containing the simulator configuration

```yaml
version: 1

logger:
    console:
        level: "debug"

cognite:
    project: ${COGNITE_PROJECT}
    # This is for microsoft as IdP, to use a different provider,
    # set implementation: Basic, and use token-url instead of tenant.
    # See the example config for the full list of options.
    idp-authentication:
        # Directory tenant
        tenant: ${COGNITE_TENANT_ID}
        # Application Id
        client-id: ${COGNITE_CLIENT_ID}
        # Client secret
        secret: ${COGNITE_CLIENT_SECRET}
        # List of resource scopes, ex:
        # scopes:
        #   - scopeA
        #   - scopeB
        scopes:
          - ${COGNITE_SCOPE}


simulator:
  name: "NEWSIMULATOR"
  data-set-id: ${COGNITE_DATA_SET_ID}
    
connector:
  name-prefix: "test-connector@"

automation:
  program-id: "COMDLLExample.MyCOMClass"
```

Create a simulator connector by subclassing the `ISimulatorClient<YourModelFilestate, SimulatorRoutineRevision>` class:
    
```csharp
public class YourSimulatorClient : 
        ISimulatorClient<YourModelFilestate, SimulatorRoutineRevision> {

    
    // TBD
}
```

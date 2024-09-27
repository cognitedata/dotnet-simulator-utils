# Error handling and logging

Use the `ILogger` interface for error handling and logging throughout your connector. 

You can access the logger in the `ISimulatorClient` as shown below:

```csharp
_logger.LogDebug("Insert your message here");
```

### Testing
To ensure the connector works as expected, create unit tests.

Follow the above steps to create a custom connector for your simulator that integrates with the Cognite simulator integration framework. Adjust the code examples to fit the specific simulator requirements and handle any simulator-specific operations in the `NewSimRoutine` and `NewSimClient` classes.
<a href="https://cognite.com/">
    <img src="https://user-images.githubusercontent.com/6639002/198020263-af95e1ec-6494-4f17-b359-339b65cf9b04.png#gh-dark-mode-only" alt="Cognite logo" title="Cognite" align="right" height="50" />
</a>

Cognite Simulator Integration Utils
=======================
[![.NET build and test](https://github.com/cognitedata/dotnet-simulator-utils/actions/workflows/buildandtest.yml/badge.svg?branch=main)](https://github.com/cognitedata/dotnet-simulator-utils/actions/workflows/buildandtest.yml)
[![Publish](https://github.com/cognitedata/dotnet-simulator-utils/actions/workflows/publish.yml/badge.svg)](https://github.com/cognitedata/dotnet-simulator-utils/actions/workflows/publish.yml)
[![codecov](https://codecov.io/gh/cognitedata/dotnet-simulator-utils/branch/main/graph/badge.svg?token=sr1aNhkc1e)](https://codecov.io/gh/cognitedata/dotnet-simulator-utils)
[![Nuget](https://img.shields.io/nuget/vpre/Cognite.Simulator.Extensions)](https://www.nuget.org/packages/Cognite.Simulator.Extensions/)

Utilities for developing simulator integrations within CDF. This contains extendable common utilities that can be used by simulator connectors to interact with CDF APIs and the simulator. 

# Getting started 

To build a new connector based upon these utilities, just add a reference to this library in your project's csproj file:

`<PackageReference Include="Cognite.Simulator.Utils" Version="1.0.0-alpha-021" />` (Locks to version 1.0.0-alpha-021)

[Latest version can be obtained from Nuget](https://www.nuget.org/packages/Cognite.Simulator.Utils/)

# For local development

Clone this repo locally then add a reference to a local version of this repository to your `csproj` file:

````
  <ItemGroup>
      <ProjectReference Include="../../dotnet-simulator-utils/Cognite.Simulator.Utils/Cognite.Simulator.Utils.csproj" />
  </ItemGroup>
````

After this run `dotnet build` in your project folder. Next start extending existing utilities in your project. [Use the DWSIM project as an example](https://github.com/cognitedata/dwsim-connector-dotnet).

# Running Tests

Create an `.envrc` file containing the CDF connection credentials. The file's format should be :

````
export COGNITE_HOST="" #Example: https://westeurope-1.cognitedata.com
export COGNITE_PROJECT="" #Example: cognite-simulator-integration
export AZURE_TENANT=""
export AZURE_CLIENT_ID=""
export AZURE_CLIENT_SECRET=""

````

Then run a `source .envrc` to load the environment variables into your current terminal session

Finally, run `dotnet test`

# Code of Conduct

This project follows https://www.contributor-covenant.org

## License

Apache v2, see [LICENSE](https://github.com/cognitedata/dotnet-simulator-utils/blob/master/LICENSE).

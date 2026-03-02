# Unfolded Circle Electrolux Integration Driver

[![Release](https://img.shields.io/github/actions/workflow/status/henrikwidlund/unfoldedcircle-electrolux/github-release.yml?label=Release&logo=github)](https://github.com/henrikwidlund/unfoldedcircle-electrolux/actions/workflows/github-release.yml)
[![CI](https://img.shields.io/github/actions/workflow/status/henrikwidlund/unfoldedcircle-electrolux/ci.yml?label=CI&logo=github)](https://github.com/henrikwidlund/unfoldedcircle-electrolux/actions/workflows/ci.yml)
![Sonar Quality Gate](https://img.shields.io/sonar/quality_gate/henrikwidlund_unfoldedcircle-electrolux?server=https%3A%2F%2Fsonarcloud.io&label=Sonar%20Quality%20Gate&logo=sonarqube)
[![Qodana](https://img.shields.io/github/actions/workflow/status/henrikwidlund/unfoldedcircle-electrolux/qodana_code_quality.yml?branch=main&label=Qodana&logo=github)](https://github.com/henrikwidlund/unfoldedcircle-electrolux/actions/workflows/qodana_code_quality.yml)
[![Docker](https://img.shields.io/github/actions/workflow/status/henrikwidlund/unfoldedcircle-electrolux/docker.yml?label=Docker&logo=docker)](https://github.com/henrikwidlund/unfoldedcircle-electrolux/actions/workflows/docker.yml)

This repository contains the server code for hosting an integration driver that can control Electrolux Air Purifiers on the Unfolded Circle Remotes.
It exposes a Climate Entity and a Select Entity.

### Prerequisites
- API Key and Refresh Token from Electrolux (https://developer.electrolux.one)

### Running

- The published binary is self-contained and doesn't require any additional software.
It's compiled for Linux ARM64 and is meant to be running on the remote.
- Use the [Docker Image](https://hub.docker.com/r/henrikwidlund/unfoldedcircle-electrolux) in the [Core Simulator](https://github.com/unfoldedcircle/core-simulator)
- Other Operating Systems - Linux, macOS, Windows - are supported.

\* Server port can be adjusted by specifying the desired port with the `UC_INTEGRATION_HTTP_PORT` environment variable.

### Development

- [dotnet 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- or [Docker](https://www.docker.com/get-started).

## Installing on the remote

1. Download `unfolded-circle-electrolux-[version]-remote.tar.gz` from the release page
2. Open the remote's Web Configurator
3. Click on `Integrations`
4. Click on `Add new` and then `Install custom` 
5. Choose the file in step 1 (`unfolded-circle-electrolux-[version]-remote.tar.gz`)
6. Make sure that your device is turned on
7. Click on the newly installed integration and follow the on-screen instructions

## Configuration

The application can be configured using the `appsettings.json` file or environment variables.
Additionally, the application saves configured entities to the `configured_entities.json` file, which will be saved to the directory specified by the `UC_CONFIG_HOME` environment variable.

## Logging

By default, the application logs to stdout. 
You can customize the log levels by either modifying the `appsettings.json` file or by setting environment variables.

### Log levels
- `Trace`
- `Debug`
- `Information`
- `Warning`
- `Error`

`Trace` log level will log the contents of all the incoming and outgoing requests and responses. 

### `appsettings.json`

```json
{
    "Logging": {
        "LogLevel": {
          "UnfoldedCircle.Server": "Information",
          "UnfoldedCircle.Electrolux": "Information",
          "Makaretu.Dns": "Warning"
        }
    }
}
```

### Environment variables

Same adjustments to log levels can be made by setting environment variables.
- `Logging__LogLevel__UnfoldedCircle.Server` = `Information`
- `Logging__LogLevel__UnfoldedCircle.Electrolux` = `Information`
- `Logging__LogLevel__Makaretu.Dns` = `Warning`

## Building from source code

### Building for the remote

Execute `publish.sh` script to build the application for the remote. This will produce a `tar.gz` file in the root of the repository.

### Building for Docker

Execute the following from the root of the repository:

```sh
docker build -f src/UnfoldedCircle.Electrolux/Dockerfile -t electrolux .
```

### dotnet CLI

```sh
dotnet publish ./src/UnfoldedCircle.Electrolux/UnfoldedCircle.Electrolux.csproj -c Release --self-contained -o ./publish
```

This will produce a self-contained binary in the `publish` directory in the root of the repository.

## Licenses / Copyright

- [License](LICENSE)
- [richardschneider/net-dns](https://github.com/richardschneider/net-dns/blob/master/LICENSE)
- [richardschneider/net-mdns](https://github.com/richardschneider/net-mdns/blob/master/LICENSE)
- [jdomnitz/net-dns](https://github.com/jdomnitz/net-dns/blob/master/LICENSE)
- [jdomnitz/net-mdns](https://github.com/jdomnitz/net-mdns/blob/master/LICENSE)

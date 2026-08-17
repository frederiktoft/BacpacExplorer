# BACPAC Explorer

A lightweight .NET 10 WPF application for inspecting SQL Server `.bacpac` files without importing them into SQL Server.

## Current capabilities

- Opens and validates a local BACPAC with Microsoft DACFx.
- Uses the supported `TSqlModel` API to discover schemas, tables, and columns.
- Displays SQL types including length, precision, scale, and `max` arguments.
- Displays column nullability.
- Filters tables by schema name, table name, or column name.
- Reads packages asynchronously so the WPF UI remains responsive.
- Reports malformed or unsupported packages without crashing.
- Imports table data on demand into a uniquely named, disposable SQL Server LocalDB database.
- Browses rows in bounded pages of up to 1,000 rows with safely quoted identifiers.
- Provides separate Explorer and Query workspaces.
- Executes one read-only `SELECT` query at a time with cancellation, a 30-second timeout, and a 1,000-row ceiling.
- Validates query syntax with Microsoft ScriptDom and executes under a generated database user granted only `SELECT`.
- Removes the temporary database when another package is opened or the application closes.
- Removes leased temporary databases left by an earlier crashed application instance without touching databases owned by running instances.
- Never modifies the BACPAC.
- Writes focused diagnostic logs for errors, exceptions, and a small number of major lifecycle events.

A BACPAC is not itself a query engine, and DACFx does not expose its serialized rows as a supported enumerable API. BACPAC Explorer therefore imports the package only when Browse Data is first requested, then uses generated read-only `SELECT` statements against LocalDB. The import can take several seconds but is reused while the package remains open.

## Runtime requirements

- 64-bit Windows
- .NET 10 Desktop Runtime
- SQL Server Express LocalDB with the automatic `MSSQLLocalDB` instance for row browsing and queries

Schema exploration does not use LocalDB, but it is an application prerequisite for the complete feature set. BACPAC data imported for browsing is temporarily stored in LocalDB under the current Windows user's profile. It is deleted on normal shutdown; a later application run removes managed databases left by a crash.

## Build and run

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project .\src\BacpacExplorer.App\BacpacExplorer.App.csproj
```

Building from source additionally requires the .NET 10 SDK.

## Portable distribution

Run the packaging script from PowerShell:

```powershell
.\scripts\Publish-Portable.ps1
```

This performs a framework-dependent, untrimmed, single-file `win-x64` Release publish and creates `artifacts\BacpacExplorer-win-x64.zip`. The ZIP is used to reduce download size; after extraction it contains `BacpacExplorer.exe`, with embedded diagnostic symbols, and the project license. Native database libraries are bundled into the executable and extracted by .NET when required. The portable profile includes English resources only, avoiding the large unused satellite-resource assemblies shipped by database dependencies. When another application translation is added, include its culture in `SatelliteResourceLanguages` in the publish profile. Users extract the ZIP to a writable folder and run `BacpacExplorer.exe`; no installer or administrator privileges are required by BACPAC Explorer itself. The declared .NET and LocalDB prerequisites must already be installed.

The application can also run from a protected installation folder. In that case logs fall back to the current user's LocalAppData folder. Application updates that replace the portable directory should preserve `Logs` when existing diagnostic history is wanted.

## Structure

```text
src/
  BacpacExplorer.Core/
    Bacpac/       BACPAC validation and DACFx model inspection
    Database/     Disposable LocalDB import lifecycle
    Models/       UI-independent database metadata
    Querying/     Bounded row pages and validated read-only query execution
    Sql/          SQL identifier utilities
  BacpacExplorer.App/
    MainWindow    WPF schema browser
tests/
  BacpacExplorer.Core.Tests/
```

`BacpacExplorer.Core` has no WPF dependency, so its package and database functionality can be reused by another UI later.

## Localization

English is currently the only included language. User-facing text is stored in neutral `.resx` resources rather than embedded in the UI:

- `src/BacpacExplorer.App/Localization/AppText.resx` contains WPF labels, status text, and dialog copy.
- `src/BacpacExplorer.Core/Localization/CoreText.resx` contains validation, package, and LocalDB messages.

To add a translation later, copy each neutral file using a culture suffix such as `AppText.da.resx` and `CoreText.da.resx`, then translate the values without changing the keys or placeholders. The .NET build will generate culture-specific satellite assemblies automatically and select them using `CurrentUICulture`. No language selector is currently included.

## Logging

Serilog writes human-readable rolling log files to a `Logs` folder beside the executable, keeping portable ZIP deployments self-contained. If that location is not writable, the application falls back to `%LOCALAPPDATA%\BacpacExplorer\Logs` without requiring administrator privileges. Files roll daily and at 10 MB, and the latest 14 files are retained. Shared-file mode allows multiple application instances to write safely. Exceptions include their messages, stack traces, and inner exceptions.

Logging is intentionally focused on failures. Informational entries cover application startup and shutdown, opening a package, LocalDB preparation, and completed queries. SQL text, query results, connection strings, and full BACPAC paths are not logged.

## License

Copyright © 2026 Frederik Rex Rytter Toft.

BACPAC Explorer is free and open-source software licensed under the [GNU General Public License version 3 only](LICENSE) (`GPL-3.0-only`). Distributed modifications must preserve the applicable copyright and license notices, identify their changes, and make the corresponding source available under the same license.

The license does not grant permission to use the author’s name or the BACPAC Explorer name and branding to imply endorsement by or affiliation with the original project. Forks may identify their origin but should clearly distinguish themselves from the original project.

# CRM Manager for XrmToolBox

An initial, separate native WinForms tool for **XrmToolBox 1.2025.10.74** and **on-premises CRM 9.1 with Active Directory authentication**. The standalone WPF application remains a separate executable.

Author: **Sanan Baig**. The assembly company metadata supplies XrmToolBox's displayed author; NuGet author metadata uses the same name.

## Build

Requires Windows, .NET Framework 4.8 targeting tools, a current .NET SDK, and an installed XrmToolBox host:

```powershell
dotnet build C:\BAJPluginManager\src\CRMManager.XrmToolBox\CRMManager.XrmToolBox.csproj --configuration Release -p:XrmToolBoxPath=C:\Tools\XrmToolBox
```

This initial project references the installed host assemblies with `Private=false`. It compiles the existing Core source files as linked source, rather than copying business logic or shipping a second SDK/connector. Shared source fixes therefore apply to both editions; each is compiled against its own host dependencies. The icon is embedded.

The output is `bin\Release\net48\CRMManager.XrmToolBox.dll`. **Only this DLL is needed for local tool installation**; the `.pdb` is optional. Do not copy the standalone app or SDK/connector DLLs into XrmToolBox.

## Local installation

Close XrmToolBox before installing or updating the tool. For the standard user storage location:

```powershell
Copy-Item C:\BAJPluginManager\src\CRMManager.XrmToolBox\bin\Release\net48\CRMManager.XrmToolBox.dll `
    "$env:APPDATA\MscrmTools\XrmToolBox\Plugins\CRMManager.XrmToolBox.dll"
```

For custom XrmToolBox storage, use its configured `Plugins` folder instead. Restart XrmToolBox, search for **CRM Manager** in the Tools list, and open it. Connect using **XrmToolBox's connection manager**; this tool has no credential form and does not store passwords.

The tool can open without a connection. CRM actions use the host's connection selection flow. The tool validates the host's Active Directory authentication and CRM 9.1 server-version metadata locally, then creates its own organization service proxy using the host proxy's service management, an in-memory copy of its client credentials, and the same caller identity. This avoids waiting on the shared connector lock while XrmToolBox downloads its metadata cache. No new credential form or password storage is introduced. Missing or unsupported version metadata is an error: reconnect in XrmToolBox rather than bypassing validation. After a connection change, loaded lists are cleared and the host metadata is checked again. If an operation is still running, wait for it to finish before starting another.

For an offline UI check, open without connecting and decline any host connection prompt. Opening the tool with an existing host connection sends no additional identity/version requests. Component lists and write actions are not run automatically.

## First edition features

- Lazy assembly > type > step > image tree, details and branch refresh.
- Signed assembly/type registration and compatible assembly DLL updates.
- Step creation/editing using CRM-sourced messages and entity filters; standard stages, sync/async, order, filtering attributes, description, unsecure configuration and async job deletion.
- Image creation/editing and step enable/disable, preserving the shared server-side guards.
- Existing customizable web-resource upload, unpublished text editing with encoding preservation and stale-content comparison, and separate selective publishing.
- Managed/unmanaged solution export, including optional confirmed permanent solution-version updates before export.
- Asynchronous CRM requests, visible operation logs, confirmations naming the target organization, and local editor recovery after save failures.

The host owns its `IOrganizationService`; disconnecting or disposing this tool does **not** dispose or modify the host's service. CRM Manager owns and disposes only its independent proxy, after its in-flight operation finishes. Its SDK request timeout uses the host's configured timeout (two minutes if unset), and the tool logs that timeout when the channel is created. Connection generation checks prevent results from an old connection repopulating the new workspace and stop subsequent writes after a switch. An already submitted server request cannot be canceled by switching or closing the tool. Wait for operations to finish and verify server state after a timeout before retrying.

SDK channel construction runs in the background rather than inside the host's connection/UI callback. Setup is limited to 30 seconds; failure restores the controls and reports an error. A channel that finishes creation after timeout, closing, or connection switching is discarded and disposed.

## Scope and limitations

- Development use is an operator responsibility, not an enforced environment-tier detector.
- This edition is a first functional port, not UI parity with the standalone application. It uses native tabs and toolbars; local search/counts, richer contextual action enablement and additional editor polish are not yet ported.
- Workflow registration, unregister/delete, secure-configuration editing and run-as selection remain unsupported.
- Text editing has no syntax highlighting/validation or automatic compilation. Save is not publish. Concurrency comparisons are pre-write checks, not atomic server locks.
- Online Dataverse, IFD/claims connections, other CRM versions and other XrmToolBox versions have not been enabled or validated.
- Real connected operations and connection switching need development-CRM validation; offline tests are not proof of server behavior.
- This is a **local DLL installation**, not a public Tool Library release. Public publishing needs compatible NuGet packaging, author/license/project metadata, organization approval and XrmToolBox review. No package has been uploaded.

## Public publishing

1. Obtain any required organization approval, choose an authorized license, and review the distributable files for confidential code or data. A public source repository is optional, but provide a public project page with documentation and support information.
2. Test the supported on-premises CRM 9.1 scenarios in a dedicated development organization, including registrations, resource saves/publishing, exports, connection switching, and error handling. Clearly advertise that online Dataverse and IFD are unsupported.
3. Create a NuGet package, for example with NuGet Package Explorer. Choose an available package ID, title **CRM Manager**, author **Sanan Baig**, an aligned package/assembly version, description, release notes, license, project URL, your own publicly accessible icon URL, and the **XrmToolBox** tag.
4. Place only `CRMManager.XrmToolBox.dll` under `lib\net48\Plugins` in the package. Add the dependency **XrmToolBox**, minimum version **1.2025.10.74**, which is the host version currently tested. Do not bundle the host's SDK/connector assemblies. The project's default `dotnet pack` layout is not configured for Tool Library; do not upload it unchanged.
5. Publish the `.nupkg` to your NuGet.org account. Keep the API key private and out of source control. Wait for the package to be indexed.
6. Sign in to the [XrmToolBox tool registration page](https://www.xrmtoolbox.com/plugins/new/), submit the NuGet package ID, and wait for administrator review. NuGet publication alone does not put the tool in Tool Library.
7. For subsequent releases, increment the aligned package/assembly version, update release notes, and publish a new package version.

Refer to the official [publishing guide](https://www.xrmtoolbox.com/documentation/for-developers/deploy-your-plugin-in-plugins-store/) and [validation checklist](https://www.xrmtoolbox.com/documentation/for-developers/deploy-your-plugin-in-plugins-store/plugin-validation-check-list/). No public package or portal submission has been created by this project.

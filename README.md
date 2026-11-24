# CRM Manager for On-Premise

Windows desktop deployment helper for **Dynamics 365 Customer Engagement on-premises 9.1**, initially targeting server build **9.1.0023.0010**. Development organizations only; higher-environment deployment remains manual.

[![Build](https://github.com/Sanan01/CRM-Manager/actions/workflows/release.yml/badge.svg)](https://github.com/Sanan01/CRM-Manager/actions/workflows/release.yml)
[![Latest release](https://img.shields.io/github/v/release/Sanan01/CRM-Manager)](https://github.com/Sanan01/CRM-Manager/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## Download and install

1. Download the latest `CRMManager-vX.Y.Z.zip` from the [Releases page](https://github.com/Sanan01/CRM-Manager/releases/latest).
2. Extract the **entire zip** to a folder of your choice (for example `C:\Tools\CRMManager`). Do not run the exe from inside the zip.
3. Run `CRMManager.exe`.

Windows 10/11 already include .NET Framework 4.8. The app is unsigned, so Windows SmartScreen may warn on first run: choose **More info → Run anyway**.

## Requirements

- Windows with .NET Framework 4.8.
- For development: a current .NET SDK capable of building SDK-style net48 WPF projects, plus the .NET Framework 4.8 targeting pack (or Visual Studio with .NET desktop development).
- Network access to the internal CRM organization URL and explicit Active Directory credentials.
- CRM privileges to read components and perform the selected registration, customization, publishing, or export operation. Full-trust plugin registration can require Deployment Administrator privileges.
- NuGet connectivity for the initial package restore.

No CRM credentials, URLs, or organization data are bundled. Do not disable certificate validation to work around an internal certificate problem.

## SDK compatibility

`Microsoft.CrmSdk.CoreAssemblies` is deliberately pinned to **9.0.2.52**. Its SDK/proxy binary build is **9.2.49.6961**, matching the SDK shipped in the locally installed, working Plugin Registration Tool 9.1.0.199. The SDK binary was verified byte-for-byte against that installation. The package version, SDK binary version, and CRM server version use different numbering schemes.

The newer SDK initially used by this application had a different binary build. Matching the working tool's SDK removes that compatibility difference, but is not proof that the live discovery fault is resolved. XRM Tooling remains the supported NuGet release 9.1.1.103; its connector build differs from PRT's private build. The application logs both loaded client library versions at startup to make stale/mixed deployments visible. Validate discovery and connection against the development organization before changing the pinned SDK.

`System.Text.Json` is explicitly referenced at 6.0.11 to resolve the different 6.0.x assembly requirements in this SDK/Tooling combination without selecting an older assembly than a dependency requires.

## Build and run

```powershell
Set-Location C:\BAJPluginManager
dotnet build .\BAJPluginManager.sln --configuration Release
dotnet test .\tests\BAJPluginManager.Tests\BAJPluginManager.Tests.csproj --configuration Release
& .\src\BAJPluginManager.App\bin\Release\net48\CRMManager.exe
```

Distribute the **entire application output folder**, not just the executable.

The application is named **CRM Manager** (`CRMManager.exe`). Existing source folder, solution, and project identifiers remain `BAJPluginManager`; they do not affect the displayed application name.

An original blue connected-records icon is embedded in the executable and used by the main window and editors. The multi-resolution Windows icon is maintained in `src\BAJPluginManager.App\Assets\CrmManager.ico`.

## XrmToolBox edition

A separate initial native WinForms tool is available in [CRMManager.XrmToolBox](src/CRMManager.XrmToolBox/README.md). It builds against the installed XrmToolBox 1.2025.10.74 host, borrows its connection, and compiles the same Core sources without shipping replacement host SDK DLLs. The standalone app remains independent. See that project's README for local installation, supported features and current limitations; this is not yet a public Tool Library package.

## First use

1. Enter **Organization URL**: the organization base URL, such as `https://crm-server/DevelopmentOrg`, or the full Organization Service URL from CRM **Settings > Customizations > Developer Resources**. Include the organization path, not just a hostname, and do not paste a browser `main.aspx` URL. Scheme and optional port come from this URL.
2. Enter **Domain**, **Username**, and **Password** for your Active Directory account. Domain may be blank when Username is `DOMAIN\username` or `user@domain`. Do not mix a UPN username with a separate Domain.
3. Click **Connect to CRM**. The app always uses the supplied credentials, checks organization access with `WhoAmI`, verifies the organization unique name, and accepts only CRM 9.1. Check the connected organization name and URL before doing anything else.
4. On connection, the app opens **Plugins**. Choose **Plugins**, **Web resources**, or **Solution exports** in the sidebar, refresh only the list needed, select a component, and run an operation. Every write and export still requires confirmation with the connected organization visible.

There is no connection-method selector, organization-discovery list, current-Windows-account option, or development confirmation checkbox. Authentication remains Microsoft's XRM Tooling Active Directory connector; IFD/claims login and online Dataverse are outside this version.

The app is intended for development organizations, but does not detect or enforce the environment tier. Do not enter UAT/production URLs.

The password is masked, read through WPF's `SecurePassword`, and cleared from the input field on each connection attempt or when closing. The connected SDK client retains the in-memory credentials needed for its session; disconnecting or closing disposes it. Remembering credentials is optional and requires an explicit **Save connection** confirmation. Saved profiles use Windows DPAPI encryption for the current Windows account; credentials are never logged. Do not share passwords in chat or screenshots.

## Saved development connections

- Enter an optional **Connection name**, organization URL, domain, username and password. Choose **Save connection** and confirm to remember them. Saving checks the input format but does not authenticate or connect to CRM.
- Choose a named profile in **Saved connections** to fill the fields. The last selected saved profile is restored on startup; the app never connects automatically.
- Choose **New connection** to clear the form and enter another development environment. Use a unique name when saving a new profile. Editing an existing selected profile and saving replaces that profile after confirmation; unsaved changes are not persisted.
- Disconnect before switching profiles or changing connection fields. Editing the URL, domain or username clears a loaded saved password so it is not silently reused for a changed target/account. Selecting the saved profile again reloads its original fields.
- **Delete selected** forgets one local profile. **Forget all saved connections** removes the local saved file, including when it cannot be decrypted. Both require confirmation and do not modify CRM. An unreadable file is reported, not silently overwritten; manual connection remains available.

Profiles are stored outside the application folder at `%LOCALAPPDATA%\CRMManager\connections.dat`. The entire file is encrypted using DPAPI `CurrentUser`; profile passwords also remain encrypted in the loaded profile collection until selected. Passwords are decrypted only as needed for the masked WPF input and authentication. DPAPI protects storage, not a compromised Windows account or running process: someone using the same Windows account can access saved credentials. Do not save credentials on a shared Windows login.

Copying the application to another PC does not copy these profiles. They are not designed for transfer between PCs or Windows accounts; enter and save credentials separately there. Do not include the saved file in application distributions or public packages.

If connection fails with **The caller was not authenticated by the service** or **Access is denied**, compare the server/SSL/port/account settings with a successful Plugin Registration Tool login. These messages alone do not prove that the password is wrong. Verify VPN/network access, CRM access, and that this endpoint uses Active Directory rather than IFD/claims. **The provided URI did not return any Service Endpoints** can indicate the wrong organization/service URL or an endpoint/metadata access problem. Connection errors identify the stage (XRM Tooling authentication, `WhoAmI`, or version check); organization display metadata comes from XRM Tooling rather than an extra `organization` table read. Do not bypass TLS certificate errors.

## Workspace experience

- Fluent-inspired light surfaces, restrained blue accents, shared control styles, and persistent sidebar navigation. Connection status and operation feedback stay visible; detailed **Activity log** content expands on demand and automatically on errors.
- Connection has a saved-profile selector beside the form, with **Connection name**, **Organization URL**, **Domain**, **Username**, and masked **Password**. The cards wrap on narrower windows. Connect does not require a profile name; saving does. Account format and URL validation report errors explicitly.
- Workspaces can be previewed while disconnected, but CRM actions remain disabled and a **Go to connection** shortcut explains how to unlock them. Return to **Connection** to review organization details or disconnect.
- Search filters loaded lists locally, without additional CRM requests. Plugin search matches **assembly names only**, not unexpanded types or steps. Resource search includes name, display name, and type; solution search includes display/unique name and version. Counts distinguish not-loaded, empty, and filtered lists.
- Plugin actions sit beside selection details and are enabled for compatible selections only. Optional assembly registration and verbose activity details stay collapsed until needed.
- Registration editors group identity, message/entity, execution, and optional configuration. They explain asynchronous stage requirements and ask before discarding local changes. Existing CRM write confirmations and preservation rules are unchanged.
- The code editor shows local edit state, encoding, caret position, and character count. **Wrap lines** is optional; when enabled, the position describes the visual wrapped line. **Ctrl+S** saves unpublished content with confirmation. **Ctrl+Tab** moves focus out of the tab-accepting code area.

The interface uses standard WPF keyboard and accessibility support, with named inputs and visible focus indicators. It is a light theme, not a dependency on Windows App SDK, and does not add a theme switch or syntax-highlighting engine.

## Implemented

- Microsoft XRM Tooling Active Directory login using a known organization URL and explicit credentials, organization access/unique-name/version verification, staged connection diagnostics, and explicit disconnect.
- Paged unmanaged database-stored plugin assembly listing and DLL update. Identity checks require the same name, culture, signing key, and major/minor version. Existing steps and images are not recreated or changed. CRM may reject incompatible type removals/dependencies.
- New strong-name-signed assembly registration with manually entered fully qualified `IPlugin` type names and sandbox/full-trust selection. Assembly and type records are created in one server transaction. CRM validates the assembly and types. Expand the new assembly, select an IPlugin type, and use **New step** to register its execution steps explicitly.
- Lazy-loaded assembly > plugin type > step > image hierarchy, registration details, selected-node refresh, and read-only workflow activity display. Standard step creation/editing supports message/entity selection, Pre-validation/Pre-operation/Post-operation, synchronous/asynchronous execution, order, Update filtering attributes, description, unsecure configuration, and successful async-job deletion. Existing secure configuration, run-as identity, deployment, and state are preserved during edits. Enable/disable is a separate confirmed operation.
- Pre/post/both image creation/editing with name, alias, request property, and explicit attributes or deliberate all-attributes selection. Client validation prevents common invalid stage/message combinations; CRM validates message-specific support. Managed or non-customizable steps/images cannot be modified.
- Existing unmanaged customizable web resource listing and binary-safe upload. Extension validation prevents accidentally uploading TypeScript as JavaScript. Changes are compared against unpublished content; unchanged assembly/resource uploads are skipped.
- **Edit code** opens unpublished HTML/CSS/JavaScript/XML/XSL/SVG/RESX content in a resizable monospaced text editor with paste, tabs, and scrolling. Saving preserves UTF-8/UTF-16/UTF-32 encoding and BOM, skips identical content, and does not publish. Invalid/unsupported encodings and binary types are rejected.
- Explicit publishing of just the selected web resource. Publishing is separate from upload so an unchanged local file does not prevent publishing pending changes.
- Managed/unmanaged export of visible unmanaged custom solutions to a chosen ZIP file. Existing exports are replaced only after the new file is fully written. No automatic publish-all, solution import, or promotion.
- Non-blocking network operations, serialized SDK access, indeterminate progress, elapsed timing, and visible in-memory operation/error logs. Closing is blocked while an operation is running.

## Limitations and next increments

- Live login and assembly listing have been confirmed by the user's screenshot. New registration mutations and text-editor saves still require validation against the development CRM; offline tests validate SDK request shapes and guards, not server execution.
- Unregister/delete of assemblies, workflow activity types and individual images, secure-configuration editing, run-as user selection, offline/client deployment editing, automatic local plugin type discovery, adding types to existing assemblies, workflow activity registration, assembly identity upgrades, new web resource creation, saved file mappings, bulk selection, build integration, and persistent diagnostics are not implemented yet.
- Lists are loaded on demand, not automatically cached. Content comparison downloads the current selected component; it avoids unnecessary writes, not all network traffic. No speed improvement over the Plugin Registration Tool has been benchmarked.
- Solution exports use the published server state. Publish intended changes first. Plugin updates themselves do not require web resource publication.
- Operations have a ten-minute SDK timeout and no cancellation/retry automation. A timeout does not prove a server operation was rolled back: refresh and verify state before retrying.
- Explicit credentials support Active Directory Windows authentication, not an IFD/claims sign-in flow. There is no discovery UI, but the SDK may still use discovery internally when connecting by a known organization URL. Password persistence is opt-in through encrypted local saved profiles.
- Logs can contain server error messages and organization information. Review/redact them before sharing.

Tests cover endpoint validation, credential account formats, missing-credential rejection before network access, assembly identity protections, and upload type/content validation. The retained Core discovery helpers also have offline tests, but are not exposed by the app. Offline WPF regression checks exercise case-insensitive filtering, empty matches, cleared searches, selection removal, disconnected workspace navigation, the simplified connection form and required-input gating, layout fit, code edit/wrap feedback, and asynchronous-stage guidance without showing windows or making CRM calls.

Saved-profile tests use only fabricated credentials in isolated temporary files. They verify encrypted multi-profile round-trips, replacement and selection persistence, duplicate-name rejection, tamper errors, preservation of existing data on failed writes, startup prefilling without automatic login, clearing loaded passwords after identity changes, unsaved-edit isolation, and explicit recovery guidance for unreadable saved files.

## Changing a solution version before export

In **Solution exports**, select a solution and optionally enter **New version** as four non-negative whole numbers, such as `1.0.0.50`. Leave it blank to export without changing the version. The suggested ZIP filename uses the entered version.

The first **Selected** column shows a checkmark for the currently selected row, including when focus moves to export options. Clicking a row or its checkmark cell selects that solution; selection is single-row, not a bulk export list. The checkmark follows keyboard selection and clears when selection is removed.

After choosing the output path, the confirmation shows the old and new versions. Only after confirmation does the app update the unmanaged custom solution's version in development CRM, then export it. The version change is permanent: if export fails, the CRM version remains updated and the activity log records the change. Canceling either dialog makes no version update. The app rechecks the current version before updating and rejects stale list entries; this comparison is not an atomic concurrency guarantee. Refresh solutions if another developer changed the version.

## Plugin steps and images

After connecting, the app opens **Plugins**. Return to **Connection** in the sidebar to disconnect. Refresh assemblies and expand the arrows to load each level. Only the expanded branch is queried. **Refresh branch** reloads its children; full assembly refresh resets the tree. The assembly filter does not search unloaded registrations.

Select an IPlugin type for **New step**, or select an existing step/image for **Edit**. Messages and entity filters come from CRM registration metadata. A global/no-entity registration is explicit and can run for every entity supported by the message; do not choose it accidentally. Message, entity, and plugin type are fixed for an existing step in this version. To replace a step, register the new one deliberately and disable the old one as appropriate to avoid duplicate execution.

Select a step for **New image** or **Enable/Disable step**. Common Create/Update/Delete images default to the `Target` request property; other messages require the appropriate SDK property name. Image attribute names and Update filtering attributes are comma-separated logical names. Blank filtering attributes mean all updates. Blank image attributes are not accepted unless **All attributes** is explicitly selected. Moving a step to another stage is rejected if its existing images would become invalid.

Select the **step itself** for **Unregister step**. This permanently deletes that unmanaged, customizable step registration and its associated images through CRM's step deletion behavior, retaining its plugin type and assembly. The confirmation identifies the organization, step, message, stage, mode and ID; cancellation sends no delete request. The service rechecks editability and registration identity before deletion, then the UI refreshes the parent type. CRM permissions and dependency errors are surfaced rather than bypassed. Prefer disabling if you only want to stop execution; there is no undo in the app. This action is currently exposed in the standalone Windows app, not the XrmToolBox edition.

Select the **IPlugin type itself** for **Unregister plugin type**. After confirmation naming the organization, assembly, type and ID, the app rechecks that the type and assembly are unmanaged, the registration identity still matches, and every associated step is unmanaged and customizable. Step deletes and the type delete run together in one CRM transaction; a dependency or permission failure rolls back the transaction rather than leaving a partially unregistered type. CRM removes the images with their parent steps. The assembly DLL and other plugin types are retained, and the parent assembly is refreshed afterward. Workflow activity types and whole-assembly deletion are not supported. This action is exposed only in the standalone Windows app. Re-registering or updating a DLL may recreate a removed type; remove the corresponding class from your source/build if that is your intent.

Save and enable/disable operations require confirmation showing the development organization. Server faults remain visible; a timeout must be checked against server state before retrying. Unsecure configuration is loaded only for the step editor, is not logged, and must not contain secrets. Secure configuration is untouched.

## Editing web resource code

Select a supported text resource in **Web resources** and click **Edit code**. The editor loads the current **unpublished** content, not an older published version. Paste or change code and choose **Save unpublished**. Closing with unsaved edits requires confirmation. Select **Publish selected** separately when ready.

Before saving, the editor compares current unpublished content with its opening snapshot and refuses detected conflicting edits. This is a pre-write comparison, not an atomic server-side concurrency guarantee. If another developer changed the resource, copy your local edits, reopen, and merge; no forced overwrite fallback is provided. Encoding/BOM are preserved, but this is a plain text editor without syntax validation/highlighting or automatic builds. JavaScript resources must contain compiled JavaScript, not TypeScript. Binary files still use upload.

Additional offline tests cover registration request shapes, immutable registration identities, managed/customizability guards, stage/image rules, enable/disable state codes, encoding round-trips, and changed/unchanged/conflicting text editor saves.

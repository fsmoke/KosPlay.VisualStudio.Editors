KosPlay Visual Studio Editors - WiX / HeatWave

Build
-----
Open KosPlay.VisualStudio.Editors.slnx with HeatWave installed, then build or
rebuild the setup project. The SDK and extensions are pinned to WiX 5.0.2;
NuGet restores them automatically. Microsoft Installer Projects is not required.
The command-line build uses Visual Studio 2026 MSBuild:
  .\Build-Msi.ps1 -Configuration Release

Result:
  Installer\bin\Release\KosPlay.VisualStudio.Editors.VS18.msi

Installer layout
----------------
Package.wxs declares per-machine installation, native MSI file components,
HKLM registration, upgrade handling, native UI and DLL action sequencing.
The setup project references the editor and InstallerCustomActions projects.
Prepare-Payload.ps1 extracts the built VSIX into Installer/obj/<Configuration>/
Extension. WiX includes those files as native components; there is no installed
payload.zip and no manual extension-file copying or deletion in custom code.
The staged manifest sets InstalledByMsi=true; the standalone VSIX is unchanged.
The MSI remains managed through Windows Installed Apps.

InstallerCustomActions is a net472 C# DTF class library. DTF produces an x64 .CA.dll
containing the action assembly, Integration Core, Setup API interop and runtime
configuration. Package.wxs embeds it in the MSI Binary table, so removal and
rollback do not depend on files that MSI has already deleted.
There is no custom-action EXE, WinForms waiting dialog, window enumeration, or
post-build MSI database rewriting.

Install / uninstall / repair
----------------------------
This package targets VS 18. Setup API discovers the installed instances.
If multiple matching instances exist, specify:
  msiexec /i "Installer\bin\Release\KosPlay.VisualStudio.Editors.VS18.msi" VSINSTALLDIR="D:\Apps\Visual Studio"

MSI owns HKLM\Software\KosPlay\VisualStudio.Editors\VS18 values VSRoot and Product.
The saved root is used for maintenance and removal. Switching instances requires
uninstalling the previous integration first. The upgrade code is unchanged from
the vdproj package. The MSI, VSIX and C# projects use product version 1.0.0.

The immediate selection action resolves VS before directory costing.
The immediate wait action runs before InstallInitialize and upgrade removal.
While the selected VS is running it sends MSI progress messages. The native
progress window displays the wait; its Cancel button returns ActionResult.UserExit.
Closing all processes of that selected VS instance continues automatically.
Cancellation during this wait happens before any package changes.
Silent installation/removal fails instead of waiting indefinitely if VS is open.

Native MSI actions install/remove the extension files and registry entries.
XML changes and devenv /setup run as deferred, non-impersonating DLL actions
with Windows Installer elevation. The selected root is passed in CustomActionData.
The launching process does not need to have been started as administrator;
Windows Installer requests elevation as required.

Installation attempts all configured XML patches and logs the complete result.
Patch failures are reported as MSI warnings; the editor and successful patches
remain installed. Uninstall restores all configured XML defaults before RemoveFiles.
If restoration fails, the action reports an error and removal stops, retaining
the editor files. Successful individual XML restores remain in place for retry.
No original XML files or fragments are saved.

For a failed fresh installation, rollback restores configured XML defaults before
MSI rolls back extension files, then refreshes VS after native file rollback.
A failed repair does not undo pre-existing XML integration.
The installed IntegrationTool.exe is retained for the extension's startup repair;
MSI invokes Integration Core directly and does not launch IntegrationTool.exe.

Integration Core
----------------
VisualStudioInstance uses Setup API packages to obtain VC MSBuild versions and
resource languages. CultureInfo converts language identifiers to LCIDs.
There is no v* or numeric-language directory scan.
EditorPatches.cs contains EditorPatches, RulePatch and Property and resolves the
embedded EditorPatches.xml hierarchy to absolute paths and rule patches.
RuleFile uses XDocument/LocalName and provides Modify/Rollback/IsModified.
EditorIntegration runs the batch with an in-memory IntegrationLog. Individual
file/property failures do not prevent attempts on remaining items.
Add future rules to Integration/EditorPatches.xml; no installer change is needed.

Verification
------------
  MSBuild Tests\KosPlay.Integration.Tests.csproj /restore /p:Configuration=Release
  .\Tests\bin\Release\net472\KosPlay.Integration.Tests.exe
  MSBuild Tests\InstallerWait\KosPlay.Installer.Wait.Tests.csproj /restore /p:Configuration=Release
  .\Tests\InstallerWait\bin\Release\net472\KosPlay.Installer.Wait.Tests.exe
  .\Tests\Verify-Msi.ps1 -Configuration Release
  .\Tests\Verify-Dtf.ps1 -Configuration Release

Verify-Msi inspects native file/registry ownership, embedded DLL architecture,
elevation, action sequencing, upgrade identity and native progress/cancellation UI.
Verify-Dtf opens the MSI without installing and invokes only embedded DLL
discovery and native directory costing. It does not patch the installed VS.
A real install/uninstall/UAC/upgrade/cancellation cycle still requires a separate
test in an isolated VS instance or VM before publication.

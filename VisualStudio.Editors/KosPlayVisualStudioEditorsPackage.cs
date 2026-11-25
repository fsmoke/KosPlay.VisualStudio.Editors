using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using KosPlay.VisualStudio.Editors.Integration;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace KosPlay.VisualStudio.Editors
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideAutoLoad(Microsoft.VisualStudio.VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid("2C46C982-1B63-4EA1-9DA4-255AA6C31639")]
    public sealed class KosPlayVisualStudioEditorsPackage : AsyncPackage
    {
        private static bool NeedsModification(VisualStudioInstance instance)
        {
            foreach (var patch in new EditorPatches().Resolve(instance))
            {
                try
                {
                    if (!new RuleFile(patch.FilePath).IsModified(patch.Patch))
						return true;
                }
                catch (Exception ex)
                {
                    ActivityLog.LogError("KosPlay.VisualStudio.Editors", patch.FilePath + ": " + ex.Message);
                }
            }
            return false;
        }
        protected override async Task InitializeAsync(CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            // Determine the instance from this devenv process, not from the SDK used at build time.
            string executable;
            using (var process = Process.GetCurrentProcess())
                executable = process.MainModule!.FileName;
            var root = VisualStudioInstance.CurrentRoot(executable);
            try
            {
                var instance = VisualStudioInstance.Require(root);
                if (!NeedsModification(instance)) return;
                await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                var consent = MessageBox.Show(
                    "The KosPlay editor registration is missing or was changed by a Visual Studio update.\n\n" +
                    "Restore it? Windows will request administrator permission. After the repair, " +
                    "you may need to restart Visual Studio.",
                    "KosPlay - editor setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (consent != DialogResult.Yes) return;
                var helper = Path.Combine(Path.GetDirectoryName(typeof(KosPlayVisualStudioEditorsPackage).Assembly.Location)!,
                    "KosPlay.Integration.Tool.exe");
                using (var process = Process.Start(new ProcessStartInfo(helper) {
                    UseShellExecute = true, Verb = "runas",
                    Arguments = "--apply \"" + root.TrimEnd('\\') + "\""
                }))
                {
                    if (process == null) throw new InvalidOperationException("Could not start the editor setup.");
                    await Task.Run(() => process.WaitForExit(), cancellationToken);
                    if (process.ExitCode == 0)
                        MessageBox.Show("The editor registration has been restored. If the properties window was already open, " +
                            "restart Visual Studio.", "KosPlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // UAC cancellation: leave the installation untouched and retry at the next startup.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                ActivityLog.LogError("KosPlay.VisualStudio.Editors", ex.ToString());
                await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                MessageBox.Show("Could not check or restore the editor registration:\n" + ex.Message,
                    "KosPlay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using KosPlay.VisualStudio.Editors.Integration;
using WixToolset.Dtf.WindowsInstaller;

namespace KosPlay.Installer
{
    public static class Actions
    {
        [CustomAction]
        public static ActionResult SelectVisualStudio(Session session) => Run(session, () =>
        {
            string saved = session["SAVEDVSROOT"];
            string requested = session["VSINSTALLDIR"];
            bool removing = session["REMOVE"].Equals("ALL", StringComparison.OrdinalIgnoreCase);
            string root = SelectRoot(saved, requested, removing,
                () => VisualStudioInstance.FindAll().Where(instance => instance.Version.Major == 18)
                    .Select(instance => instance.Root).ToArray());

            if (!removing)
            {
                var instance = VisualStudioInstance.Require(root);
                if (instance.Version.Major != 18)
                    throw new InvalidOperationException("This MSI targets Visual Studio 18.");
            }

            session["VSINSTALLDIR"] = root;
            string data = new CustomActionData { { "VSRoot", root } }.ToString();
            foreach (string action in new[] { "KosPlayModify", "KosPlayRestore", "KosPlayRollbackRules",
                "KosPlayRefresh", "KosPlayRollbackRefresh" })
                session[action] = data;
            session.Log("Selected Visual Studio: {0}", root);
            return ActionResult.Success;
        });

        internal static string SelectRoot(string saved, string requested, bool removing, Func<string[]> findRoots)
        {
            if (!string.IsNullOrWhiteSpace(saved) && !string.IsNullOrWhiteSpace(requested) &&
                !string.Equals(Normalize(saved), Normalize(requested), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Uninstall the previous integration before selecting another Visual Studio instance.");
            string root = string.IsNullOrWhiteSpace(saved) ? requested : saved;
            if (string.IsNullOrWhiteSpace(root))
            {
                if (removing)
                    throw new InvalidOperationException("The saved Visual Studio installation path is missing.");
                string[] candidates = findRoots();
                if (candidates.Length != 1)
                    throw new InvalidOperationException("Specify VSINSTALLDIR=\"<Visual Studio path>\". Installed instances: " +
                        string.Join(", ", candidates));
                root = candidates[0];
            }
            return Normalize(root);
        }

        private static string Normalize(string root) => Path.GetFullPath(root).TrimEnd('\\');

        [CustomAction]
        public static ActionResult WaitForVisualStudio(Session session) => Run(session, () =>
        {
            string root = session["VSINSTALLDIR"];
            bool silent = int.TryParse(session["UILevel"], out int level) && level <= 2;
            using (var start = new Record(3))
            using (var progress = new Record(4))
            {
                start[1] = "KosPlayWait";
                start[2] = "Waiting for Visual Studio to close: " + root;
                start[3] = "";
                if (session.Message(InstallMessage.ActionStart, start) == MessageResult.Cancel)
                    return ActionResult.UserExit;
                // Zero ticks: pump MSI progress/cancellation without advancing the progress bar.
                progress[1] = 2;
                progress[2] = 0;
                progress[3] = 0;
                progress[4] = 0;
                return StudioWait.Wait(
                    () => StudioWait.HasRunningInstance(root),
                    () => session.Message(InstallMessage.Progress, progress) == MessageResult.Cancel,
                    silent);
            }
        });

        [CustomAction]
        public static ActionResult ModifyRules(Session session) => Run(session, () =>
        {
            var log = new EditorIntegration(VisualStudioInstance.Require(Root(session))).Modify();
            session.Log("{0}", log.ToString());
            // Keep successful changes and the installed editor even if another XML could not be patched.
            if (!log) Message(session, InstallMessage.Warning, log.ToString());
            return ActionResult.Success;
        });

        [CustomAction]
        public static ActionResult RestoreRules(Session session) => Run(session, () =>
        {
            string root = Root(session);
            var instance = VisualStudioInstance.FindAll().FirstOrDefault(candidate =>
                string.Equals(candidate.Root, root, StringComparison.OrdinalIgnoreCase));
            if (instance == null)
            {
                if (Directory.Exists(root))
                    throw new InvalidOperationException("Cannot locate the selected Visual Studio through Setup API: " + root);
                return ActionResult.Success; // Visual Studio itself has already been removed.
            }
            var log = new EditorIntegration(instance).Rollback();
            session.Log("{0}", log.ToString());
            if (!log) throw new InvalidOperationException(log.ToString());
            return ActionResult.Success;
        });

        [CustomAction]
        public static ActionResult RefreshVisualStudio(Session session) => Run(session, () =>
        {
            string root = Root(session);
            string devenv = Path.Combine(root, "Common7", "IDE", "devenv.exe");
            if (!File.Exists(devenv)) return ActionResult.Success;
            using (var process = Process.Start(new ProcessStartInfo(devenv, "/setup /nosetupvstemplates") {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(devenv)!
            }))
            {
                if (process == null) throw new InvalidOperationException("Could not refresh Visual Studio.");
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("devenv /setup failed: " + process.ExitCode);
            }
            return ActionResult.Success;
        });

        private static string Root(Session session) => session.CustomActionData["VSRoot"];

        internal static ActionResult Run(Session session, Func<ActionResult> operation)
        {
            try { return operation(); }
            catch (OperationCanceledException) { return ActionResult.UserExit; }
            catch (Exception ex)
            {
                session.Log("{0}", ex.ToString());
                Message(session, InstallMessage.Error, ex.Message);
                return ActionResult.Failure;
            }
        }

        private static void Message(Session session, InstallMessage kind, string text)
        {
            using (var record = new Record(0))
            {
                record.FormatString = text;
                session.Message(kind, record);
            }
        }
    }
}
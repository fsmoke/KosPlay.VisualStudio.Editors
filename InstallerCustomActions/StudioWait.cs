using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using KosPlay.VisualStudio.Editors.Integration;
using WixToolset.Dtf.WindowsInstaller;

namespace KosPlay.Installer
{
    internal static class StudioWait
    {
        internal static ActionResult Wait(Func<bool> isRunning, Func<bool> isCanceled, bool silent,
            Action? pause = null)
        {
            while (isRunning())
            {
                if (silent)
                    throw new InvalidOperationException("Close the selected Visual Studio before a silent installation or removal.");
                if (isCanceled()) return ActionResult.UserExit;
                if (pause == null) Thread.Sleep(300);
                else pause();
            }
            return ActionResult.Success;
        }

        internal static bool HasRunningInstance(string root)
        {
            string selected = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            foreach (var process in Process.GetProcessesByName("devenv"))
            using (process)
            {
                IntPtr handle = OpenProcess(0x1000, false, process.Id); // PROCESS_QUERY_LIMITED_INFORMATION
                if (handle == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 87) continue; // Already exited.
                    throw new Win32Exception(error, "Could not inspect a running Visual Studio process.");
                }
                try
                {
                    var path = new StringBuilder(32768);
                    int length = path.Capacity;
                    if (!QueryFullProcessImageName(handle, 0, path, ref length))
                    {
                        if (process.HasExited) continue;
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    if (string.Equals(VisualStudioInstance.CurrentRoot(path.ToString()),
                        selected, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                finally { CloseHandle(handle); }
            }
            return false;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int length);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
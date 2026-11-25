using System;
using System.Windows.Forms;
using KosPlay.VisualStudio.Editors.Integration;

namespace KosPlay.Integration.Tool
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length != 2 || (args[0] != "--apply" && args[0] != "--rollback"))
                    throw new ArgumentException("Usage: KosPlay.Integration.Tool.exe --apply|--rollback <Visual Studio installation>");

                var integration = new EditorIntegration(VisualStudioInstance.Require(args[1]));
                var log = args[0] == "--apply" ? integration.Modify() : integration.Rollback();
                if (!log)
                {
                    MessageBox.Show(log.ToString(), "KosPlay: integration", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 1;
                }
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "KosPlay: integration", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}

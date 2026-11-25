using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Linq;

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace KosPlay.VisualStudio.Editors
{
	[Export(typeof(IPropertyPageUIValueEditor))]
	[ExportMetadata("Name", EditorName)]
	[AppliesTo("")]
	internal sealed class KosPlayCommandPropertyEditor
		: IPropertyPageUIValueEditor
	{
		public const string EditorName = "KosPlayCommandPropertyEditor";

		[Import(RequiredCreationPolicy = CreationPolicy.Shared)]
		public SVsServiceProvider ServiceProvider { get; set; } = null!;

		public async Task<object> EditValueAsync(
			IServiceProvider pageHost,
			IProperty property,
			object? currentValue)
		{
			var macros = await MacroLoader.LoadAsync(property);

			await ThreadHelper.JoinableTaskFactory
				.SwitchToMainThreadAsync();

			var provider = (IServiceProvider)ServiceProvider;
			var uiShell = provider.GetService(typeof(SVsUIShell)) as IVsUIShell;

			// Do not open a separate modal window without an owner.
			if (uiShell == null)
				return currentValue!;

			ErrorHandler.ThrowOnFailure(
				uiShell.GetDialogOwnerHwnd(out IntPtr ownerHandle));

			var thisEditor = property?.ValueEditors?.FirstOrDefault( e => e.EditorType == EditorName);
			var defaultSyntax = thisEditor?.Metadata?.FirstOrDefault(p => p.Name == "DefaultSyntax")?.Value ?? "";
			
			using (var dialog = new CommandEditorWindow(
				property?.DisplayName ?? "Command editor",
				currentValue?.ToString() ?? string.Empty,
				defaultSyntax,
				macros))
			{
				var owner = new WindowOwner(ownerHandle);

				return dialog.ShowDialog(owner) == DialogResult.OK
					? (object)dialog.CommandText
					: currentValue!;
			}
		}

		private sealed class WindowOwner : IWin32Window
		{
			public WindowOwner(IntPtr handle)
			{
				Handle = handle;
			}

			public IntPtr Handle { get; }
		}
	}
}

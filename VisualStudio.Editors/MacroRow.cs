using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.VisualStudio.ProjectSystem.Properties;

namespace KosPlay.VisualStudio.Editors
{
	internal sealed class MacroRow
	{
		public string Name { get; set; } = string.Empty;

		public string Value { get; set; } = string.Empty;
	}

	internal static class MacroLoader
	{
		public static async Task<List<MacroRow>> LoadAsync(
			IProperty property)
		{
			var result = new List<MacroRow>();

			if (!(property is IEvaluatedProperty evaluatedProperty))
				return result;

			var macros =
				await evaluatedProperty.ComputeBuildMacrosAsync();

			foreach (var macro in macros)
			{
				result.Add(new MacroRow
				{
					Name = macro.Key,
					Value = macro.Value.Value ?? string.Empty
				});
			}

			result.Sort((left, right) =>
				StringComparer.OrdinalIgnoreCase.Compare(
					left.Name,
					right.Name));

			return result;
		}
	}
}

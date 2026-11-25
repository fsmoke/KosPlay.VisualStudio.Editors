using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Setup.Configuration;

namespace KosPlay.VisualStudio.Editors.Integration
{
    public sealed class VisualStudioInstance
    {
        private readonly IReadOnlyList<VisualStudioPackage> packages;

        public string Root { get; }
        public Version Version { get; }

        public VisualStudioInstance(string root, Version version, IEnumerable<VisualStudioPackage> packages)
        {
            Root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            Version = version;
            this.packages = packages.ToArray();
        }

        public static IReadOnlyList<VisualStudioInstance> FindAll()
        {
            var result = new List<VisualStudioInstance>();
            var query = (ISetupConfiguration2)new SetupConfiguration();
            var instances = query.EnumAllInstances();
            var buffer = new ISetupInstance[1];

            while (true)
            {
                instances.Next(1, buffer, out int count);
                if (count == 0) break;

                var instance = (ISetupInstance2)buffer[0];
                string root = instance.GetInstallationPath();
                if (!instance.IsComplete() || !File.Exists(Path.Combine(root, "Common7", "IDE", "devenv.exe")))
                    continue;

                var packages = instance.GetPackages().Select(package => new VisualStudioPackage(
                    package.GetId(), package.GetVersion(), package.GetLanguage()));
                result.Add(new VisualStudioInstance(root, Version.Parse(instance.GetInstallationVersion()), packages));
            }
            return result;
        }

        public static VisualStudioInstance Require(string root)
        {
            string fullPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            return FindAll().SingleOrDefault(instance => string.Equals(instance.Root, fullPath, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Visual Studio installation was not found: " + root);
        }

        public static string CurrentRoot(string devenvPath) =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(devenvPath)!, "..", ".."));

        public IReadOnlyList<VcDirectory> GetVCDirectories()
        {
            var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var languages = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

            foreach (var package in packages)
            {
                // Modern packages name the target directory explicitly, e.g. MSBuild.v180.Base.
                // Older MSBuild.Base packages use their version: 16.x corresponds to v160.
                var match = Regex.Match(package.Id,
                    @"^Microsoft\.VisualStudio\.VC\.MSBuild\.(?:v(?<version>\d+)\.)?Base(?<resources>\.Resources)?$",
                    RegexOptions.IgnoreCase);
                if (!match.Success) continue;

                string directory = match.Groups["version"].Success
                    ? "v" + match.Groups["version"].Value
                    : "v" + (System.Version.Parse(package.Version).Major * 10).ToString(CultureInfo.InvariantCulture);

                if (!match.Groups["resources"].Success)
                {
                    versions.Add(directory);
                    continue;
                }

                string language = package.Language ?? "";
                if (string.IsNullOrWhiteSpace(language) || language.Equals("neutral", StringComparison.OrdinalIgnoreCase))
                    continue;

                int lcid = CultureInfo.GetCultureInfo(language.Replace('_', '-')).LCID;
                if (!languages.TryGetValue(directory, out var ids))
                    languages.Add(directory, ids = new HashSet<int>());
                ids.Add(lcid);
            }

            var result = new List<VcDirectory>();
            foreach (string version in versions.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                string path = Path.Combine(Root, "MSBuild", "Microsoft", "VC", version);
                if (Directory.Exists(path))
                    result.Add(new VcDirectory(path, languages.TryGetValue(version, out var ids) ? ids : Enumerable.Empty<int>()));
            }
            return result;
        }
    }

    public sealed class VcDirectory
    {
        public string Path { get; }
        public IReadOnlyList<int> Languages { get; }

        public VcDirectory(string path, IEnumerable<int> languages)
        {
            Path = System.IO.Path.GetFullPath(path);
            Languages = languages.Distinct().OrderBy(value => value).ToArray();
        }
    }

    public sealed class VisualStudioPackage
    {
        public string Id { get; }
        public string Version { get; }
        public string? Language { get; }

        public VisualStudioPackage(string id, string version, string? language = null)
        {
            Id = id;
            Version = version;
            Language = language;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace KosPlay.VisualStudio.Editors.Integration
{
    public sealed class EditorPatches
    {
        private readonly XElement root;

        public EditorPatches()
        {
            using (var stream = typeof(EditorPatches).Assembly.GetManifestResourceStream(
                "KosPlay.Integration.Core.EditorPatches.xml")
                ?? throw new InvalidOperationException("EditorPatches.xml was not found in resources."))
                root = Load(stream);
        }

        public EditorPatches(Stream stream)
        {
            root = Load(stream);
        }

        private static XElement Load(Stream stream)
        {
            var document = XDocument.Load(stream);
            if (document.Root?.Name.LocalName != "EditorPatches")
                throw new InvalidDataException("Expected the EditorPatches root tag.");
            return document.Root;
        }

        public IReadOnlyList<(string FilePath, RulePatch Patch)> Resolve(VisualStudioInstance installation) =>
            Resolve(installation.GetVCDirectories());

        public IReadOnlyList<(string FilePath, RulePatch Patch)> Resolve(IEnumerable<VcDirectory> directories)
        {
            var result = new List<(string FilePath, RulePatch Patch)>();
            foreach (var node in root.Elements())
            {
                if (node.Name.LocalName != "VCDir")
                    throw new InvalidDataException("Expected VCDir, found " + node.Name.LocalName + ".");

                foreach (var directory in directories)
                    ReadChildren(node, directory.Path, directory, result);
            }
            return result;
        }

        private static void ReadChildren(XElement parent, string path, VcDirectory directory,
            List<(string FilePath, RulePatch Patch)> result)
        {
            foreach (var node in parent.Elements())
            {
                switch (node.Name.LocalName)
                {
                    case "LangDir":
                        foreach (int language in directory.Languages)
                            ReadChildren(node, Path.Combine(path, language.ToString(CultureInfo.InvariantCulture)), directory, result);
                        break;

                    case "SubDir":
                        ReadChildren(node, SubDirectory(path, Required(node, "Path")), directory, result);
                        break;

                    case "File":
                        string name = Required(node, "Name");
                        if (Path.GetFileName(name) != name)
                            throw new InvalidDataException("File.Name must contain a file name, not a path.");
                        var patch = new RulePatch { Name = Required(node, "Rule") };
                        foreach (var property in node.Elements())
                        {
                            if (property.Name.LocalName != "Property")
                                throw new InvalidDataException("Expected Property, found " + property.Name.LocalName + ".");
                            patch.Properties.Add(new Property {
                                Name = Required(property, "Name"),
                                DefaultEditor = Required(property, "DefaultEditor"),
                                Syntax = property.Attr("Syntax") ?? "batch"
                            });
                        }
                        string filePath = Path.Combine(path, name);
                        if (File.Exists(filePath)) result.Add((filePath, patch));
                        break;

                    default:
                        throw new InvalidDataException("Unknown patch tag: " + node.Name.LocalName);
                }
            }
        }

        private static string SubDirectory(string parent, string relative)
        {
            if (Path.IsPathRooted(relative))
                throw new InvalidDataException("SubDir.Path must be relative.");
            string path = Path.GetFullPath(Path.Combine(parent, relative));
            string prefix = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SubDir.Path must stay inside its parent directory.");
            return path;
        }

        private static string Required(XElement element, string name)
        {
            string? value = element.Attr(name);
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException(element.Name.LocalName + " requires attribute " + name + ".");
            return value!;
        }
    }

    public sealed class RulePatch
    {
        public string Name = "";
        public List<Property> Properties = new List<Property>();
    }

    public sealed class Property
    {
        public string Name = "";
        public string DefaultEditor = "";
        public string Syntax = "batch";
    }
}

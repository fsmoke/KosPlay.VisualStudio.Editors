using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace KosPlay.VisualStudio.Editors.Integration
{
    public sealed class RuleFile
    {
        public const string EditorName = "KosPlayCommandPropertyEditor";
        private const string SyntaxName = "DefaultSyntax";
        private readonly string path;
        private readonly XDocument document;
        private bool changed;

        public RuleFile(string filePath)
        {
            if (!Path.IsPathRooted(filePath))
                throw new ArgumentException("An absolute rule file path is required.", nameof(filePath));
            path = filePath;
            document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            if (document.Root?.Name.LocalName != "Rule")
                throw new InvalidDataException("Expected the Rule root tag: " + path);
            document.Changed += (_, __) => changed = true;
        }

        public PatchResult Modify(RulePatch patch) => Execute(patch, ModifyProperty);
        public PatchResult Rollback(RulePatch patch) => Execute(patch, RollbackProperty);

        // Read-only inspection used by the package's startup repair check.
        public bool IsModified(RulePatch patch)
        {
            ValidateRule(patch);
            foreach (var property in patch.Properties)
            {
                var editor = FindEditor(FindProperty(property.Name));
                if (editor?.Attr("EditorType") != EditorName) return false;
                var metadata = Child(editor, "ValueEditor.Metadata");
                var syntax = metadata?.Elements().Where(IsSyntax).ToList();
                if (syntax == null || syntax.Count != 1 || syntax[0].Attr("Value") != property.Syntax)
                    return false;
            }
            return true;
        }

        private PatchResult Execute(RulePatch patch, Action<Property> action)
        {
            var errors = new List<string>();
            try { ValidateRule(patch); }
            catch (Exception ex) { return new PatchResult(PatchStatus.Failed, ex.Message); }

            foreach (var property in patch.Properties)
            {
                try { action(property); }
                catch (Exception ex) { errors.Add(property.Name + ": " + ex.Message); }
            }

            bool modified = changed;
            if (changed)
            {
                try
                {
                    document.Save(path, SaveOptions.DisableFormatting);
                    changed = false;
                }
                catch (Exception ex) { errors.Add(ex.Message); }
            }

            if (errors.Count != 0)
                return new PatchResult(PatchStatus.Failed, string.Join(Environment.NewLine, errors));
            return new PatchResult(modified ? PatchStatus.Modified : PatchStatus.NotChanged);
        }

        private void ValidateRule(RulePatch patch)
        {
            if (document.Root!.Attr("Name") != patch.Name)
                throw new InvalidDataException("Expected rule " + patch.Name + ".");
        }

        private XElement FindProperty(string name) =>
            document.Root!.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "StringProperty" && element.Attr("Name") == name)
            ?? throw new InvalidDataException("StringProperty was not found: " + name);

        private static XElement? Child(XElement parent, string name) =>
            parent.Elements().FirstOrDefault(element => element.Name.LocalName == name);

        private static XElement? FindEditor(XElement property)
        {
            var collection = Child(property, "StringProperty.ValueEditors");
            return collection == null ? null : Child(collection, "ValueEditor");
        }

        private static XElement AddChild(XElement parent, string name)
        {
            var child = new XElement(parent.Name.Namespace + name);
            parent.Add(child);
            return child;
        }

        private static void SetAttribute(XElement element, string name, string value)
        {
            var attribute = element.Attributes().FirstOrDefault(item =>
                !item.IsNamespaceDeclaration && item.Name.LocalName == name);
            if (attribute == null) element.Add(new XAttribute(name, value));
            else if (attribute.Value != value) attribute.Value = value;
        }

        private static bool IsSyntax(XElement element) =>
            element.Name.LocalName == "NameValuePair" && element.Attr("Name") == SyntaxName;

        private void ModifyProperty(Property property)
        {
            var source = FindProperty(property.Name);
            var collection = Child(source, "StringProperty.ValueEditors") ?? AddChild(source, "StringProperty.ValueEditors");
            var editor = Child(collection, "ValueEditor");
            if (editor == null)
            {
                editor = AddChild(collection, "ValueEditor");
                editor.Add(new XAttribute("DisplayName", "<Edit...>"));
            }
            SetAttribute(editor, "EditorType", EditorName);

            var metadata = Child(editor, "ValueEditor.Metadata") ?? AddChild(editor, "ValueEditor.Metadata");
            var values = metadata.Elements().Where(IsSyntax).ToList();
            var syntax = values.FirstOrDefault();
            if (syntax == null)
            {
                syntax = AddChild(metadata, "NameValuePair");
                syntax.Add(new XAttribute("Name", SyntaxName));
            }
            SetAttribute(syntax, "Value", property.Syntax);
            foreach (var duplicate in values.Skip(1)) duplicate.Remove();
        }

        private void RollbackProperty(Property property)
        {
            var editor = FindEditor(FindProperty(property.Name));
            if (editor?.Attr("EditorType") != EditorName) return;

            SetAttribute(editor, "EditorType", property.DefaultEditor);
            var metadata = Child(editor, "ValueEditor.Metadata");
            if (metadata == null) return;
            foreach (var syntax in metadata.Elements().Where(IsSyntax).ToList()) syntax.Remove();

            if (!metadata.HasElements && metadata.Nodes().All(node =>
                node is XText text && string.IsNullOrWhiteSpace(text.Value)))
                metadata.Remove();
        }
    }

    public static class X
    {
        public static IEnumerable<XElement> Tags(this XContainer node, string name) =>
            node.Descendants().Where(element => element.Name.LocalName == name);

        public static string? Attr(this XElement element, string name) =>
            element.Attributes().FirstOrDefault(attribute =>
                !attribute.IsNamespaceDeclaration && attribute.Name.LocalName == name)?.Value;

        public static string? Child(this XElement element, string name) =>
            element.Elements().FirstOrDefault(child => child.Name.LocalName == name)?.Value;
    }
}

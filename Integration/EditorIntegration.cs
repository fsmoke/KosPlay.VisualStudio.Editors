using System;
using System.Collections.Generic;
using System.Linq;

namespace KosPlay.VisualStudio.Editors.Integration
{
    public sealed class EditorIntegration
    {
        private readonly IReadOnlyList<(string FilePath, RulePatch Patch)> patches;

        public EditorIntegration(VisualStudioInstance installation)
            : this(new EditorPatches().Resolve(installation))
        {
        }

        public EditorIntegration(IEnumerable<(string FilePath, RulePatch Patch)> patches)
        {
            this.patches = patches.ToArray();
        }

        public IntegrationLog Modify() => Run((file, patch) => file.Modify(patch));
        public IntegrationLog Rollback() => Run((file, patch) => file.Rollback(patch));

        private IntegrationLog Run(Func<RuleFile, RulePatch, PatchResult> operation)
        {
            var log = new IntegrationLog();
            foreach (var patch in patches)
            {
                PatchResult result;
                try { result = operation(new RuleFile(patch.FilePath), patch.Patch); }
                catch (Exception ex) { result = new PatchResult(PatchStatus.Failed, ex.Message); }
                log.Add(patch.FilePath, result);
            }
            return log;
        }
    }
}

namespace KosPlay.VisualStudio.Editors.Integration
{
    public enum PatchStatus
    {
        Modified,
        NotChanged,
        Failed
    }

    public sealed class PatchResult
    {
        public PatchStatus Status { get; }
        public string? Error { get; }

        public PatchResult(PatchStatus status, string? error = null)
        {
            Status = status;
            Error = error;
        }

        public static implicit operator bool(PatchResult result) =>
            result != null && result.Status != PatchStatus.Failed;

        public override string ToString() =>
            Error == null ? Status.ToString() : Status + ": " + Error;
    }
}

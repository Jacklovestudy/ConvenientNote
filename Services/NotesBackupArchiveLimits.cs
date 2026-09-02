namespace ConvenientNote.Services;

internal sealed record NotesBackupArchiveLimits(
    int MaximumEntryCount,
    long MaximumManifestBytes,
    long MaximumNotesJsonBytes,
    int MaximumNoteCount,
    long MaximumMediaEntryBytes,
    long MaximumTotalExpandedBytes,
    double MaximumCompressionRatio,
    int MaximumNormalizedPathDepth)
{
    // These ceilings cover a large local notebook (10k notes, 50k ZIP entries and up to 4 GiB of media)
    // while bounding memory, disk and path-processing work from an untrusted package.
    internal static NotesBackupArchiveLimits Default { get; } = new(
        MaximumEntryCount: 50_000,
        MaximumManifestBytes: 64L * 1024,
        MaximumNotesJsonBytes: 128L * 1024 * 1024,
        MaximumNoteCount: 10_000,
        MaximumMediaEntryBytes: 256L * 1024 * 1024,
        MaximumTotalExpandedBytes: 4L * 1024 * 1024 * 1024,
        MaximumCompressionRatio: 500,
        MaximumNormalizedPathDepth: 16);
}

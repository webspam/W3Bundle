namespace W3Bundle;

public sealed record BundleToc(string Name, uint AlignedBundleSize, IReadOnlyList<BundleEntry> Entries);

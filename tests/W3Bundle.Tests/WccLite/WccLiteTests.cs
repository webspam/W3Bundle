using System.Security.Cryptography;

using CliWrap;

namespace W3Bundle.Tests.WccLite;

public sealed class WccLiteTests : IDisposable {
    readonly TempDirectory input = new();
    readonly TempDirectory content = new();

    public void Dispose() {
        input.Dispose();
        content.Dispose();
    }

    [Test]
    [MethodDataSource(typeof(WccLiteCases), nameof(WccLiteCases.Names))]
    public async Task MatchesWccLite(string caseName) {
        var expected = WccLiteExpectedHashes.ByCase[caseName];
        WccLiteCases.Get(caseName).WriteTo(input);

        BundlePacker.Pack(input.Path, content.Path);
        if (expected.Metadata is not null) {
            MetadataStore.Generate(content.Path);
        }

        await Assert.That(ContentHashes()).IsEqualTo(expected);
    }

    [Test]
    [Explicit]
    [NotInParallel]
    [MethodDataSource(typeof(WccLiteCases), nameof(WccLiteCases.Names))]
    public async Task RecordWccLite(string caseName, CancellationToken cancellationToken) {
        if (!TestContext.Parameters.TryGetValue("wcc-lite", out var values) || values.SingleOrDefault() is not { } wccLite) {
            Assert.Fail("Pass --test-parameter wcc-lite=<path to wcc_lite.exe>");
            return;
        }
        WccLiteCases.Get(caseName).WriteTo(input);

        await RunWccLite(wccLite, cancellationToken, "pack", $"-dir={input.Path}", $"-outdir={content.Path}");
        await RunWccLite(wccLite, cancellationToken, "metadatastore", $"-path={content.Path}");

        var hashes = ContentHashes();
        string metadata = hashes.Metadata is null ? "null" : $"\"{hashes.Metadata}\"";
        string buffers = hashes.Buffers is null ? "" : $", \"{hashes.Buffers}\"";
        Console.WriteLine($"[\"{caseName}\"] = new(\"{hashes.Bundle}\", {metadata}{buffers}),");
    }

    WccLiteHashes ContentHashes() => new(
        Sha256(Path.Combine(content.Path, BundleFormat.BlobBundleName)),
        Sha256IfExists(Path.Combine(content.Path, "metadata.store")),
        Sha256IfExists(Path.Combine(content.Path, BundleFormat.BuffersBundleName)));

    static string? Sha256IfExists(string path) => File.Exists(path) ? Sha256(path) : null;

    static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    static Task<CommandResult> RunWccLite(string wccLite, CancellationToken cancellationToken, params string[] arguments) {
        string fullPath = Path.GetFullPath(wccLite);
        return Cli.Wrap(fullPath)
            .WithWorkingDirectory(Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory)
            .WithArguments([.. arguments, "-agreetoterms"])
            .WithValidation(CommandResultValidation.None)
            .ExecuteAsync(cancellationToken);
    }
}

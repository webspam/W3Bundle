namespace W3Bundle.Tests;

sealed class TempDirectory : IDisposable {
    public string Path { get; } = Directory.CreateTempSubdirectory("w3bundle-tests-").FullName;

    public void Write(string relativePath, byte[] content) {
        var file = new FileInfo(System.IO.Path.Combine(Path, relativePath.Replace('\\', '/')));
        file.Directory?.Create();
        File.WriteAllBytes(file.FullName, content);
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}

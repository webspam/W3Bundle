using System.Diagnostics;

using W3Bundle;

const string Usage = """
    usage:
      W3Bundle pack -dir=<source> -outdir=<content>
      W3Bundle metadatastore -path=<content>
      W3Bundle build -dir=<source> -outdir=<content>
    """;

if (args.Length == 0) {
    Console.Error.WriteLine(Usage);
    return 1;
}

var options = args.Skip(1)
    .Where(a => a.StartsWith('-') && a.Contains('='))
    .Select(a => a[1..].Split('=', 2))
    .ToDictionary(kv => kv[0], kv => kv[1].Trim('"'), StringComparer.OrdinalIgnoreCase);

string Require(string name) => options.TryGetValue(name, out string? value)
    ? value
    : throw new ArgumentException($"Missing -{name}=\n{Usage}");

var stopwatch = Stopwatch.StartNew();
try {
    switch (args[0].ToLowerInvariant()) {
        case "pack":
            Report(BundlePacker.Pack(Require("dir"), Require("outdir")));
            break;
        case "metadatastore":
            Report(MetadataStore.Generate(Require("path")));
            break;
        case "build":
            string outdir = Require("outdir");
            Report(BundlePacker.Pack(Require("dir"), outdir));
            Report(MetadataStore.Generate(outdir));
            break;
        default:
            Console.Error.WriteLine(Usage);
            return 1;
    }
}
catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException) {
    Console.Error.WriteLine(ex.Message);
    return 1;
}
return 0;

void Report(string path) => Console.WriteLine($"{path} ({stopwatch.ElapsedMilliseconds} ms)");

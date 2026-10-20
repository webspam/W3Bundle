namespace W3Bundle.Tests;

static class TestParameters {
    public static string Required(string name) =>
        TestContext.Parameters.TryGetValue(name, out var values) && values.SingleOrDefault() is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Pass --test-parameter {name}=<value>");
}

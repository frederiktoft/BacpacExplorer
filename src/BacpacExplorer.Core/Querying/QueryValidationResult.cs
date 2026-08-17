namespace BacpacExplorer.Core.Querying;

/// <summary>
/// Describes whether submitted SQL passed structural validation and supplies user-facing reasons when it did not.
/// </summary>
public sealed record QueryValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static QueryValidationResult Success { get; } = new(true, []);
}

namespace BacpacExplorer.Core.Querying;

/// <summary>
/// Screens user-entered SQL against the application's structural read-only query rules.
/// </summary>
public interface IReadOnlyQueryValidator
{
    QueryValidationResult Validate(string sql);
}

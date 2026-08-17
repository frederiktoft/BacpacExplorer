using Microsoft.SqlServer.TransactSql.ScriptDom;
using BacpacExplorer.Core.Localization;

namespace BacpacExplorer.Core.Querying;

/// <summary>
/// Uses Microsoft's T-SQL parser to accept exactly one SELECT statement and reject SELECT INTO or
/// cross-database references before user-entered SQL reaches LocalDB. Runtime permissions remain the final safety boundary.
/// </summary>
public sealed class ScriptDomReadOnlyQueryValidator : IReadOnlyQueryValidator
{
    public QueryValidationResult Validate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return Invalid(CoreText.Get("EnterSelect"));

        var parser = new TSql180Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out var parseErrors);
        if (parseErrors.Count > 0)
        {
            return new QueryValidationResult(false, parseErrors
                .Select(error => CoreText.Format("ParseError", error.Line, error.Column, error.Message))
                .ToArray());
        }

        if (fragment is not TSqlScript { Batches.Count: 1 } script
            || script.Batches[0].Statements.Count != 1
            || script.Batches[0].Statements[0] is not SelectStatement select)
        {
            return Invalid(CoreText.Get("SingleSelectOnly"));
        }

        if (select.Into is not null)
            return Invalid(CoreText.Get("SelectIntoRejected"));

        var crossDatabaseVisitor = new CrossDatabaseReferenceVisitor();
        select.Accept(crossDatabaseVisitor);
        if (crossDatabaseVisitor.HasCrossDatabaseReference)
            return Invalid(CoreText.Get("CrossDatabaseRejected"));

        return QueryValidationResult.Success;
    }

    private static QueryValidationResult Invalid(string message) => new(false, [message]);

    /// <summary>
    /// Walks the parsed SELECT syntax tree to detect three- and four-part object names that could escape
    /// the temporary database, such as <c>otherDatabase.dbo.Table</c>.
    /// </summary>
    private sealed class CrossDatabaseReferenceVisitor : TSqlConcreteFragmentVisitor
    {
        public bool HasCrossDatabaseReference { get; private set; }

        public override void ExplicitVisit(SchemaObjectName node)
        {
            if (node.Identifiers.Count > 2)
                HasCrossDatabaseReference = true;

            base.ExplicitVisit(node);
        }
    }
}

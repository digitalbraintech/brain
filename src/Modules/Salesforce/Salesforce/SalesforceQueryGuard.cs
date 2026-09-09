using System.Text;
using System.Text.RegularExpressions;

namespace DigitalBrain.Salesforce;

internal static partial class SalesforceQueryGuard
{
    public static void Validate(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (query.Length > 20_000)
        {
            throw new ArgumentException("Salesforce query exceeds the supported length.", nameof(query));
        }

        // Ignore literals and subqueries when checking the OUTER query's filter and limit.
        // Otherwise WHERE/LIMIT inside a string or child SELECT could bypass the guard.
        var outer = new StringBuilder(query.Length);
        var syntax = new StringBuilder(query.Length);
        var depth = 0;
        var quoted = false;
        for (var i = 0; i < query.Length; i++)
        {
            var c = query[i];
            if (quoted)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '\'')
                {
                    quoted = false;
                }
                continue;
            }
            if (c == '\'')
            {
                quoted = true;
                if (depth == 0)
                {
                    outer.Append(" literal ");
                }
                continue;
            }
            if (c == ';' || (i + 1 < query.Length
                && ((c == '-' && query[i + 1] == '-') || (c == '/' && query[i + 1] is '*' or '/'))))
            {
                throw InvalidQuery();
            }
            syntax.Append(c);
            if (c == '(')
            {
                if (depth++ == 0)
                {
                    outer.Append(" expression ");
                }
            }
            else if (c == ')')
            {
                if (--depth < 0)
                {
                    throw InvalidQuery();
                }
            }
            else if (depth == 0)
            {
                outer.Append(c);
            }
        }

        if (quoted || depth != 0 || !BoundedSelect().IsMatch(outer.ToString()) || LockingQuery().IsMatch(syntax.ToString()))
        {
            throw InvalidQuery();
        }
    }

    private static ArgumentException InvalidQuery()
        => new("Use one read-only SELECT query with an outer WHERE filter and positive LIMIT. Comments, multiple statements, and locking queries are not allowed.", "query");

    [GeneratedRegex(@"\bFOR\s+(?:UPDATE|VIEW|REFERENCE)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LockingQuery();

    [GeneratedRegex(@"\A\s*SELECT\s+.+\s+FROM\s+[A-Za-z][A-Za-z0-9_]*(?:\s+[A-Za-z][A-Za-z0-9_]*)?\s+WHERE\s+.+\s+LIMIT\s+[1-9][0-9]*(?:\s+OFFSET\s+[0-9]+)?\s*\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.NonBacktracking)]
    private static partial Regex BoundedSelect();
}

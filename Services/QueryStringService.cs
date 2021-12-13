using Microsoft.AspNetCore.WebUtilities;

public interface IQueryStringService
{
    string GetValue(QueryString queryString, string name);
    QueryString CreateOnly(QueryString queryString, string keep);
    QueryString CreateExcept(QueryString queryString, string remove);
    List<(string Name, List<string> Args)> Args(QueryString queryString, Dictionary<string, HashSet<string>> requiredParameters);
}

public class QueryStringService : IQueryStringService
{
    public string GetValue(QueryString queryString, string name)
    {
        QueryStringEnumerable.Enumerator queryEnumerator = new QueryStringEnumerable(queryString.ToString())
            .GetEnumerator();

        List<KeyValuePair<string, string>> allExcept = new();

        string signature = null;

        while (queryEnumerator.MoveNext())
        {
            string decodedName = queryEnumerator.Current
                    .DecodeName()
                    .ToString()
                    .ToLower();

            if (decodedName == name) {
                return queryEnumerator.Current
                    .DecodeValue()
                    .ToString();
            }
        }

        return null;
    }

    public QueryString CreateOnly(QueryString queryString, string keep)
    {
        QueryStringEnumerable.Enumerator queryEnumerator = new QueryStringEnumerable(queryString.ToString())
            .GetEnumerator();

        List<KeyValuePair<string, string>> only = new();

        while (queryEnumerator.MoveNext())
        {
            string name = queryEnumerator.Current
                    .DecodeName()
                    .ToString()
                    .ToLower();

            if (keep == name) {
                only.Add(
                    new KeyValuePair<string, string>(
                        queryEnumerator.Current.DecodeName().ToString(),
                        queryEnumerator.Current.DecodeValue().ToString()));
            }
        }

        return QueryString.Create(only);
    }

    public QueryString CreateExcept(QueryString queryString, string remove)
    {
        QueryStringEnumerable.Enumerator queryEnumerator = new QueryStringEnumerable(queryString.ToString())
            .GetEnumerator();

        List<KeyValuePair<string, string>> allExcept = new();

        while (queryEnumerator.MoveNext())
        {
            string name = queryEnumerator.Current
                    .DecodeName()
                    .ToString()
                    .ToLower();

            if (remove != name) {
                allExcept.Add(
                    new KeyValuePair<string, string>(
                        queryEnumerator.Current.DecodeName().ToString(),
                        queryEnumerator.Current.DecodeValue().ToString()));
            }
        }

        return QueryString.Create(allExcept);
    }

    public List<(string Name, List<string> Args)> Args(QueryString queryString, Dictionary<string, HashSet<string>> requiredParameters)
    {
        List<(string Name, List<string> Args)> ret = new();

        List<QueryStringEnumerable.EncodedNameValuePair> queries = new();
        QueryStringEnumerable.Enumerator queryEnumerator = new QueryStringEnumerable(queryString.ToString())
            .GetEnumerator();

        while (queryEnumerator.MoveNext())
        {
            queries.Add(queryEnumerator.Current);
        }

        List<QueryStringEnumerable.EncodedNameValuePair> currentOpQueries = new();

        for (int idx = 0; idx < queries.Count(); ++idx)
        {
            currentOpQueries.Clear();

            HashSet<string> foundOpRequiredParameters = new();

            string opName = queries[idx]
                    .DecodeName()
                    .ToString()
                    .Split('.')
                    .First()
                    .ToLower();

            int totalAllowedRequiredParameters = requiredParameters[opName].Count();

            for (int jdx = idx; jdx < queries.Count(); ++jdx)
            {
                string jdxName = queries[jdx].DecodeName().ToString();

                string jdxOpName = queries[jdx]
                    .DecodeName()
                    .ToString()
                    .Split('.')
                    .First()
                    .ToLower();

                bool isInCurrentOpSet = false;

                if (requiredParameters[jdxOpName].Contains(jdxName) && totalAllowedRequiredParameters > 0) {
                    --totalAllowedRequiredParameters;

                    isInCurrentOpSet = true;
                } else if (requiredParameters[jdxOpName].Contains(jdxName) is false) {
                    isInCurrentOpSet = true;
                }

                if (opName == jdxOpName && isInCurrentOpSet) {
                    currentOpQueries.Add(queries[idx]);

                    ++idx;
                } else {
                    --idx;

                    break;
                }
            }

            List<string> args = new();

            foreach (QueryStringEnumerable.EncodedNameValuePair query in currentOpQueries)
            {
                string opSuffix = query.DecodeName().ToString().Split('.').Last();

                Log.Debug($"Found queryParameter: {opName}.{opSuffix}={query.DecodeValue().ToString()}");
                args.Add($"{opSuffix}={query.DecodeValue().ToString()}");
            }

            ret.Add((opName, args));
        }

        return ret;
    }
}


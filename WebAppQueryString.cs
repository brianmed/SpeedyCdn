using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

#if false
namespace SpeedyCdn.Server
{
    partial class WebApp
    {
        public static string QueryStringGetValue(QueryString queryString, string name)
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

        public static QueryString QueryStringOnly(QueryString queryString, string keep)
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

        public static QueryString QueryStringExcept(QueryString queryString, string remove)
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
    }
}
#endif

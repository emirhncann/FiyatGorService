using System.Text.RegularExpressions;

namespace FiyatGorService.Services;

public static partial class DynamicQueryParameterParser
{
    [GeneratedRegex("^[a-zA-Z0-9_]+$")]
    private static partial Regex ValidParameterNameRegex();

    public static IReadOnlyDictionary<string, string> Parse(IQueryCollection query)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in query)
        {
            if (string.Equals(entry.Key, "barcode", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!ValidParameterNameRegex().IsMatch(entry.Key))
            {
                continue;
            }

            parameters[entry.Key] = entry.Value.ToString();
        }

        return parameters;
    }
}

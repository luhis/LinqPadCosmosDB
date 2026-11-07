using System.Text;

namespace LinqPadCosmosDB;

public static class CosmosNameUtilities
{
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
        "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
        "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void",
        "volatile", "while"
    };

    public static string EncodeContainerPropertyName(string container) =>
        "C_" + string.Concat(Encoding.UTF8.GetBytes(container).Select(b =>
            b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9'
                ? ((char)b).ToString() : $"_{b:X2}"));

    public static string EncodeTypedContainerPropertyName(string container, string typeValue) =>
        EncodeContainerPropertyName(container + " " + typeValue);

    public static string ToContainerPropertyName(string container) =>
        ToPropertyName(container);

    public static string ToTypedContainerPropertyName(string container, string typeValue) =>
        ToPropertyName(container + " " + typeValue);

    public static string ToClassName(string container)
    {
        var sb = new StringBuilder("Entity");
        var capitalizeNext = true;
        foreach (var ch in container)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(capitalizeNext ? char.ToUpperInvariant(ch) : ch);
                capitalizeNext = false;
            }
            else
            {
                capitalizeNext = true;
            }
        }

        if (sb.Length == 6)
            sb.Append("Item");

        return sb.ToString();
    }

    public static string ToTypedClassName(string container, string typeValue) =>
        ToClassName(container + " " + typeValue);

    public static string ToPropertyName(string name)
    {
        var sb = new StringBuilder();
        var capitalizeNext = true;
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (sb.Length == 0 && char.IsDigit(ch))
                    sb.Append('_');
                sb.Append(capitalizeNext ? char.ToUpperInvariant(ch) : ch);
                capitalizeNext = false;
            }
            else
            {
                capitalizeNext = true;
            }
        }

        if (sb.Length == 0)
            sb.Append("Value");

        var result = sb.ToString();
        if (CSharpKeywords.Contains(result))
            result += "Value";

        return result;
    }
}

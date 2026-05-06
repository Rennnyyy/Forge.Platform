using System.Globalization;
using Forge.Repository.Rdf;

namespace Forge.Repository.Mapping;

/// <summary>
/// Converts CLR scalar values to/from RDF literals using XSD datatypes.
/// </summary>
internal static class LiteralCodec
{
    public static RdfTerm Encode(object? value, Type clrType)
    {
        if (value is null) return RdfTerm.Literal("", RdfVocab.XsdString);

        // Strip Nullable<T>
        var t = Nullable.GetUnderlyingType(clrType) ?? clrType;

        if (t == typeof(string)) return RdfTerm.Literal((string)value, RdfVocab.XsdString);
        if (t == typeof(bool)) return RdfTerm.Literal(((bool)value) ? "true" : "false", RdfVocab.XsdBoolean);
        if (t == typeof(int)) return RdfTerm.Literal(((int)value).ToString(CultureInfo.InvariantCulture), RdfVocab.XsdInt);
        if (t == typeof(long)) return RdfTerm.Literal(((long)value).ToString(CultureInfo.InvariantCulture), RdfVocab.XsdLong);
        if (t == typeof(short)) return RdfTerm.Literal(((short)value).ToString(CultureInfo.InvariantCulture), RdfVocab.XsdInt);
        if (t == typeof(double)) return RdfTerm.Literal(((double)value).ToString("R", CultureInfo.InvariantCulture), RdfVocab.XsdDouble);
        if (t == typeof(float)) return RdfTerm.Literal(((float)value).ToString("R", CultureInfo.InvariantCulture), RdfVocab.XsdFloat);
        if (t == typeof(decimal)) return RdfTerm.Literal(((decimal)value).ToString(CultureInfo.InvariantCulture), RdfVocab.XsdDecimal);
        if (t == typeof(Guid)) return RdfTerm.Literal(((Guid)value).ToString("D"), RdfVocab.XsdString);
        if (t == typeof(DateTime))
            return RdfTerm.Literal(((DateTime)value).ToString("o", CultureInfo.InvariantCulture), RdfVocab.XsdDateTime);
        if (t == typeof(DateTimeOffset))
            return RdfTerm.Literal(((DateTimeOffset)value).ToString("o", CultureInfo.InvariantCulture), RdfVocab.XsdDateTime);
        if (t == typeof(DateOnly))
            return RdfTerm.Literal(((DateOnly)value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), RdfVocab.XsdDate);
        if (t == typeof(Uri))
            return RdfTerm.Iri(((Uri)value).ToString());

        // Fallback: invariant ToString
        return RdfTerm.Literal(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "", RdfVocab.XsdString);
    }

    public static object? Decode(RdfTerm term, Type clrType)
    {
        var t = Nullable.GetUnderlyingType(clrType) ?? clrType;
        var s = term.Value;

        if (t == typeof(string)) return s;
        if (t == typeof(bool)) return bool.Parse(s);
        if (t == typeof(int)) return int.Parse(s, CultureInfo.InvariantCulture);
        if (t == typeof(long)) return long.Parse(s, CultureInfo.InvariantCulture);
        if (t == typeof(short)) return short.Parse(s, CultureInfo.InvariantCulture);
        if (t == typeof(double)) return double.Parse(s, CultureInfo.InvariantCulture);
        if (t == typeof(float)) return float.Parse(s, CultureInfo.InvariantCulture);
        if (t == typeof(decimal)) return decimal.Parse(s, CultureInfo.InvariantCulture);
        if (t == typeof(Guid)) return Guid.Parse(s);
        if (t == typeof(DateTime)) return DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (t == typeof(DateTimeOffset)) return DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (t == typeof(DateOnly)) return DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (t == typeof(Uri)) return new Uri(s);

        // Fallback: Convert.ChangeType with invariant culture
        return Convert.ChangeType(s, t, CultureInfo.InvariantCulture);
    }
}

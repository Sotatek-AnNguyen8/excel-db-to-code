namespace Application.Extensions;

public static class StringExtension
{
    public static T ToEnum<T>(this string value)
    {
        return (T)Enum.Parse(typeof(T), value, true);
    }

    public static string ToVariableCase(this string value)
    {
        return char.ToLower(value[0]) + value[1..];
    }
}
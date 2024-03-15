using Application.Extensions;
using ClosedXML.Excel;
using Humanizer;
using Microsoft.Extensions.Configuration;

namespace Application.Models;

public enum ExcelDbEntityFieldType
{
    Number,
    Varchar,
    Timestamp,
    DateTime
}

public class ExcelDbEntityField
{
    public int Index { get; init; }
    public string Name { get; init; } = null!;
    public string Description { get; init; } = null!;
    public bool IsPrimaryKey { get; init; }
    public bool IsLookup { get; init; }
    public bool IsNullable { get; init; }
    public dynamic DefaultValue { get; init; } = null!;
    public ExcelDbEntityFieldType Type { get; init; }
    public double? Length { get; init; }
}

public class ExcelDbObject
{
    private static int _cIndex;
    private static int _cName;
    private static int _cDescription;
    private static int _cPrimaryKey;
    private static int _cLookup;
    private static int _cNullable;
    private static int _cDefaultValue;
    private static int _cType;
    private static int _cLength;
    private static IEnumerable<string?> _skippedEntityFields = [];
    private static IEnumerable<string?> _skippedDtoFields = [];
    private static Dictionary<string, string?> _mappingDtoFields = new();

    // <Row, Column>
    private static Tuple<int, int> _entityNamePos = null!;

    private string Name { get; init; } = null!;
    private List<ExcelDbEntityField> Fields { get; init; } = [];

    public Dictionary<string, object> ToDictionary()
    {
        var entityFields = Fields
            .Where(f => !_skippedEntityFields.Contains(f.Name))
            .ToList();
        var dtoFields = Fields
            .Where(f => !_skippedDtoFields.Contains(f.Name))
            .ToList();

        return new Dictionary<string, object>
        {
            { "Name", Name },
            { "VarName", Name.ToVariableCase() },
            {
                "EntityFields", entityFields
                    .Select(f => new Dictionary<string, object?>
                    {
                        { "Index", f.Index },
                        { "Name", f.Name },
                        { "Description", f.Description },
                        { "PrimaryKey", f.IsPrimaryKey },
                        { "Lookup", f.IsLookup },
                        { "Nullable", f.IsNullable },
                        {
                            "DefaultValue",
                            f.DefaultValue is string
                                ? string.IsNullOrEmpty(f.DefaultValue) ? "string.Empty" : $"\"{f.DefaultValue}\""
                                : f.DefaultValue
                        },
                        { "Type", GetType(f.Type) },
                        { "MaxLength", f.Length },
                        // Additional
                        { "HasMaxLength", f is { Type: ExcelDbEntityFieldType.Varchar, Length: > 0 } },
                        { "IsRequired", !f.IsNullable },
                        { "HasDefaultValue", f is { Type: ExcelDbEntityFieldType.Varchar, IsNullable: true } },
                        { "Validation", GetValidation(f) }
                    })
            },
            {
                "DtoFields", dtoFields
                    .Select(f => new Dictionary<string, object?>
                    {
                        { "Index", f.Index },
                        { "Name", _mappingDtoFields.TryGetValue(f.Name, out var field) ? field : f.Name },
                        { "Description", f.Description },
                        { "PrimaryKey", f.IsPrimaryKey },
                        { "Lookup", f.IsLookup },
                        { "Nullable", f.IsNullable },
                        {
                            "DefaultValue",
                            f.DefaultValue is string
                                ? string.IsNullOrEmpty(f.DefaultValue) ? "string.Empty" : $"\"{f.DefaultValue}\""
                                : f.DefaultValue
                        },
                        { "Type", GetType(f.Type) },
                        { "MaxLength", f.Length },
                        // Additional
                        { "HasMaxLength", f is { Type: ExcelDbEntityFieldType.Varchar, Length: > 0 } },
                        { "IsRequired", f.IsNullable },
                        { "HasDefaultValue", f is { Type: ExcelDbEntityFieldType.Varchar, IsNullable: true } }
                    })
            },
            // Additional
            { "NamePlural", Name.Pluralize() },
            { "ParamValidation", string.Join("\n", entityFields.Select(GetParamValidation)) },
            {
                "Arguments",
                string.Join(", ",
                    entityFields.Select(f => $"{GetType(f.Type)} {f.Name.ToVariableCase()}"))
            },
            {
                "NullableArguments",
                string.Join(", ",
                    entityFields.Select(f => $"{GetType(f.Type)}? {f.Name.ToVariableCase()}"))
            },
            {
                "Params",
                string.Join(", ",
                    entityFields.Select(f => $"request.{f.Name}"))
            },
            {
                "Assignments",
                string.Join("\n",
                    entityFields.Select(
                        f => $"{new string(' ', 8)}{f.Name} = {f.Name.ToVariableCase()};"))
            }
        };
    }

    public static void Init(IConfiguration configuration)
    {
        _cIndex = int.Parse(configuration["Source:Columns:Index"] ?? "-1");
        _cName = int.Parse(configuration["Source:Columns:Name"] ?? "-1");
        _cDescription = int.Parse(configuration["Source:Columns:Description"] ?? "-1");
        _cPrimaryKey = int.Parse(configuration["Source:Columns:PrimaryKey"] ?? "-1");
        _cLookup = int.Parse(configuration["Source:Columns:Lookup"] ?? "-1");
        _cNullable = int.Parse(configuration["Source:Columns:Nullable"] ?? "-1");
        _cDefaultValue = int.Parse(configuration["Source:Columns:DefaultValue"] ?? "-1");
        _cType = int.Parse(configuration["Source:Columns:Type"] ?? "-1");
        _cLength = int.Parse(configuration["Source:Columns:Length"] ?? "-1");
        _entityNamePos = Tuple.Create(int.Parse(configuration["Source:EntityName:Row"] ?? "-1"),
            int.Parse(configuration["Source:EntityName:Column"] ?? "-1"));
        _skippedEntityFields = configuration.GetSection("Generated:Entity:SkippedFields").GetChildren()
            .Select(c => c.Value);
        _skippedDtoFields = configuration.GetSection("Generated:Dto:SkippedFields").GetChildren().Select(c => c.Value);
        _mappingDtoFields = configuration.GetSection("Generated:Dto:Mapping").GetChildren()
            .ToDictionary(x => x.Key, x => x.Value);
    }

    public static ExcelDbObject BuildEntityFromSheet(IXLWorksheet ws)
    {
        var entity = new ExcelDbObject
        {
            Name = GetName(ws),
            Fields = GetFields(ws)
        };

        return entity;
    }

    private static string GetName(IXLWorksheet ws)
    {
        return string.Join("", ws.Cell(_entityNamePos.Item1, _entityNamePos.Item2)
            .GetText()
            .Split(' ')
            .Select((str, idx) => idx == 0 ? str : str.ToVariableCase()));
    }

    private static List<ExcelDbEntityField> GetFields(IXLWorksheet ws)
    {
        var currRow = FindFirstRow(ws);
        var fields = new List<ExcelDbEntityField>();

        while (!ws.Cell(currRow, _cIndex).Value.IsBlank)
        {
            var row = ws.Row(currRow);
            var cLengthValue = row.Cell(_cLength).Value;
            var cDefaultValueValue = row.Cell(_cDefaultValue).Value;

            var index = (int)row.Cell(_cIndex).Value.GetNumber();
            var name = row.Cell(_cName).Value.GetText();
            var description = row.Cell(_cDescription).Value.GetText();
            var isPrimaryKey = !row.Cell(_cPrimaryKey).Value.IsBlank;
            var isLookup = !row.Cell(_cLookup).Value.IsBlank;
            var isNullable = row.Cell(_cNullable).Value.IsBlank;
            dynamic defaultValue = cDefaultValueValue.IsBlank
                ? string.Empty
                : cDefaultValueValue.IsText
                    ? cDefaultValueValue.GetText()
                    : cDefaultValueValue.GetNumber();
            var type = row.Cell(_cType).Value.GetText().ToEnum<ExcelDbEntityFieldType>();
            double? length = cLengthValue.IsNumber ? cLengthValue.GetNumber() : null;

            fields.Add(new ExcelDbEntityField
            {
                Index = index,
                Name = name,
                Description = description,
                IsPrimaryKey = isPrimaryKey,
                IsLookup = isLookup,
                IsNullable = isNullable,
                DefaultValue = defaultValue,
                Type = type,
                Length = length,
            });

            currRow++;
        }

        return fields;
    }

    private static int FindFirstRow(IXLWorksheet ws)
    {
        var rows = ws.RangeUsed().RowsUsed().Skip(1); // Skip header row
        foreach (var row in rows)
        {
            var rowValue = row.Cell(_cIndex).Value;
            if (rowValue.IsNumber && (int)rowValue.GetNumber() == 1)
            {
                return row.RowNumber();
            }
        }

        throw new Exception("Cannot find first row, which has index cell as 1");
    }

    private static string GetType(ExcelDbEntityFieldType type)
    {
        return type switch
        {
            ExcelDbEntityFieldType.Varchar => "string",
            ExcelDbEntityFieldType.Number => "double",
            ExcelDbEntityFieldType.Timestamp => "DateTimeOffset",
            ExcelDbEntityFieldType.DateTime => "DateTime",
            _ => throw new Exception($"Unhandled type: {type}")
        };
    }

    private static string? GetValidation(ExcelDbEntityField field)
    {
        List<string> validations = [];

        if (!field.IsNullable)
        {
            validations.Add("NotEmpty()");
        }

        switch (field.Type)
        {
            case ExcelDbEntityFieldType.Varchar:
                if (field.Length is > 0)
                {
                    validations.Add($"MaximumLength({field.Length})");
                }

                break;
            case ExcelDbEntityFieldType.Number:
            case ExcelDbEntityFieldType.Timestamp:
            case ExcelDbEntityFieldType.DateTime:
            default:
                break;
        }

        if (validations.Count == 0)
        {
            return null;
        }

        return $"{new string(' ', 8)}validator.RuleFor(x => x.{field.Name})" +
               string.Join("", validations.Select(v => $"\n{new string(' ', 12)}.{v}")) + ";";
    }

    private static string GetParamValidation(ExcelDbEntityField field)
    {
        var varName = field.Name.ToVariableCase();
        var varAbbr = varName[0];

        if (field.Type == ExcelDbEntityFieldType.Varchar)
        {
            return $$"""
                             if (!string.IsNullOrEmpty({{field.Name}}))
                             {
                                 Query.Where({{varAbbr}} => {{varAbbr}}.{{field.Name}}.Contains({{varName}}));
                             }
                     """;
        }

        return $$"""
                         if (status != null)
                         {
                             Query.Where({{varAbbr}} => {{varAbbr}}.{{field.Name}}.Contains({{varName}}));
                         }
                 """;
    }
}
using System.Text.RegularExpressions;
using Application.Extensions;
using ClosedXML.Excel;
using Humanizer;
using Microsoft.Extensions.Configuration;

namespace Application.Models;

public enum ExcelDbEntityFieldType
{
    Number,
    Int,
    Decimal,
    Varchar,
    Timestamp,
    DateTime,
    Enum,
    Boolean
}

public class ExcelDbEntityField
{
    public int Index { get; init; }
    public string Name { get; init; } = null!;
    public string? Description { get; init; }
    public bool IsPrimaryKey { get; init; }
    public bool IsLookup { get; init; }
    public bool IsNullable { get; init; }
    public dynamic DefaultValue { get; init; } = null!;
    public ExcelDbEntityFieldType Type { get; init; }
    public double? Length { get; init; }
    public ExcelDbEntityEnum? EnumType { get; init; }
}

public class ExcelDbEntityEnum
{
    public string Name { get; init; } = null!;
    public string DisplayName { get; init; } = null!;
    public List<KeyValuePair<string, int>> Values { get; init; } = null!;
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
    private static int _cEnumNo;
    private static int _cEnumValue;
    private static int _cEnumDescription;
    private static string _modelSuffix = string.Empty;
    private static IEnumerable<string?> _skippedEntityFields = [];
    private static IEnumerable<string?> _skippedDtoFields = [];
    private static Dictionary<string, string?> _mappingDtoFields = new();

    // <Row, Column>
    private static Tuple<int, int> _entityNamePos = null!;

    private string Name { get; init; } = null!;
    private string OriginName { get; init; } = null!;
    private List<ExcelDbEntityField> Fields { get; init; } = [];
    private List<ExcelDbEntityEnum> Enums { get; init; } = [];

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
                        { "Type", GetType(f) },
                        { "MaxLength", f.Length },
                        // Additional
                        { "HasMaxLength", f is { Type: ExcelDbEntityFieldType.Varchar, Length: > 0 } },
                        { "IsRequired", !f.IsNullable },
                        { "HasDefaultValue", f is { Type: ExcelDbEntityFieldType.Varchar, IsNullable: false } },
                        { "Validation", GetValidation(f) },
                        { "Mock", GetMock(f) }
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
                        { "Type", GetType(f) },
                        { "MaxLength", f.Length },
                        // Additional
                        { "HasMaxLength", f is { Type: ExcelDbEntityFieldType.Varchar, Length: > 0 } },
                        { "IsRequired", !f.IsNullable },
                        { "HasDefaultValue", f is { Type: ExcelDbEntityFieldType.Varchar, IsNullable: false } }
                    })
            },
            {
                "Enums", Enums
                    .Select(e => new Dictionary<string, object?>
                    {
                        { "Name", e.Name },
                        { "DisplayName", e.DisplayName },
                        { "EnumValues", e.Values }
                    })
            },
            // Additional
            { "VarName", Name.ToVariableCase() },
            { "NamePlural", Name.Pluralize() },
            { "NamePluralHumanize", OriginName.Pluralize().Humanize(LetterCasing.LowerCase) },
            { "NameSingularHumanize", OriginName.Humanize(LetterCasing.LowerCase) },
            { "ParamValidation", string.Join("\n", entityFields.Select(GetParamValidation)) },
            {
                "Arguments",
                string.Join(", ",
                    entityFields.Select(f => $"{GetType(f)}{(f.IsNullable ? "?" : "")} {f.Name.ToVariableCase()}"))
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
            },
            {
                "ParamInit",
                string.Join(", ",
                    entityFields.Select(
                        f => $"{f.Name} = request.{f.Name}"))
            },
            {
                "ParamInitNonObject",
                string.Join(",\n",
                    entityFields.Select(f => $"{new string(' ', 12)}{f.Name} = {f.Name.ToVariableCase()}"))
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
        _cEnumNo = int.Parse(configuration["Source:Columns:EnumNo"] ?? "-1");
        _cEnumValue = int.Parse(configuration["Source:Columns:EnumValue"] ?? "-1");
        _cEnumDescription = int.Parse(configuration["Source:Columns:EnumDescription"] ?? "-1");
        _modelSuffix = configuration["Generated:ModelSuffix"] ?? "";
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
        var name = GetName(ws);
        var enums = GetEnums(ws, name);

        var entity = new ExcelDbObject
        {
            Name = name,
            OriginName = GetOriginName(ws),
            Fields = GetFields(ws, enums),
            Enums = enums
        };

        return entity;
    }

    private static string GetName(IXLWorksheet ws)
    {
        var nameFromExcel = string.Join("", ws.Cell(_entityNamePos.Item1, _entityNamePos.Item2)
            .GetText()
            .Split(' ')
            .Select((str, idx) => idx == 0 ? str : str.ToVariableCase()));

        return nameFromExcel + _modelSuffix;
    }

    private static string GetOriginName(IXLWorksheet ws)
    {
        var nameFromExcel = string.Join("", ws.Cell(_entityNamePos.Item1, _entityNamePos.Item2)
            .GetText()
            .Split(' ')
            .Select((str, idx) => idx == 0 ? str : str.ToVariableCase()));

        return nameFromExcel;
    }

    private static List<ExcelDbEntityField> GetFields(IXLWorksheet ws, List<ExcelDbEntityEnum> enums)
    {
        var currRow = FindFirstRow(ws);
        var fields = new List<ExcelDbEntityField>();

        while (!ws.Cell(currRow, _cIndex).Value.IsBlank)
        {
            var row = ws.Row(currRow);
            var cLengthValue = row.Cell(_cLength).Value;
            var cDescriptionValue = row.Cell(_cDescription).Value;
            var cDefaultValueValue = row.Cell(_cDefaultValue).Value;

            var index = (int)row.Cell(_cIndex).Value.GetNumber();
            var name = row.Cell(_cName).Value.GetText();
            var enumType = enums.FirstOrDefault(e =>
                string.Equals(e.Name, name, StringComparison.CurrentCultureIgnoreCase));
            var description = cDescriptionValue.IsBlank ? null : cDescriptionValue.GetText();
            var isPrimaryKey = !row.Cell(_cPrimaryKey).Value.IsBlank;
            var isLookup = !row.Cell(_cLookup).Value.IsBlank;
            var isNullable = row.Cell(_cNullable).Value.IsBlank;
            dynamic defaultValue = cDefaultValueValue.Type switch
            {
                XLDataType.Blank => string.Empty,
                XLDataType.Boolean => cDefaultValueValue.GetBoolean(),
                XLDataType.Number => cDefaultValueValue.GetNumber(),
                XLDataType.Text => cDefaultValueValue.GetText(),
                _ => throw new ArgumentOutOfRangeException(nameof(cDefaultValueValue.Type),
                    "Invalid value of cell \"default value\"")
            };
            var type = enumType != null
                ? ExcelDbEntityFieldType.Enum
                : row.Cell(_cType).Value.GetText().ToEnum<ExcelDbEntityFieldType>();
            double? length = cLengthValue.IsNumber ? cLengthValue.GetNumber() : null;

            fields.Add(new ExcelDbEntityField
            {
                Index = index,
                Name = Regex.Replace(name, "/", " ").Dehumanize(),
                Description = description,
                IsPrimaryKey = isPrimaryKey,
                IsLookup = isLookup,
                IsNullable = isNullable,
                DefaultValue = defaultValue,
                Type = type,
                EnumType = enumType,
                Length = length,
            });

            currRow++;
        }

        return fields;
    }

    private static List<ExcelDbEntityEnum> GetEnums(IXLWorksheet ws, string objectName)
    {
        var currRow = FindNextEnumRow(ws, 2);
        var enums = new List<ExcelDbEntityEnum>();

        while (currRow > 0 && !ws.Cell(currRow, _cEnumNo).Value.IsBlank)
        {
            var row = ws.Row(currRow);
            var enumName = Regex.Replace(row.Cell(_cEnumNo).Value.GetText(), "/", " ")
                .ToLower().Dehumanize();

            var @enum = new ExcelDbEntityEnum
            {
                Name = enumName,
                DisplayName = objectName + enumName,
                Values = []
            };

            currRow += 3;

            while (!ws.Cell(currRow, _cEnumNo).Value.IsBlank)
            {
                row = ws.Row(currRow);
                var descriptionValue = row.Cell(_cEnumDescription).Value;
                if (descriptionValue.IsBlank)
                {
                    currRow++;
                    continue;
                }

                var name = descriptionValue.GetText().ToLower().Dehumanize();
                var value = (int)row.Cell(_cEnumValue).Value.GetNumber();

                @enum.Values.Add(KeyValuePair.Create(name, value));

                currRow++;
            }

            enums.Add(@enum);

            currRow = FindNextEnumRow(ws, currRow);
        }

        return enums;
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

    private static int FindNextEnumRow(IXLWorksheet ws, int skipRows)
    {
        var rows = ws.RangeUsed().RowsUsed().Skip(skipRows); // Skip header row
        foreach (var row in rows)
        {
            if (row.Cell(_cEnumNo).Value.IsText && row.Cell(_cEnumValue).Value.IsBlank)
            {
                return row.RowNumber();
            }
        }

        return -1;
    }

    private static string GetType(ExcelDbEntityField field)
    {
        return field.Type switch
        {
            ExcelDbEntityFieldType.Varchar => "string",
            ExcelDbEntityFieldType.Number => "int",
            ExcelDbEntityFieldType.Int => "int",
            ExcelDbEntityFieldType.Decimal => "double",
            ExcelDbEntityFieldType.Timestamp => "DateTimeOffset",
            ExcelDbEntityFieldType.DateTime => "DateTime",
            ExcelDbEntityFieldType.Enum => field.EnumType!.DisplayName,
            ExcelDbEntityFieldType.Boolean => "bool",
            _ => throw new Exception($"Unhandled type: {field}")
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

                if (field.Name.Contains("email", StringComparison.CurrentCultureIgnoreCase))
                {
                    validations.Add("EmailAddress()");
                }

                break;
            case ExcelDbEntityFieldType.Number:
            case ExcelDbEntityFieldType.Timestamp:
            case ExcelDbEntityFieldType.DateTime:
            case ExcelDbEntityFieldType.Decimal:
            case ExcelDbEntityFieldType.Enum:
            case ExcelDbEntityFieldType.Int:
            case ExcelDbEntityFieldType.Boolean:
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

    private static string GetMock(ExcelDbEntityField field)
    {
        var type = GetType(field);

        switch (field.Type)
        {
            case ExcelDbEntityFieldType.Varchar:
            case ExcelDbEntityFieldType.Number:
            case ExcelDbEntityFieldType.Boolean:
            case ExcelDbEntityFieldType.Int:
            case ExcelDbEntityFieldType.Decimal:
                return $"{type} {field.Name.ToVariableCase()} = It.IsAny<{type}>();";

            case ExcelDbEntityFieldType.Timestamp:
            case ExcelDbEntityFieldType.DateTime:
            case ExcelDbEntityFieldType.Enum:
            default:
                return $"var {field.Name.ToVariableCase()} = It.IsAny<{type}>();";
        }
    }

    private static string GetParamValidation(ExcelDbEntityField field)
    {
        var varAbbr = field.Name.ToVariableCase()[0];

        if (field.Type == ExcelDbEntityFieldType.Varchar)
        {
            return $$"""
                             if (!string.IsNullOrEmpty(request.{{field.Name}}))
                             {
                                 Query.Where({{varAbbr}} => {{varAbbr}}.{{field.Name}}.ToLower().Contains(request.{{field.Name}}.ToLower()));
                             }

                     """;
        }

        return $$"""
                         if (request.{{field.Name}} != null)
                         {
                             Query.Where({{varAbbr}} => {{varAbbr}}.{{field.Name}} == request.{{field.Name}});
                         }

                 """;
    }
}
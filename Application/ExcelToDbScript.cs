using System.Text;
using System.Text.RegularExpressions;
using Application.Models;
using ClosedXML.Excel;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using Stubble.Core.Builders;

namespace Application;

public class ExcelToDbScript(IConfiguration config)
{
    private readonly string _pathToExcelFile = config["Source:PathToExcelFile"]!;
    private readonly string _pathGeneration = config["Generated:Path"]!;

    private readonly IEnumerable<string> _includeKeyword =
        config.GetSection("Source:IncludeKeyword").GetChildren().Select(c => c.Value).Where(c => c != null)!;

    private XLWorkbook _wb = null!;

    public async Task Run()
    {
        var sheetsName = SelectSheets();

        await AnsiConsole.Status()
            .StartAsync("Rendering...", async ctx =>
            {
                foreach (var sheetName in sheetsName)
                {
                    ctx.Status($"Rendering {sheetName}");
                    var entity = ExcelDbObject.BuildEntityFromSheet(_wb.Worksheet(sheetName));
                    await Render(entity);
                    AnsiConsole.MarkupLine($"Generated {sheetName}");
                }
            });
    }

    private List<string> SelectSheets()
    {
        var parsableSheets = new List<string>();
        AnsiConsole.Status()
            .Start("Loading file...", _ => { parsableSheets = GetParsableSheets(); });

        var selectedSheets = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("Select sheets to generate")
                .InstructionsText(
                    "[grey](Press [blue]<space>[/] to toggle, " +
                    "[green]<enter>[/] to accept)[/]")
                .AddChoices(parsableSheets));

        return selectedSheets;
    }

    private List<string> GetParsableSheets()
    {
        _wb = new XLWorkbook(_pathToExcelFile);
        var sheets = new List<string>();

        foreach (var ws in _wb.Worksheets)
        {
            var firstCellValue = ws.Cell(1, 1).Value;
            if ((!_includeKeyword.Any() ||
                 _includeKeyword.FirstOrDefault(k =>
                     ws.Name.Contains(k, StringComparison.CurrentCultureIgnoreCase)) != null) &&
                firstCellValue.Type == XLDataType.Text &&
                firstCellValue.GetText() == "HOME")
            {
                sheets.Add(ws.Name);
            }
        }

        sheets.Sort();

        return sheets;
    }

    private async Task Render(ExcelDbObject obj)
    {
        var objDict = obj.ToDictionary();

        await RenderEntity(objDict);
        await RenderDto(objDict);
        await RenderCqrs(objDict);
    }

    private async Task RenderEntity(Dictionary<string, object> objDict)
    {
        var stubble = new StubbleBuilder().Build();
        string template;
        string updateTemplate;

        using (var sr =
            new StreamReader(
                Path.Combine(Directory.GetCurrentDirectory(), @"Templates\ExcelToDb\Entity.mustache"),
                Encoding.UTF8))
        {
            template = await sr.ReadToEndAsync();
        }

        using (var sr =
            new StreamReader(
                Path.Combine(Directory.GetCurrentDirectory(), @"Templates\ExcelToDb\Entity_Update.mustache"),
                Encoding.UTF8))
        {
            updateTemplate = await sr.ReadToEndAsync();
        }

        var output = RemoveRedundantLines(await stubble.RenderAsync(template,
            new
            {
                Entity = objDict,
                EntityNamespace = config["Generated:Entity:Namespace"]
            }, new Dictionary<string, string>
            {
                { "Update", updateTemplate }
            }));

        var folderPath = Path.Combine(_pathGeneration, "Entities", $"{objDict.GetValueOrDefault("Name")}.cs");
        new FileInfo(folderPath).Directory?.Create(); // If the directory already exists, this method does nothing.

        await using (var sw = new StreamWriter(folderPath, !File.Exists(folderPath)))
        {
            await sw.WriteLineAsync(output);
        }
    }

    private async Task RenderDto(Dictionary<string, object> objDict)
    {
        var stubble = new StubbleBuilder().Build();
        string template;

        using (var sr =
            new StreamReader(
                Path.Combine(Directory.GetCurrentDirectory(), @"Templates\ExcelToDb\Dto.mustache"),
                Encoding.UTF8))
        {
            template = await sr.ReadToEndAsync();
        }

        var output = RemoveRedundantLines(await stubble.RenderAsync(template,
            new
            {
                Entity = objDict,
                DtoNamespace = config["Generated:Dto:Namespace"]
            }));

        var folderPath = Path.Combine(_pathGeneration, "Dtos", $"{objDict.GetValueOrDefault("Name")}Dto.cs");
        new FileInfo(folderPath).Directory?.Create(); // If the directory already exists, this method does nothing.

        await using (var sw = new StreamWriter(folderPath, !File.Exists(folderPath)))
        {
            await sw.WriteLineAsync(output);
        }
    }

    private async Task RenderCqrs(Dictionary<string, object> objDict)
    {
        var name = objDict.GetValueOrDefault("Name");
        var view = new
        {
            Entity = objDict,
            IdType = config["Generated:Entity:IdType"],
            EntityNamespace = config["Generated:Entity:Namespace"],
            DtoNamespace = config["Generated:Dto:Namespace"],
            CqrsNamespace = config["Generated:Cqrs:Namespace"],
            ValidationNamespace = config["Generated:Validation:Namespace"],
        };

        // Template name, output folder name, output file name
        List<Tuple<string, string, string>> taskList =
        [
            Tuple.Create("GetByIdQuery", $@"Cqrs\{name}\Queries", $"Get{name}ByIdQuery.cs"),
            Tuple.Create("GetByConditionQuery", $@"Cqrs\{name}\Queries", $"Get{name}ByConditionQuery.cs"),
            Tuple.Create("BaseCommand", $@"Cqrs\{name}", $"I{name}Command.cs"),
            Tuple.Create("Validation", $@"Cqrs\{name}", $"{name}ValidationRules.cs"),
        ];

        var stubble = new StubbleBuilder().Build();

        foreach (var task in taskList)
        {
            string template;

            using (var sr =
                new StreamReader(
                    Path.Combine(Directory.GetCurrentDirectory(), $@"Templates\ExcelToDb\{task.Item1}.mustache"),
                    Encoding.UTF8))
            {
                template = await sr.ReadToEndAsync();
            }

            var output = RemoveRedundantLines(await stubble.RenderAsync(template, view));

            var folderPath = Path.Combine(_pathGeneration, task.Item2, task.Item3);
            new FileInfo(folderPath).Directory?.Create(); // If the directory already exists, this method does nothing.

            await using (var sw = new StreamWriter(folderPath, !File.Exists(folderPath)))
            {
                await sw.WriteLineAsync(output);
            }
        }
    }

    private static string RemoveRedundantLines(string str)
    {
        str = new Regex(@"\{\r\n\s*\[").Replace(str, "{\r\n    [");

        var emptyLineFromAttributesRegex = new Regex(@"\]\r\n\s*\r\n");
        var m = emptyLineFromAttributesRegex.Match(str);

        while (m.Success)
        {
            str = emptyLineFromAttributesRegex.Replace(str, "]\r\n");
            m = emptyLineFromAttributesRegex.Match(str);
        }

        var emptyLineFromFieldsRegex = new Regex(@"\r\n\r\n *\r\n *\[");
        m = emptyLineFromFieldsRegex.Match(str);

        while (m.Success)
        {
            str = emptyLineFromFieldsRegex.Replace(str, "\r\n\r\n    [");
            m = emptyLineFromFieldsRegex.Match(str);
        }

        return str;
    }
}
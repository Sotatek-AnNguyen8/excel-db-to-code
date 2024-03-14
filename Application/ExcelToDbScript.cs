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

        Console.ReadLine();
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
            if (firstCellValue.Type == XLDataType.Text && firstCellValue.GetText() == "HOME")
            {
                sheets.Add(ws.Name);
            }
        }

        sheets.Sort();

        return sheets;
    }

    private async Task Render(ExcelDbObject obj)
    {
        await RenderEntity(obj);
    }

    private async Task RenderEntity(ExcelDbObject obj)
    {
        var stubble = new StubbleBuilder().Build();
        var entity = obj.ToDictionary();
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
                Entity = entity,
                EntityNamespace = config["Generated:Entity:Namespace"]
            }, new Dictionary<string, string>
            {
                { "Update", updateTemplate }
            }));

        var folderPath = Path.Combine(_pathGeneration, "Entities", $"{obj.Name}.cs");
        new FileInfo(folderPath).Directory?.Create(); // If the directory already exists, this method does nothing.

        await using (var sw = new StreamWriter(folderPath, !File.Exists(folderPath)))
        {
            await sw.WriteLineAsync(output);
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
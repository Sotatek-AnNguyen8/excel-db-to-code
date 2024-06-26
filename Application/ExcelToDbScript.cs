﻿using System.Text;
using System.Text.RegularExpressions;
using Application.Models;
using ClosedXML.Excel;
using Humanizer;
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
            .StartAsync("Generating...", async ctx =>
            {
                foreach (var sheetName in sheetsName)
                {
                    ctx.Status($"Generating {sheetName}");
                    var entity = ExcelDbObject.BuildEntityFromSheet(_wb.Worksheet(sheetName));
                    var obj = entity.ToDictionary();
                    await GenerateCode(obj);
                    await GenerateTests(obj);
                    AnsiConsole.MarkupLine($"Generated {sheetName}");
                }
            });
        
        AnsiConsole.MarkupLine($"\nSee result in {_pathGeneration}");
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

    private async Task GenerateCode(Dictionary<string, object> obj)
    {
        await GenerateEnum(obj);
        await GenerateEntity(obj);
        await GenerateDto(obj);
        await GenerateCqrs(obj);
        await GenerateController(obj);
    }

    private async Task GenerateTests(Dictionary<string, object> obj)
    {
        await GenerateCqrsTest(obj);
    }

    private async Task GenerateEnum(Dictionary<string, object> objDict)
    {
        var stubble = new StubbleBuilder().Build();
        string template;

        using (var sr =
               new StreamReader(
                   Path.Combine(Directory.GetCurrentDirectory(), "Templates", "ExcelToDb", "Enum.mustache"),
                   Encoding.UTF8))
        {
            template = await sr.ReadToEndAsync();
        }

        var output = RemoveRedundantLines(await stubble.RenderAsync(template, new { Entity = objDict }));

        var folderPath = Path.Combine(_pathGeneration, "Enums", $"{objDict.GetValueOrDefault("Name")}.cs");
        new FileInfo(folderPath).Directory?.Create(); // If the directory already exists, this method does nothing.

        await using (var sw = new StreamWriter(folderPath, !File.Exists(folderPath)))
        {
            await sw.WriteLineAsync(output);
        }
    }

    private async Task GenerateEntity(Dictionary<string, object> objDict)
    {
        var stubble = new StubbleBuilder().Build();
        string template;
        string updateTemplate;
        string createTemplate;

        using (var sr =
               new StreamReader(
                   Path.Combine(Directory.GetCurrentDirectory(), "Templates", "ExcelToDb", "Entity.mustache"),
                   Encoding.UTF8))
        {
            template = await sr.ReadToEndAsync();
        }

        using (var sr =
               new StreamReader(
                   Path.Combine(Directory.GetCurrentDirectory(), "Templates", "ExcelToDb", "Entity_Update.mustache"),
                   Encoding.UTF8))
        {
            updateTemplate = await sr.ReadToEndAsync();
        }

        using (var sr =
               new StreamReader(
                   Path.Combine(Directory.GetCurrentDirectory(), "Templates", "ExcelToDb", "Entity_Create.mustache"),
                   Encoding.UTF8))
        {
            createTemplate = await sr.ReadToEndAsync();
        }

        var output = RemoveRedundantLines(await stubble.RenderAsync(template,
            new
            {
                Entity = objDict,
                EntityNamespace = config["Generated:Entity:Namespace"],
                IdType = config["Generated:Entity:IdType"]
            }, new Dictionary<string, string>
            {
                { "Update", updateTemplate },
                { "Create", createTemplate }
            }));

        var folderPath = Path.Combine(_pathGeneration, "Entities", $"{objDict.GetValueOrDefault("Name")}.cs");
        new FileInfo(folderPath).Directory?.Create(); // If the directory already exists, this method does nothing.

        await using (var sw = new StreamWriter(folderPath, !File.Exists(folderPath)))
        {
            await sw.WriteLineAsync(output);
        }
    }

    private async Task GenerateDto(Dictionary<string, object> objDict)
    {
        var stubble = new StubbleBuilder().Build();
        string template;

        using (var sr =
               new StreamReader(
                   Path.Combine(Directory.GetCurrentDirectory(), "Templates", "ExcelToDb", "Dto.mustache"),
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

    private async Task GenerateCqrs(Dictionary<string, object> objDict)
    {
        var name = (string)objDict.GetValueOrDefault("Name")!;
        var pluralName = name.Pluralize();
        var view = new
        {
            Entity = objDict,
            IdType = config["Generated:Entity:IdType"],
            EntityNamespace = config["Generated:Entity:Namespace"],
            DtoNamespace = config["Generated:Dto:Namespace"],
            CqrsNamespace = config["Generated:Cqrs:Namespace"],
            ValidationNamespace = config["Generated:Validation:Namespace"],
            ParamNamespace = config["Generated:Param:Namespace"]
        };

        // Template name, output folder name, output file name
        List<Tuple<string, string, string>> taskList =
        [
            // Commands
            Tuple.Create("BaseCommand", Path.Combine("Cqrs", pluralName, "Commands"), $"{name}BaseCommand.cs"),
            Tuple.Create("CreateCommand", Path.Combine("Cqrs", pluralName, "Commands"), $"Create{name}Command.cs"),
            Tuple.Create("UpdateCommand", Path.Combine("Cqrs", pluralName, "Commands"), $"Update{name}Command.cs"),
            Tuple.Create("DeleteCommand", Path.Combine("Cqrs", pluralName, "Commands"), $"Delete{name}Command.cs"),
            Tuple.Create("ExportCommand", Path.Combine("Cqrs", pluralName, "Commands"), $"Export{name}Command.cs"),
            // Queries
            Tuple.Create("GetByIdQuery", Path.Combine("Cqrs", pluralName, "Queries"), $"Get{name}ByIdQuery.cs"),
            Tuple.Create("GetByConditionQuery", Path.Combine("Cqrs", pluralName, "Queries"),
                $"Get{name}ByConditionQuery.cs"),
            // Specs
            Tuple.Create("GetByConditionSpec", Path.Combine("Cqrs", pluralName, "Specs"), $"{name}ByConditionSpec.cs"),
            Tuple.Create("GetByIdSpec", Path.Combine("Cqrs", pluralName, "Specs"), $"{name}ByIdSpec.cs"),
            Tuple.Create("ExportCommandSpec", Path.Combine("Cqrs", pluralName, "Specs"), $"Export{name}CommandSpec.cs"),
            // Validators
            Tuple.Create("ValidatorBase", Path.Combine("Cqrs", pluralName, "Validators"), $"{name}ValidatorBase.cs"),
            Tuple.Create("CreateValidator", Path.Combine("Cqrs", pluralName, "Validators"),
                $"Create{name}CommandValidator.cs"),
            Tuple.Create("DeleteValidator", Path.Combine("Cqrs", pluralName, "Validators"),
                $"Delete{name}CommandValidator.cs"),
            Tuple.Create("UpdateValidator", Path.Combine("Cqrs", pluralName, "Validators"),
                $"Update{name}CommandValidator.cs"),
            // Params
            Tuple.Create("SearchParam", Path.Combine("Cqrs", pluralName, "Params"), $"Search{name}Param.cs")
        ];

        var stubble = new StubbleBuilder().Build();

        foreach (var task in taskList)
        {
            string template;

            using (var sr =
                   new StreamReader(
                       Path.Combine(Directory.GetCurrentDirectory(), "Templates", "ExcelToDb",
                           $"{task.Item1}.mustache"),
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

    private async Task GenerateController(Dictionary<string, object> objDict)
    {
        var stubble = new StubbleBuilder().Build();
        string template;

        using (var sr =
               new StreamReader(
                   Path.Combine(Directory.GetCurrentDirectory(), "Templates", "ExcelToDb", "Controller.mustache"),
                   Encoding.UTF8))
        {
            template = await sr.ReadToEndAsync();
        }

        var output = RemoveRedundantLines(await stubble.RenderAsync(template,
            new
            {
                Entity = objDict,
                ControllerNamespace = config["Generated:Controller:Namespace"],
                CqrsNamespace = config["Generated:Cqrs:Namespace"],
                IdType = config["Generated:Entity:IdType"]
            }));

        var folderPath = Path.Combine(_pathGeneration, "Controllers",
            $"{objDict.GetValueOrDefault("Name")}Controller.cs");
        new FileInfo(folderPath).Directory?.Create(); // If the directory already exists, this method does nothing.

        await using (var sw = new StreamWriter(folderPath, !File.Exists(folderPath)))
        {
            await sw.WriteLineAsync(output);
        }
    }

    private async Task GenerateCqrsTest(Dictionary<string, object> objDict)
    {
        var name = (string)objDict.GetValueOrDefault("Name")!;
        var pluralName = name.Pluralize();
        var view = new
        {
            Entity = objDict,
            EntityNamespace = config["Generated:Entity:Namespace"],
            DtoNamespace = config["Generated:Dto:Namespace"],
            CqrsNamespace = config["Generated:Cqrs:Namespace"],
            ValidationNamespace = config["Generated:Validation:Namespace"],
            TestNamespace = config["Generated:Test:Namespace"]
        };

        // Template name, output folder name, output file name
        List<Tuple<string, string, string>> taskList =
        [
            Tuple.Create("GetByIdQuery", Path.Combine("Test", "Cqrs", pluralName, "Queries"),
                $"Get{name}ByIdQueryTest.cs"),
            Tuple.Create("CreateCommand", Path.Combine("Test", "Cqrs", pluralName, "Commands"),
                $"Create{name}CommandTest.cs"),
            Tuple.Create("UpdateCommand", Path.Combine("Test", "Cqrs", pluralName, "Commands"),
                $"Update{name}CommandTest.cs"),
            Tuple.Create("DeleteCommand", Path.Combine("Test", "Cqrs", pluralName, "Commands"),
                $"Delete{name}CommandTest.cs"),
            Tuple.Create("Builder", Path.Combine("Test", "Cqrs", pluralName, "Builders"), $"{name}Builder.cs")
        ];

        var stubble = new StubbleBuilder().Build();

        foreach (var task in taskList)
        {
            string template;

            using (var sr = new StreamReader(
                       Path.Combine(Directory.GetCurrentDirectory(), "Templates", "ExcelToDb", "Tests",
                           $"{task.Item1}.mustache"), Encoding.UTF8))
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
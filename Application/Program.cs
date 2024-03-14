using Application;
using Application.Models;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

IConfiguration config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json")
    .Build();

ExcelDbObject.Init(config);

AnsiConsole.Write(
    new FigletText(Constants.AppName)
        .LeftJustified());

var type = AnsiConsole.Prompt(
    new SelectionPrompt<string>()
        .Title("What do you want to generate?")
        .AddChoices(Constants.GenerateType.ExcelDbToCode));

AnsiConsole.Clear();

switch (type)
{
    case Constants.GenerateType.ExcelDbToCode:
        await new ExcelToDbScript(config).Run();
        break;
}
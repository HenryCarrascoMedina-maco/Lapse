using System.Text;
using Lapse.Cli;

Console.OutputEncoding = Encoding.UTF8;
return await LapseCommands.Create().Parse(args).InvokeAsync();

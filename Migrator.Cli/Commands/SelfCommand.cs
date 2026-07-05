using System;

internal static class SelfCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        var command = args[0].Trim().ToLowerInvariant();
        return command switch
        {
            "update" => RunUpdate(args[1..]),
            _ => UnknownCommand(command)
        };
    }

    static int RunUpdate(string[] args)
    {
        if (args.Length > 0 && args[0] is not "--print-command")
        {
            Console.Error.WriteLine($"Unknown option for self update: {args[0]}");
            PrintHelp();
            return 2;
        }

        var report = InstallDoctorCommand.CreateInstallReport();
        Console.WriteLine("SELF_UPDATE_COMMAND");
        Console.WriteLine(report.RecommendedUpdateCommand);
        Console.WriteLine();
        Console.WriteLine($"Detected channel: {report.Channel}");
        Console.WriteLine("Run the printed command in your shell. The migrator does not mutate global npm/dotnet/standalone installs automatically.");
        return 0;
    }

    static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown self command: {command}");
        PrintHelp();
        return 2;
    }

    static bool IsHelp(string value) => value is "--help" or "-h" or "help";

    static void PrintHelp()
    {
        Console.WriteLine("""
Usage:
  selenium-pw-migrator self update [--print-command]

Commands:
  update        Print the update command for the currently detected install channel.

Examples:
  selenium-pw-migrator doctor install
  selenium-pw-migrator self update
""");
    }
}

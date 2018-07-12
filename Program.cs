using System;
using System.Linq;
using commands;

namespace rules
{
    class Program
    {
        static void Main(string[] args)
        {
            var run = true;
            
            while (run)
            {
                Console.WriteLine("Enter command (help to display commands)");
                var command = Parser.Parse(Console.ReadLine());
                if(command == null)
                {
                    run = true;
                }
                else{
                        run = command.Execute();
                }
            }
        }
    }

    public static class Parser
    {
        public static ICommand Parse (string commandString)
        {
            var commandParts = commandString.Split(' ').ToList();
            var commandName = commandParts[0];
            switch (commandName)
            {
                case "help": return new HelpCommand();
                case "exit": return new ExitCommand();
                case "run" : return new RulesCommand();
                case "lookup":return new LookupCommand();
                default: 
                    Console.WriteLine(string.Format("Command {0} not found.  Please check the spelling and try again.",commandName));
                    return null;
            }

        }
    }


}

using System;
using System.IO;
using System.Text;
using commands;


namespace rules
{
    class HelpCommand:ICommand
    {
        public bool Execute()
        {
            Console.WriteLine("exit - exit");
            Console.WriteLine("run - run rules command");
            Console.WriteLine("lookup - run lookup command");

            return true;
        }

        public bool Execute (string args)
        {
            return Execute();
        }

        public string Name()
        {
            return "Help Command";
        }
    }
}
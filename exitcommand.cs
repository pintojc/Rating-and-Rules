using System;
using commands;



namespace rules
{
    public class ExitCommand:ICommand
    {
        public bool Execute()
        {
            return false;
        }

        public bool Execute (string args)
        {
            return Execute();
        }

        public string Name ()
        {
            return "Exit Command";
        }
    }



}



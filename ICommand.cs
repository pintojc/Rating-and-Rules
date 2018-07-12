using System;


namespace commands
{
    public interface ICommand
    {
        bool Execute();

        bool Execute (string args);
        string Name();

        
    }

}
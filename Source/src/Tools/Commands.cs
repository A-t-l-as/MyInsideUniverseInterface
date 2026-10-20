// Registration of the console commands implemented outside Program.cs.
namespace InterfaceTools
{
    public static partial class Extra
    {
        static partial void RegisterAll()
        {
            Register("dds", Dds.Run);
            RegisterPipeline();
            Gui = global::InterfaceTools.Gui.MainForm.Run;
        }

        static partial void RegisterPipeline();
    }
}

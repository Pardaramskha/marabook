using System;
using System.IO;
using System.Windows;

namespace UniversSale
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Settings.AppSettings.Load();
            View.Chrome.Toggle(Settings.AppSettings.DarkTheme);
            var application = new Application();
            View.Theme.Apply(application);
            var window = new MainWindow();
            if (args.Length > 0 && File.Exists(args[0]))
                window.OpenFile(args[0]);
            application.Run(window);
        }
    }
}

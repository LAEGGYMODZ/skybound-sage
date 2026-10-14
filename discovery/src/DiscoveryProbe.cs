using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SkyboundSageFocused
{
    public sealed class DiscoveryProbe : CSharpModBase.ICSharpMod
    {
        private bool initialized;
        public string Name { get { return "SkyboundSageFocused"; } }
        public string Version { get { return "0.4.0"; } }
        public void Init()
        {
            if(initialized) return; initialized=true;
            try {
                CatalogResult result=new ApiCatalog().Capture(AppDomain.CurrentDomain.GetAssemblies());
                string directory=ReportDirectory();
                // Open both destinations before either write to avoid replacing an earlier report.
                using(FileStream api=File.Open(Path.Combine(directory,"SkyboundSageFocused-api.tsv"),FileMode.CreateNew,FileAccess.Write,FileShare.Read))
                using(FileStream summary=File.Open(Path.Combine(directory,"SkyboundSageFocused-summary.txt"),FileMode.CreateNew,FileAccess.Write,FileShare.Read)) {
                    Write(api,result.ReportText()); Write(summary,result.SummaryText());
                }
                Log("Focused metadata saved. Selected types="+result.SelectedTypes+", rows="+result.Rows.Count+", limited="+result.Limited+". Combat hooks/effects and controller reads remain off.");
            } catch(Exception error) { Log("Focused discovery incomplete: "+error.GetType().Name+". No game action was invoked by this module."); }
        }
        public void DeInit() { initialized=false; Log("No event subscriptions or modifiers were created by focused discovery."); }
        private static string ReportDirectory()
        {
            string executable;
            using(Process process=Process.GetCurrentProcess()) executable=process.MainModule.FileName;
            if(!String.Equals(Path.GetFileName(executable),"b1-Win64-Shipping.exe",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
            string binaries=Path.GetDirectoryName(executable),loader=Path.Combine(binaries,"CSharpLoader"),data=Path.Combine(loader,"Data");
            foreach(string directory in new[]{binaries,loader,data})
                if(!Directory.Exists(directory)||(File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0) throw new IOException();
            return data;
        }
        private static void Write(Stream stream,string text) { byte[] data=new UTF8Encoding(false).GetBytes(text); stream.Write(data,0,data.Length); }
        private static void Log(string text) { try { Console.WriteLine("[SkyboundSageFocused] "+text); } catch(Exception) { } }
    }
}

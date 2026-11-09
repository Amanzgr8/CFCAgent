using System.Runtime.InteropServices;

namespace CFCAgent.App.Core;

// One line describing where the app is running: the operating system, whether the process is
// native (for example Arm64) or emulated, and the .NET version. It lives in the UI-free layer so
// the window only has to display it, and it works the same on every operating system.
public static class RuntimeSummary
{
    public static string Describe()
    {
        return $"{RuntimeInformation.OSDescription} | process: {RuntimeInformation.ProcessArchitecture} | " +
               $"machine: {RuntimeInformation.OSArchitecture} | {RuntimeInformation.FrameworkDescription}";
    }
}

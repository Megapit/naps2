namespace NAPS2.Tools.Project.Packaging;

public static class MsixPackager
{
    public static void PackageMsix(Func<PackageInfo> pkgInfoFunc, bool noSign)
    {
        Output.Verbose("Building binaries");
        Cli.Run("dotnet", "clean NAPS2.App.Worker -c Release");
        Cli.Run("dotnet", "clean NAPS2.App.WinForms -c Release");
        Cli.Run("dotnet", "clean NAPS2.App.Console -c Release");
        Cli.Run("dotnet",
            "publish NAPS2.App.Worker -c Release /p:DebugType=None /p:DebugSymbols=false /p:DefineConstants=MSI");
        Cli.Run("dotnet",
            "publish NAPS2.App.WinForms -c Release /p:DebugType=None /p:DebugSymbols=false /p:DefineConstants=MSI");
        Cli.Run("dotnet",
            "publish NAPS2.App.Console -c Release /p:DebugType=None /p:DebugSymbols=false /p:DefineConstants=MSI");

        var pkgInfo = pkgInfoFunc();

        var msixPath = pkgInfo.GetPath("msix");
        var msixTestPath = msixPath.Replace(".msix", "-test.msix");
        Output.Info($"Packaging msix installer: {msixPath}");

        if (File.Exists(msixPath))
        {
            File.Delete(msixPath);
        }
        if (File.Exists(msixTestPath))
        {
            File.Delete(msixTestPath);
        }

        var manifestPath = Path.Combine(Paths.SetupObj, "appxmanifest.xml");
        File.Copy(Path.Combine(Paths.SetupWindows, "appxmanifest.xml"), manifestPath, true);
        var logoPath = Path.Combine(Paths.SolutionRoot, "NAPS2.Lib", "Icons", "scanner-128.png");
        var publishDir = Path.Combine(Paths.SolutionRoot, "NAPS2.App.WinForms", "bin", "Release", "net9", "win-x64",
            "publish");
        var mappingFilePath = Path.Combine(Paths.SetupObj, "msixmapping.txt");

        var mappingFile = new StreamWriter(new FileStream(mappingFilePath, FileMode.Create));
        mappingFile.WriteLine("[Files]");
        foreach (var file in pkgInfo.Files)
        {
            var fullSourcePath = Path.Combine(publishDir, file.SourcePath);
            mappingFile.WriteLine($"\"{fullSourcePath}\" \"{file.DestPath}\"");
        }
        mappingFile.WriteLine($"\"{manifestPath}\" \"AppxManifest.xml\"");
        mappingFile.WriteLine($"\"{logoPath}\" \"scanner.png\"");
        mappingFile.Close();

        var makeAppx = @"C:\Program Files (x86)\Windows Kits\10\App Certification Kit\makeappx.exe";

        Cli.Run(makeAppx, $"pack /f \"{mappingFilePath}\" /p \"{msixPath}\"");

        File.WriteAllText(manifestPath, File.ReadAllText(manifestPath).Replace(
            "CN=1D624E39-8523-4AAC-B3B6-1452E653A003",
            N2Config.WindowsIdentity));

        Cli.Run(makeAppx, $"pack /f \"{mappingFilePath}\" /p \"{msixTestPath}\"");

        if (!noSign)
        {
            Output.Verbose("Signing test installer");
            WindowsSigning.SignFile(msixTestPath);
        }

        Output.OperationEnd($"Packaged msix installer: {msixPath}");
    }
}
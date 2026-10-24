using System;
using System.IO;
internal static class ClientStub
{
    private static void Main(string[] args)
    { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stub-result.txt"), Environment.CurrentDirectory + "\n" + args.Length); }
}

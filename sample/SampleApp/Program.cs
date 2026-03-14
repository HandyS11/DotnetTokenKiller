// Program.cs — handles --fail arg for integration test coverage
if (args.Contains("--fail"))
{
    Console.WriteLine("App starting...");
    Environment.Exit(1);
}

Console.WriteLine("Hello from SampleApp!");

// Entry point — valid so the build succeeds with warnings.
Console.WriteLine("SampleApp.Warnings");

#pragma warning disable CS0219 // Variable is assigned but its value is never used
var unused = 42;
#pragma warning restore CS0219

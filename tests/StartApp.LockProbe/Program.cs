using StartApp.Storage.Json;
// Test helper: take the single-writer lock, announce, hold for N ms, exit.
using var js = new JsonStore(args[0]);
js.Mutate(c => c.CreateWorkspace("probe"));
Console.WriteLine("LOCKED"); Console.Out.Flush();
Thread.Sleep(int.Parse(args[1]));

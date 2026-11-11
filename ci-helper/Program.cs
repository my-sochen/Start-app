// CI-only marker-only helper. It does nothing except write one marker file (path from env STARTAPP_HELPER_MARKER) and exit.
var marker = Environment.GetEnvironmentVariable("STARTAPP_HELPER_MARKER");
if (!string.IsNullOrEmpty(marker)) File.WriteAllText(marker, $"helper-started args={Environment.GetCommandLineArgs().Length - 1} pid={Environment.ProcessId}");
return 0;

using ZeppelinForms.LanguageServer;
using ZeppelinForms.LanguageServer.Protocol;

// The protocol on stdin and stdout; anything written for people goes to stderr, which the
// editors show in their output panel — a stray Console.WriteLine would break the stream.
TextWriter log = Console.Error;
Console.SetOut(log);

var rpc = new JsonRpc(Console.OpenStandardInput(), Console.OpenStandardOutput());

log.WriteLine($"[zf-ls] ZeppelinForms language server {typeof(Server).Assembly.GetName().Version?.ToString(3)} on .NET {Environment.Version}");

return await new Server(rpc, log).RunAsync(CancellationToken.None);

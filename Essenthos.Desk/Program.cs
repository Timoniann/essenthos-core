using Essenthos.Core.Desk;
using System.Text;

// The owner's console: what is waiting on him, the review lists he decides, the portraits and the
// runs that apply what he decided. It listens on the loopback and nowhere else, is never deployed,
// and writes only the files the loaders already read.
Console.OutputEncoding = Encoding.UTF8;

await using var app = DeskApplication.Build(args);
await app.RunAsync();

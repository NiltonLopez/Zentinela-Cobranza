// Genera el hash compatible con ASP.NET Identity para una contraseña dada.
// Uso: dotnet run --project tools/HashGen -- "<password>"
using Microsoft.AspNetCore.Identity;

if (args.Length < 1)
{
    Console.Error.WriteLine("Uso: HashGen <password>");
    return 1;
}

var hasher = new PasswordHasher<object>();
var hash = hasher.HashPassword(new object(), args[0]);
Console.WriteLine(hash);
return 0;

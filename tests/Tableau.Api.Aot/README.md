# Tableau.Api.Aot

The native AOT test. An app, not a test project: only publishing an executable runs the native AOT compiler, and only
the compiler sees the whole program. The analyzers that `IsAotCompatible` turns on check the library's own code, file by
file; the compiler checks what the library's code reaches.

CI (`ci.yml`, the Native AOT job) does two things:

1. **Publishes it with native AOT.** The 2.0 types `roots.xml` lists are kept whole, used by the app or not, so the
   compiler analyses every member of each. Any trim or AOT warning fails the publish.
2. **Runs the binary,** which exits with 1 on the first failure (`Program.cs`): every fixture of the offline tests,
   read and written back through the generated models, and the shapes Tableau's JSON takes; the client's calls over a
   handler that answers from the fixtures, its version negotiated and an error read; and the client from DI, its
   options bound from configuration and bad ones refused at start-up. Reflection-based JSON is off in the binary, so
   a converter that reached for it fails there.

**What it keeps until the old client goes:** 2.0 shares the root namespace `Tableau.Api` with the old client, whose
`XmlSerializer` and Newtonsoft.Json code can never pass, so `roots.xml` lists only 2.0's types: the namespace
`Tableau.Api.Rest.Models` whole, which only the generated models, their converters and the JSON context are in, and
each other 2.0 type by its full name, such as `Tableau.Api.TableauClient`. `ModelRootsTests` in the offline tests fails on a public type in neither `roots.xml`
nor its list of the old client's types. Once the old client is gone, the whole library is kept and the list goes.

## Run it

Needs the native AOT
[prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites). On Linux:

```
dotnet restore tests/Tableau.Api.Aot
dotnet publish tests/Tableau.Api.Aot --no-restore -c Release -r linux-x64 -o artifacts/aot
artifacts/aot/Tableau.Api.Aot
```

Restore first: `publish -r` would restore every project for that runtime, which their lock files don't hold. On
Windows, publish with `-r win-x64` and no `--no-restore`, from a shell where `vswhere.exe` is on the path, such as a
Developer PowerShell; the linker needs it. `dotnet run --project tests/Tableau.Api.Aot` runs the same checks without
native AOT.

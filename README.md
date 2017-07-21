# Simple DNS

Simple DNS essentially functions the same as a regular hosts file, however it 
allows for more complicated domain matching (via Regular Expressions) 
rather than fixed domains.

### Example Simple DNS file entries

Simple DNS record entries must be prefixed with `#@` and can be placed into any
regular file, including your existing hosts file without issue.

```txt
#@  \.?example\.com$        192.168.56.100  # Maps all domains belonging to example.com to 192.168.56.100
#@  ^google\.com$           192.168.56.100  # Maps *only* the google.com domain to 192.168.56.100 (maps.google.com would be ignored)
```

### Building and running the project

The project is built with [.NET Core 1.1](https://www.microsoft.com/net/download/core "Microsoft .NET Core") and the solution file must be opened with VS2017 or newer.
Simple DNS can be built and run from the command line on both Linux and Windows systems.

To build the project run the following commands from the project root (the same directory as SimpleDns.sln)

```bash
SimpleDns$ dotnet restore && dotnet build
```

To run the project on Windows

```cmd
dotnet run ^
    --configuration=Release ^
    --project=src\SimpleDns\SimpleDns.csproj ^
    -- ^
    "C:\Windows\System32\drivers\etc\hosts"
```

Or on Linux

```bash
dotnet run \
    --configuration=Release \
    --project=src/SimpleDns/SimpleDns.csproj \
    -- \
    "/etc/hosts"
```

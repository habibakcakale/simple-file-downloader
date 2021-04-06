#Simple File Downloader
## Build
- `-c Release` for Release and optimized mode.
- `-r osx-64` is runtime identifier.
- `-p:PublishReadyToRun=true` - Some kind of AOT compilation that improves startup time.
- `-p:PublishTrimmed=true` Trim unnecessary dll imports if not used.
``` 
dotnet publish -c Release -r osx-x64 -p:PublishReadyToRun=true -p:PublishTrimmed=true --self-contained=true
```

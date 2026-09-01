### Add migration

```sh
dotnet ef migrations add Init
```

### Update database

```sh
dotnet ef database update
```

### Suppress EF Core logging. Add the following to `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.Hosting.Lifetime": "Information",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
    }
  }
}
```
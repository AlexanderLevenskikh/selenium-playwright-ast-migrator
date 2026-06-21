# Navigation URL mapping

`Navigation.OpenPage<T>(Urls.X)` is parsed as `NavigationAction`, not as a generic `MethodInvocationAction`.
Therefore `ParameterizedMethods` does not apply to it.

Use `NavigationUrls` to map source-only URL constants to target URL values:

```json
{
  "NavigationUrls": {
    "Urls.BaseUrlPartners": "catalogs?activeTab=partners&type=simple",
    "Urls.BaseUrlDebt": "debt"
  }
}
```

By default, mapped navigation still renders through the renderer fallback:

```csharp
await Page.GotoAsync("debt");
```

For projects with their own helper, set `NavigationTargetStatement`:

```json
{
  "NavigationUrls": {
    "Urls.BaseUrlDebt": "debt"
  },
  "NavigationTargetStatement": "await GoToAsync({url});"
}
```

Generated output:

```csharp
await GoToAsync("debt");
```

`{url}` is replaced with a C# string expression. Plain config values are converted to string literals. Values already written as C# string literals are preserved.

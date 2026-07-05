# Package manager templates

The primary supported distribution channels are standalone GitHub Release assets, npm wrapper, and dotnet tool. Scoop and Homebrew templates are provided as follow-up packaging starting points, not as published package-manager taps/buckets yet.

## Scoop template

Template file:

```text
package-managers/scoop/selenium-pw-migrator.json
```

Before publishing to a Scoop bucket:

1. Replace `0.0.0-preview.8` with the release version.
2. Replace `TODO_SHA256_WIN_X64` with the SHA256 from `checksums.sha256`.
3. Commit the manifest to a Scoop bucket repository.
4. Smoke on Windows:

```powershell
scoop install selenium-pw-migrator
selenium-pw-migrator --version
```

## Homebrew formula template

Template file:

```text
package-managers/homebrew/selenium-pw-migrator.rb
```

Before publishing to a Homebrew tap:

1. Replace the version and URL if needed.
2. Replace `TODO_SHA256_OSX_ARM64` and `TODO_SHA256_OSX_X64` with release checksums.
3. Commit the formula to a tap repository.
4. Smoke on macOS:

```bash
brew install selenium-pw-migrator
selenium-pw-migrator --version
```

Do not treat these templates as production package-manager releases until the checksums have been copied from the published GitHub Release and the install smoke passes.

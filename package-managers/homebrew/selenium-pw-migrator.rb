# Template formula. Replace TODO sha256 values with the published checksums.sha256 values
# before adding this formula to a Homebrew tap.
class SeleniumPwMigrator < Formula
  desc "Selenium to Playwright AST migration CLI"
  homepage "https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator"
  version "0.0.0-preview.8"
  license "MIT"

  on_macos do
    if Hardware::CPU.arm?
      url "https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/download/v0.0.0-preview.8/selenium-pw-migrator-0.0.0-preview.8-osx-arm64.tar.gz"
      sha256 "TODO_SHA256_OSX_ARM64"
    else
      url "https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/download/v0.0.0-preview.8/selenium-pw-migrator-0.0.0-preview.8-osx-x64.tar.gz"
      sha256 "TODO_SHA256_OSX_X64"
    end
  end

  def install
    libexec.install Dir["*"]
    bin.install_symlink libexec/"selenium-pw-migrator" => "selenium-pw-migrator"
  end

  test do
    system "#{bin}/selenium-pw-migrator", "--version"
  end
end
